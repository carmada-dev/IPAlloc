using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace IPAlloc.Functions
{
    public class Health
    {
        [Function(nameof(Health))]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData request)
            => request.CreateResponse(HttpStatusCode.OK);
    }
}
