using System.Net;
using System.Security.Claims;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace IPAlloc.Middleware;

internal sealed class AuthorizationMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var principal = context.Features.Get<ClaimsPrincipalFeature>()?.Principal;

        if (Authorize(context, principal))
        {
            await next(context);
        }
        else
        {
            await context.SendStatusResponseAsync(HttpStatusCode.Forbidden);
        }
    }

    private bool Authorize(FunctionContext context, ClaimsPrincipal? principal)
    {
        var principalRoles = principal?
            .FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct();

        if (principalRoles?.Any() ?? false)
        {
            return context.GetFunctionAllowRoles().Intersect(principalRoles).Any()
                && !context.GetFunctionDenyRoles().Intersect(principalRoles).Any();
        }
        else
        {
            return !context.GetFunctionAllowRoles().Any()
                && !context.GetFunctionDenyRoles().Any();
        }
        
    }
}
