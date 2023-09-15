using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Net;

using IPAlloc.Authorization;
using IPAlloc.Middleware;
using IPAlloc.Model;
using IPAlloc.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

using static IPAlloc.Services.RunnerService;

namespace IPAlloc.Functions;

[Allow("Runner")]
internal class Allocation
{
    private readonly ILogger logger;
    private readonly IDistributedLockManager distributedLockManager;
    private readonly AllocationRepository allocationRepository;
    private readonly RunnerService runnerService;

    public Allocation(ILogger<Allocation> logger, IDistributedLockManager distributedLockManager, AllocationRepository allocationRepository, RunnerService runnerService)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.distributedLockManager = distributedLockManager ?? throw new ArgumentNullException(nameof(distributedLockManager));
        this.allocationRepository = allocationRepository ?? throw new ArgumentNullException(nameof(allocationRepository));
        this.runnerService = runnerService ?? throw new ArgumentNullException(nameof(runnerService));
    }

    [Function(nameof(Allocation))]
    public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", "delete", Route = "allocation/{key}")] HttpRequestData request, FunctionContext context, Guid key)
    {
        try
        {
            logger.LogInformation($"Received {request.Method} request for key '{key}'.");

            switch (request.Method)
            {
                case "POST":
                    return await PostAsync(request, context, key);
                case "GET":
                    return await GetAsync(request, context, key);
                case "DELETE":
                    return await DeleteAsync(request, context, key);
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

    private async Task<HttpResponseData> DeleteAsync(HttpRequestData request, FunctionContext context, Guid key)
    {
        await allocationRepository.DeletePartitionAsync(key.ToString());

        return request.CreateResponse(HttpStatusCode.OK);
    }

    private async Task<HttpResponseData> GetAsync(HttpRequestData request, FunctionContext context, Guid key)
    {
        var subnets = await allocationRepository
            .GetPartitionAsync(key.ToString())
            .Select(allocation => allocation.Network)
            .ToArrayAsync();

        return await request.CreateJsonResponseAsync(HttpStatusCode.OK, subnets);
    }

    private async Task<HttpResponseData> PostAsync(HttpRequestData request, FunctionContext context, Guid key)
    {
        var cidrs = request.Query
            .GetValues("cidr")?
            .Select(cidr => byte.TryParse(cidr, out var cidrParsed) ? cidrParsed : throw new ArgumentException($"Failed to parse cidr value '{cidr}'."))
            ?? Enumerable.Empty<byte>();

        if (!cidrs.Any())
            throw new ArgumentException($"Missing mandatory query parameter 'cidr'.");
        else if (!cidrs.All(cidr => cidr >= 0 && cidr <= 32))
            throw new ArgumentException($"At least one 'cidr' query parameter is out of range (0-32)");

        var envs = request.Query
            .GetValues("env")?
            .Where(env => !string.IsNullOrWhiteSpace(env))
            .ToArray() ?? Array.Empty<string>();

        Runner? envRunner = default;

        if (envs.Length > 1)
        {
            throw new ArgumentException($"Only one 'env' query parameter is allowed.");
        }
        else if (envs.Length == 0)
        {
            var principalId = context.Features.Get<ClaimsPrincipalFeature>()?.GetObjectId()
                ?? throw new ArgumentException($"Missing principal identifier in context.");

            var runners = await runnerService
                .GetAsync(principalId)
                .ToArrayAsync();

            if (runners.Length == 0)
                throw new ArgumentException($"Principal '{principalId}' is no valid runner in project '{RunnerService.ProjectResourceId}'.");
            else if (runners.Length > 1)
                throw new ArgumentException($"Principal '{principalId}' maps to multiple environment types ({string.Join(", ", runners)}) in project '{RunnerService.ProjectResourceId}'. Please specify the environment type to use by using the 'env' query parameter.");
            else
                envRunner = runners.Single();
        }
        else
        {
            var principalId = context.Features.Get<ClaimsPrincipalFeature>()?.GetObjectId()
                ?? throw new ArgumentException($"Missing principal identifier in context.");

            envRunner = await runnerService
                .GetAsync(envs.First());

            if (envRunner is null)
                throw new ArgumentException($"No runner found for environment type '{envs.First()}' in project '{RunnerService.ProjectResourceId}'.");
            else if (!envRunner.Value.PrincipalId.Equals(principalId))
                throw new ArgumentException($"Runner '{principalId}' is not linked to environment '{envs.First()}' in project '{RunnerService.ProjectResourceId}'.");
        }

        var syncLock = await distributedLockManager.AcquireDistributedLock(nameof(Allocation), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        logger.LogInformation($"Acquired lock '{syncLock.LockId}' for allocation key '{key}'.");

        try
        {
            var allocations = await allocationRepository
                .GetAllAsync()
                .ToArrayAsync();

            var allocationsNew = new List<AllocationEntity>();

            foreach (var cidr in cidrs)
            {
                var network = envRunner.Value.Included
                    .SelectMany(included => included.Subnet(cidr).Where(subnet => !envRunner.Value.Excluded.Any(excluded => excluded.Overlap(subnet))))
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
                            Environment = envs.First()
                        }));

                        logger.LogInformation($"Allocated network '{network}' for allocation key '{key}' in environment type '{envs.First()}'.");
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
