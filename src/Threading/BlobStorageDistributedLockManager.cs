using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

using Microsoft.Azure.WebJobs.Host;

namespace IPAlloc.Threading
{
    public sealed class BlobStorageDistributedLockManager : IDistributedLockManager
    {
        private const string OWNERID_METADATA = "OwnerId";
        private const string CONTAINER_NAME = "distributed-locks";

        private static readonly ConcurrentDictionary<string, BlobContainerClient> BlobContainerClientCache = new ConcurrentDictionary<string, BlobContainerClient>(StringComparer.OrdinalIgnoreCase);

        public Task<bool> RenewAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            LockHandle lockHandleTyped = (LockHandle)lockHandle;
            return lockHandleTyped.RenewAsync(cancellationToken);
        }

        public async Task ReleaseLockAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            LockHandle lockHandleTyped = (LockHandle)lockHandle;
            await ReleaseLeaseAsync(lockHandleTyped.BlobLeaseClient, lockHandleTyped.LeaseId, cancellationToken);
        }

        public async Task<string> GetLockOwnerAsync(string account, string lockId, CancellationToken cancellationToken)
        {
            var lockBlob = GetContainerClient().GetBlobClient(GetLockPath(lockId));
            var blobProperties = await ReadLeaseBlobMetadataAsync(lockBlob, cancellationToken);

            // if the lease is Available, then there is no current owner
            // (any existing owner value is the last owner that held the lease)
            if (blobProperties != null &&
                blobProperties.LeaseState == LeaseState.Available &&
                blobProperties.LeaseStatus == LeaseStatus.Unlocked)
            {
                return null;
            }

            string owner = default;
            blobProperties?.Metadata.TryGetValue(OWNERID_METADATA, out owner);
            return owner;
        }

        public async Task<IDistributedLock> TryLockAsync(string account, string lockId, string lockOwnerId, string proposedLeaseId, TimeSpan lockPeriod, CancellationToken cancellationToken)
        {
            var lockBlob = GetContainerClient().GetBlobClient(GetLockPath(lockId));
            var leaseId = await TryAcquireLeaseAsync(lockBlob, lockPeriod, proposedLeaseId, cancellationToken);

            if (string.IsNullOrEmpty(leaseId))
                return null;

            if (!string.IsNullOrEmpty(lockOwnerId))
                await WriteLeaseBlobMetadataAsync(lockBlob, leaseId, lockOwnerId, cancellationToken);

            var lockHandle = new LockHandle(leaseId, lockId, lockBlob.GetBlobLeaseClient(leaseId), lockPeriod);

            return lockHandle;
        }

        private BlobContainerClient GetContainerClient()
        {
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            return BlobContainerClientCache.GetOrAdd($"{CONTAINER_NAME}@{connectionString}", _ =>
            {
                var blobContainerClient = new BlobContainerClient(connectionString, CONTAINER_NAME);

                blobContainerClient.CreateIfNotExistsAsync().Wait();

                return blobContainerClient;
            });
        }

        private static string GetLockPath(string lockId) => $"locks/{lockId}";

        private static async Task<string> TryAcquireLeaseAsync(BlobClient blobClient, TimeSpan leasePeriod, string proposedLeaseId, CancellationToken cancellationToken)
        {
            bool blobDoesNotExist;

            try
            {
                // Check if a lease is available before trying to acquire. The blob may not
                // yet exist; if it doesn't we handle the 404, create it, and retry below.
                // The reason we're checking to see if the lease is available before trying
                // to acquire is to avoid the flood of 409 errors that Application Insights
                // picks up when a lease cannot be acquired due to conflict; see issue #2318.

                var blobProperties = await ReadLeaseBlobMetadataAsync(blobClient, cancellationToken);

                switch (blobProperties?.LeaseState)
                {
                    case null:
                    case LeaseState.Available:
                    case LeaseState.Expired:
                    case LeaseState.Broken:

                        var leaseResponse = await blobClient
                            .GetBlobLeaseClient(proposedLeaseId)
                            .AcquireAsync(leasePeriod, cancellationToken: cancellationToken);
                        
                        return leaseResponse.Value.LeaseId;

                    default:
                        return null;
                }
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 409)
                {
                    return null;
                }
                else if (exception.Status == 404)
                {
                    blobDoesNotExist = true;
                }
                else
                {
                    throw;
                }
            }

            if (blobDoesNotExist)
            {
                await TryCreateAsync(blobClient, cancellationToken);

                try
                {
                    var leaseResponse = await blobClient
                        .GetBlobLeaseClient(proposedLeaseId)
                        .AcquireAsync(leasePeriod, cancellationToken: cancellationToken);

                    return leaseResponse.Value.LeaseId;
                }
                catch (RequestFailedException exception)
                {
                    if (exception.Status == 409)
                    {
                        return null;
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return null;
        }

        private static async Task ReleaseLeaseAsync(BlobLeaseClient blobLeaseClient, string leaseId, CancellationToken cancellationToken)
        {
            try
            {
                // Note that this call returns without throwing if the lease is expired. See the table at:
                // http://msdn.microsoft.com/en-us/library/azure/ee691972.aspx

                await blobLeaseClient.ReleaseAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 404 || exception.Status == 409)
                {
                    // there is nothing to release !!!
                    // either because the blob no longer exists, or there is another lease.
                }
                else
                {
                    throw;
                }
            }
        }

        private static async Task<bool> TryCreateAsync(BlobClient blobClient, CancellationToken cancellationToken)
        {
            bool isContainerNotFoundException;

            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Empty));
                await blobClient.UploadAsync(stream, cancellationToken: cancellationToken);
                return true;
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 404)
                {
                    isContainerNotFoundException = true;
                }
                else if (exception.Status == 409 || exception.Status == 412)
                {
                    return false; // The blob already exists, or is leased by someone else
                }
                else
                {
                    throw;
                }
            }

            Debug.Assert(isContainerNotFoundException);

            var container = blobClient.GetParentBlobContainerClient();

            try
            {
                await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status == 409 && string.Compare("ContainerBeingDeleted", exception.ErrorCode) == 0)
            {
                throw new RequestFailedException("The host container is pending deletion and currently inaccessible.");
            }

            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Empty));
                await blobClient.UploadAsync(stream, cancellationToken: cancellationToken);
                return true;
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 409 || exception.Status == 412)
                {
                    return false; // The blob already exists, or is leased by someone else
                }
                else
                {
                    throw;
                }
            }
        }

        private static async Task WriteLeaseBlobMetadataAsync(BlobClient blobClient, string leaseId, string lockOwnerId, CancellationToken cancellationToken)
        {
            var blobProperties = await ReadLeaseBlobMetadataAsync(blobClient, cancellationToken);

            if (blobProperties != null)
            {
                blobProperties.Metadata[OWNERID_METADATA] = lockOwnerId;
                await blobClient.SetMetadataAsync(blobProperties.Metadata, new BlobRequestConditions { LeaseId = leaseId }, cancellationToken: cancellationToken);
            }
        }

        private static async Task<BlobProperties> ReadLeaseBlobMetadataAsync(BlobClient blobClient, CancellationToken cancellationToken)
        {
            try
            {
                var propertiesResponse = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                return propertiesResponse.Value;
            }
            catch (RequestFailedException exception)
            {
                if (exception.Status == 404)
                {
                    return null; // the blob no longer exists
                }
                else
                {
                    throw;
                }
            }
        }

        internal class LockHandle : IDistributedLock
        {
            private readonly TimeSpan leasePeriod;

            public LockHandle() { }

            public LockHandle(string leaseId, string lockId, BlobLeaseClient blobLeaseClient, TimeSpan leasePeriod)
            {
                this.LeaseId = leaseId;
                this.LockId = lockId;
                this.leasePeriod = leasePeriod;
                this.BlobLeaseClient = blobLeaseClient;
            }

            public string LeaseId { get; internal set; }

            public string LockId { get; internal set; }

            public BlobLeaseClient BlobLeaseClient { get; internal set; }

            public async Task<bool> RenewAsync(CancellationToken cancellationToken)
            {
                try
                {
                    await BlobLeaseClient.RenewAsync(cancellationToken: cancellationToken);
                    return true; // The next execution should occur after a normal delay.
                }
                catch (RequestFailedException exception)
                {
                    if (exception.Status >= 500 && exception.Status < 600)
                    {
                        return false; // The next execution should occur more quickly (try to renew the lease before it expires).
                    }
                    else
                    {
                        throw; // If we've lost the lease or cannot re-establish it, we want to fail any in progress function execution
                    }
                }
            }
        }
    }
}
