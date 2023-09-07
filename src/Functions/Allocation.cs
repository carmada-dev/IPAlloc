using System.Data;
using System.Net;
using System.Threading;

using IPAlloc.Model;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Core;
using Microsoft.WindowsAzure.Storage.Table;

namespace IPAlloc.Functions
{
    public class Allocation
    {
        private readonly ILogger logger;
        private readonly IDistributedLockManager distributedLockManager;
        private readonly AllocationRepository allocationRepository;

        public Allocation(ILogger<Allocation> logger, IDistributedLockManager distributedLockManager, AllocationRepository allocationRepository)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.distributedLockManager = distributedLockManager ?? throw new ArgumentNullException(nameof(distributedLockManager));
            this.allocationRepository = allocationRepository ?? throw new ArgumentNullException(nameof(allocationRepository));
        }

        [Function(nameof(Allocation))]
        public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", "delete", Route = "allocation/{key}")] HttpRequestData request, Guid key)
        {
            try
            {
                logger.LogInformation($"Received {request.Method} request for key '{key}'.");

                switch (request.Method)
                {
                    case "POST":
                        return await PostAsync(request, key);
                    case "GET":
                        return await GetAsync(request, key);
                    case "DELETE":
                        return await DeleteAsync(request, key);
                    default:
                        throw new ArgumentException($"Unsupported HTTP method '{request.Method}'");
                }
            }
            catch (ArgumentException exc)
            {
                logger.LogError(exc, "Failed to allocate IP address.");

                return await request.CreateErrorResponseAsync(HttpStatusCode.BadRequest, exc);
            }
            catch (Exception exc)
            {
                logger.LogError(exc, "Failed to allocate IP address.");

                return await request.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, exc);
            }
        }

        private async Task<HttpResponseData> DeleteAsync(HttpRequestData request, Guid key)
        {
            await allocationRepository.DeletePartitionAsync(key.ToString());

            return request.CreateResponse(HttpStatusCode.OK);
        }

        private async Task<HttpResponseData> GetAsync(HttpRequestData request, Guid key)
        {
            var subnets = await allocationRepository
                .GetPartitionAsync(key.ToString())
                .Select(allocation => allocation.Network)
                .ToArrayAsync();

            return await request.CreateJsonResponseAsync(HttpStatusCode.OK, subnets);
        }

        private async Task<HttpResponseData> PostAsync(HttpRequestData request, Guid key)
        {
            var cidrs = request.Query
                .GetValues("cidr")?
                .Select(cidr => byte.TryParse(cidr, out var cidrParsed) ? cidrParsed : throw new ArgumentException($"Failed to parse cidr value '{cidr}'."))
                ?? Enumerable.Empty<byte>();

            if (!cidrs.Any())
                throw new ArgumentException($"Missing mandatory query parameter 'cidr'.");
            else if (!cidrs.All(cidr => cidr >= 0 && cidr <= 32))
                throw new ArgumentException($"At least one 'cidr' query parameter is out of range (0-32)");

            var env = request.Query
                .GetValues("env")?
                .FirstOrDefault();

            if (env is null)
                throw new ArgumentException($"Missing mandatory query parameter 'env'.");
            else if (string.IsNullOrWhiteSpace(env))
                throw new ArgumentException($"Query parameter 'env' must not be empty.");

            var syncLock = await distributedLockManager.AcquireDistributedLock(nameof(Allocation), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
            logger.LogInformation($"Acquired lock '{syncLock.LockId}' for allocation key '{key}'.");

            var ipPool = IPPool.Get(env);
            logger.LogDebug($"IP pool -> {ipPool}");

            try
            {
                var allocations = await allocationRepository
                    .GetAllAsync()
                    .ToArrayAsync();

                var allocationsNew = new List<AllocationEntity>();

                foreach (var cidr in cidrs)
                {
                    var network = ipPool.Included
                        .SelectMany(included => included.Subnet(cidr).Where(subnet => !ipPool.Excluded.Any(excluded => excluded.Overlap(subnet))))
                        .FirstOrDefault(subnet => !allocations.Concat(allocationsNew).Any(allocation => allocation.Network.Overlap(subnet)));

                    if (network is null)
                    {
                        if (allocationsNew.Any())
                            await allocationRepository.DeleteAsync(allocationsNew);

                        throw new ApplicationException($"Could not allocate subnet with mask {cidr}.");
                    }
                    else
                    {
                        try
                        {
                            allocationsNew.Add(await allocationRepository.InsertAsync(new AllocationEntity()
                            {
                                Key = key,
                                Network = network,
                                Environment = env
                            }));

                            logger.LogInformation($"Allocated network '{network}' for allocation key '{key}' in environment type '{env}'.");
                        }
                        catch 
                        {
                            if (allocationsNew.Any())
                                await allocationRepository.DeleteAsync(allocationsNew);

                            throw;
                        }
                    }
                }

                return await request.CreateJsonResponseAsync(HttpStatusCode.OK, allocationsNew.Select(allocation => allocation.Network));
            }
            finally
            {
                await distributedLockManager.ReleaseLockAsync(syncLock, CancellationToken.None);
                logger.LogInformation($"Released lock '{syncLock.LockId}' for allocation key '{key}'.");
            }
        }
    }
}
