using System.Net;
using System.Threading;

using IPAlloc.Model;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace IPAlloc.Functions
{
    public class Allocate
    {
        private readonly ILogger logger;
        private readonly AllocationRepository allocationRepository;

        public Allocate(ILoggerFactory loggerFactory, AllocationRepository allocationRepository)
        {
            this.logger = loggerFactory?.CreateLogger<Allocate>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.allocationRepository = allocationRepository ?? throw new ArgumentNullException(nameof(allocationRepository));
        }

        [Function(nameof(Allocate))]
        public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
        {
            try
            {
                var cidrs = request.Query
                    .GetValues("cidr")?
                    .Select(cidr => byte.TryParse(cidr, out var cidrParsed) ? cidrParsed : throw new ArgumentException($"Could not parse cidr value '{cidr}' as unsigned integer."))
                    ?? Enumerable.Empty<byte>();

                if (!cidrs.All(cidr => cidr >= 0 && cidr <= 32))
                    throw new ArgumentException($"At least one requested mask is out of range (0-32)");
                else if (!cidrs.Any())
                    throw new ArgumentException($"Missing mandatory query parameter 'mask'.");

                using var syncLock = new SemaphoreSlim(1, 1);

                var subnets = cidrs
                    .Select(cidr => AllocateSubnetAsync(cidr, syncLock))
                    .WaitAll();

                return await request.CreateJsonResponseAsync(HttpStatusCode.OK, subnets);
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

        private IEnumerable<IPNetwork> GetNetworks()
        {
            var networks = new[] {
                "10.0.200.0/24",
                "192.168.0.0/16"
            };

            return networks.Select(network => IPNetwork.Parse(network));
        }   

        private async Task<IPNetwork?> AllocateSubnetAsync(byte cidr, SemaphoreSlim syncLock)
        {
            syncLock.Wait();

            try
            {
                foreach (var network in GetNetworks())
                {
                    var allocations = await allocationRepository
                        .GetPartitionAsync(network)
                        .ToListAsync();

                    var subnet = network.Subnet(cidr).FirstOrDefault(subnet =>
                    {
                        var overlap = allocations.Any(allocation => allocation.NetworkAllocation?.Overlap(subnet) ?? false);

                        if (overlap)
                            logger.LogInformation($"Skip subnet {subnet} as it overlaps with at least one allocated subnet.");

                        return !overlap;
                    });

                    if (subnet is not null)
                    {
                        logger.LogInformation($"Allocating subnet {subnet} in network pool {network}.");

                        var allocation = new AllocationEntity()
                        {
                            NetworkAllocation = subnet,
                            NetworkPool = network
                        };

                        allocations.Add(await allocationRepository.InsertAsync(allocation));

                        return subnet;
                    }
                }

                return null;
            }
            finally
            {
                syncLock.Release();
            }
        }
    }
}
