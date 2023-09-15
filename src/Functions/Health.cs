using System.Net;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace IPAlloc.Functions;

internal class Health
{
    private readonly ILogger logger;

    public Health(ILogger<Health> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function(nameof(Health))]
    public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request)
        => request.CreateResponse(HttpStatusCode.OK);
}
