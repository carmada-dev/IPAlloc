using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

using Azure.Core;
using Azure.Identity;

using IPAlloc.Services;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace IPAlloc.Middleware;

internal sealed class AuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private readonly JwtSecurityTokenHandler tokenValidator = new JwtSecurityTokenHandler();
    private readonly ILogger<AuthenticationMiddleware> logger;
    private readonly TokenService tokenService;
    private readonly RunnerService runnerService;

    public AuthenticationMiddleware(ILogger<AuthenticationMiddleware> logger, TokenService tokenService, RunnerService runnerService)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        this.runnerService = runnerService ?? throw new ArgumentNullException(nameof(runnerService));
    }

    private static bool TryGetTokenFromHeaders(FunctionContext context, out string? token)
    {
        token = default;

        if (context.BindingContext.BindingData.TryGetValue("Headers", out var value) && value is string header)
        {
            var headers = JsonSerializer
                .Deserialize<Dictionary<string, string>>(header)?
                .ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value) ?? new Dictionary<string, string>();

            if (headers.TryGetValue("authorization", out var authHeaderValue) && authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = authHeaderValue.Substring("Bearer ".Length).Trim();
        }

        return !string.IsNullOrEmpty(token);
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!TryGetTokenFromHeaders(context, out var token))
        {
            context.Features.Set(ClaimsPrincipalFeature.Empty);

            await next(context);
        }
        else if (!tokenValidator.CanReadToken(token))
        {
            await context.SendStatusResponseAsync(HttpStatusCode.BadRequest);
        }
        else
        {
            try
            {
                var securityToken = tokenService.GetSecurityToken();

                var validationParameters = new TokenValidationParameters()
                {
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,

                    IssuerSigningKeys = tokenService.GetSigningKeys(),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5),

                    ValidateAudience = false,
                    ValidAudience = securityToken.Claims.First(c => c.Type.Equals("aud")).Value,

                    ValidateIssuer = true,
                    ValidIssuer = securityToken.Claims.First(c => c.Type.Equals("iss")).Value,
                };

                var principal = tokenValidator.ValidateToken(token, validationParameters, out _);

                if (principal.Identity is ClaimsIdentity claimsIdentity)
                {
                    if (await runnerService.GetAsync().AnyAsync(runner => runner.PrincipalId.Equals(securityToken.GetObjectId())))
                        claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, "Runner"));
                }

                context.Features.Set(new ClaimsPrincipalFeature()
                {
                    Principal = principal,
                    Token = token
                });

                await next(context);
            }
            catch (SecurityTokenException exc)
            {
                logger.LogError(exc, "Token authentication failed");

                await context.SendStatusResponseAsync(HttpStatusCode.Unauthorized);
            }
        }
    }
}
