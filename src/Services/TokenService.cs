using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;

using Azure.Core;
using Azure.Identity;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace IPAlloc.Services;

internal sealed class TokenService
{
    public const string DefaultScope = "https://management.azure.com/.default";

    private static DefaultAzureCredentialOptions GetAzureCredentialOptions()
    {
        var options = new DefaultAzureCredentialOptions()
        {
            AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,

            // Exclude the following credential types
            // to improve performance and avoid issues

            ExcludeSharedTokenCacheCredential = true,
            ExcludeAzureDeveloperCliCredential = true,
            ExcludeInteractiveBrowserCredential = true,
            ExcludeAzurePowerShellCredential = true,
            ExcludeAzureCliCredential = true,

            // Exclude the following credential types
            // if in DEBUG mode to support Azure Functions
            // local development and debugging
#if DEBUG
            ExcludeVisualStudioCodeCredential = false,
            ExcludeVisualStudioCredential = false,
#else
            ExcludeVisualStudioCodeCredential = true,
            ExcludeVisualStudioCredential = true,
#endif

            // Include the following credential types
            ExcludeEnvironmentCredential = false,
            ExcludeManagedIdentityCredential = false,
        };

        var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
        
        if (!string.IsNullOrEmpty(tenantId))
            options.TenantId = tenantId;

        if (Debugger.IsAttached)
            options.Diagnostics.IsLoggingContentEnabled = true;

        return options;
    }

    private static readonly DefaultAzureCredential DefaultCredential = new DefaultAzureCredential(GetAzureCredentialOptions());

    private static readonly JwtSecurityTokenHandler SecurityTokenHandler = new JwtSecurityTokenHandler();
    private static readonly ConcurrentDictionary<string, OpenIdConnectConfiguration> OpenIdConnectConfigurationCache = new ConcurrentDictionary<string, OpenIdConnectConfiguration>();

    private readonly ILogger<TokenService> logger;
    private readonly IMemoryCache cache;

    public TokenService(ILogger<TokenService> logger, IMemoryCache cache)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public AccessToken GetAccessToken(string scope = DefaultScope) => cache.GetOrCreate<AccessToken>($"{nameof(TokenService)}@{scope}", entry =>
    {
        var accessToken = DefaultCredential.GetToken(new TokenRequestContext(new[] { scope }));

        entry.AbsoluteExpiration = accessToken.ExpiresOn.Subtract(TimeSpan.FromSeconds(30));
        entry.SetPriority(CacheItemPriority.High);

        return accessToken;
    });

    public string GetBearerToken(string scope = DefaultScope) 
        => GetAccessToken(scope).Token;

    public JwtSecurityToken GetSecurityToken(string scope = DefaultScope)
        => SecurityTokenHandler.ReadJwtToken(GetAccessToken(scope).Token);

    private OpenIdConnectConfiguration? GetOpenIdConnectConfiguration(string scope = DefaultScope)
    {
        var tenantId = GetSecurityToken(scope).GetTenantId();

        return tenantId is null ? null : OpenIdConnectConfigurationCache.GetOrAdd(tenantId.Value.ToString(), _ => 
        {
            var metadataAddress = $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration";
            return new ConfigurationManager<OpenIdConnectConfiguration>(metadataAddress, new OpenIdConnectConfigurationRetriever()).GetConfigurationAsync(default).Result;
        });
    }

    public ICollection<SecurityKey> GetSigningKeys(string scope = DefaultScope) 
        => GetOpenIdConnectConfiguration(scope)?.SigningKeys ?? new Collection<SecurityKey>();

    public Guid? GetObjectId(string scope = DefaultScope)
        => GetSecurityToken(scope).GetObjectId();

    public Guid? GetTenantId(string scope = DefaultScope)
        => GetSecurityToken(scope).GetTenantId();

    public Guid? GetClientId(string scope = DefaultScope)
        => GetSecurityToken(scope).GetClientId();
}
