using System.Net;
using System.Text.RegularExpressions;

using Azure.Core;
using Azure.Identity;

using Flurl;
using Flurl.Http;
using Flurl.Util;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IPAlloc.Services;

internal sealed class RunnerService
{
    public static string ProjectResourceId 
        => Environment.GetEnvironmentVariable("PROJECT_RESOURCEID") 
        ?? string.Empty;

    private static readonly Regex IPNetworkExpression = new Regex(@"^(?<qualifier>[!]?)(?<ip>(?:\d{1,3}\.){3}\d{1,3})\/(?<mask>[0-9]|[1-2][0-9]|3[0-2])$", RegexOptions.Compiled);

    private static IEnumerable<IPNetwork> ParseNetworks(string? environmentIPPools, bool included)
    {
        if (string.IsNullOrWhiteSpace(environmentIPPools))
            yield break;    

        var ipPools = environmentIPPools
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(x => IPNetworkExpression.Match(x))
            .Where(m => m.Success);

        foreach (var ipPool in ipPools)
        {
            var ip = IPAddress.Parse(ipPool.Groups["ip"].Value);
            var mask = int.Parse(ipPool.Groups["mask"].Value);
            var qualifier = ipPool.Groups["qualifier"].Value;

            // A qualifier of ! means exclude this pool
            if (qualifier.Equals("!") == !included)
                yield return IPNetwork.Parse($"{ip}/{mask}");
        }
    }

    private const string ResourceManagerEndpoint = "https://management.azure.com";

    private readonly ILogger<RunnerService> logger;
    private readonly IMemoryCache cache;
    private readonly TokenService tokenService;

    public RunnerService(ILogger<RunnerService> logger, IMemoryCache cache, TokenService tokenService)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
    }

    public async Task<Runner?> GetAsync(string runnerName)
        => await GetAsync().SingleOrDefaultAsync(x => x.EnvironmentType.Equals(runnerName, StringComparison.OrdinalIgnoreCase));

    public IAsyncEnumerable<Runner> GetAsync(Guid principalId)
        => GetAsync().Where(x => x.PrincipalId.Equals(principalId));

    public async IAsyncEnumerable<Runner> GetAsync()
    {
        if (!string.IsNullOrEmpty(ProjectResourceId))
        {
            var json = await cache.GetOrCreateAsync(ProjectResourceId, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.SetPriority(CacheItemPriority.High);

                return ResourceManagerEndpoint
                    .AppendPathSegments(ProjectResourceId, "environmentTypes")
                    .SetQueryParam("api-version", "2022-11-11-preview")
                    .WithOAuthBearerToken(tokenService.GetBearerToken())
                    .GetJObjectAsync();
            });

            foreach (var value in (json["value"] ?? new JArray()))
            {
                var environmentType = value["name"]?.ToString();

                if (!string.IsNullOrEmpty(environmentType) && value["identity"] is JObject identity && identity is not null)
                {
                    var runnerIPPoolName = $"IPPOOL_{environmentType.Trim()}".ToUpperInvariant();
                    var runnerIPPool = Environment.GetEnvironmentVariable(runnerIPPoolName);

                    yield return new Runner()
                    {
                        EnvironmentType = environmentType,
                        PrincipalId = Guid.TryParse(identity["principalId"]?.ToString(), out var principalId) ? principalId : Guid.Empty,
                        TenantId = Guid.TryParse(identity["tenantId"]?.ToString(), out var tenantId) ? tenantId : Guid.Empty,
                        Included = ParseNetworks(runnerIPPool, true),
                        Excluded = ParseNetworks(runnerIPPool, false)
                    };
                }
            }

#if DEBUG

            var localEnvironmentType = "local";
            var localRunnerIPPoolName = $"IPPOOL_{localEnvironmentType.Trim()}".ToUpperInvariant();
            var localRunnerIPPool = Environment.GetEnvironmentVariable(localRunnerIPPoolName);

            yield return new Runner()
            {
                EnvironmentType = localEnvironmentType,
                PrincipalId = tokenService.GetObjectId() ?? Guid.Empty,
                TenantId = tokenService.GetTenantId() ?? Guid.Empty,
                Included = ParseNetworks(localRunnerIPPool, true),
                Excluded = ParseNetworks(localRunnerIPPool, false)
            };

#endif
        }
    }

    internal record struct Runner(string EnvironmentType, Guid PrincipalId, Guid TenantId, IEnumerable<IPNetwork> Included, IEnumerable<IPNetwork> Excluded)
    {
        public override string ToString()
        {
            const string SEPERATOR = ", ";

            var networks = new string[] {
                string.Join(SEPERATOR, Included.Select(x => $"{x}")),
                string.Join(SEPERATOR, Excluded.Select(x => $"!{x}"))
            };

            return $"{EnvironmentType} ({PrincipalId}/{TenantId}): {string.Join(SEPERATOR, networks.Where(x => !string.IsNullOrEmpty(x)))}";
        }
    }
}
