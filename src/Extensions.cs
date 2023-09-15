using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;

using Flurl;
using Flurl.Http;

using IPAlloc.Authorization;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage;

using Newtonsoft.Json.Linq;

namespace IPAlloc;

internal static class Extensions
{
    private static readonly PropertyInfo CloudStorageAccount_IsDevStoreAccountProperty = typeof(CloudStorageAccount)
        .GetProperty("IsDevStoreAccount", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo CloudStorageAccount_SettingsProperty = typeof(CloudStorageAccount)
        .GetProperty("Settings", BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool IsDevelopmentStorageAccount(this CloudStorageAccount cloudStorageAccount)
    {
        if (cloudStorageAccount is null)
            throw new ArgumentNullException(nameof(cloudStorageAccount));

        return (bool)CloudStorageAccount_IsDevStoreAccountProperty.GetValue(cloudStorageAccount);
    }

    public static IDictionary<string, string> GetSettings(this CloudStorageAccount cloudStorageAccount)
    {
        if (cloudStorageAccount is null)
            throw new ArgumentNullException(nameof(cloudStorageAccount));

        return (IDictionary<string, string>)CloudStorageAccount_SettingsProperty.GetValue(cloudStorageAccount);
    }

    public static async Task<IDistributedLock> AcquireDistributedLock(this IDistributedLockManager distributedLockManager, string lockId, TimeSpan lockTimeout, TimeSpan acquisitionTimeout, CancellationToken cancellationToken = default)
    {
        var distributedLock = await distributedLockManager.TryLockAsync(null, lockId, null, null, lockTimeout, cancellationToken);
        var acquisitionDeadline = DateTime.UtcNow + acquisitionTimeout;

        while (distributedLock is null && DateTime.UtcNow <= acquisitionDeadline)
        {
            await Task.Delay(100); // give an existing lock 100 msec to be released before trying again

            distributedLock = await distributedLockManager.TryLockAsync(null, lockId, null, null, lockTimeout, cancellationToken);
        }

        return distributedLock ?? throw new TimeoutException($"Failed to acquire lock for id '{lockId}' within {acquisitionTimeout.TotalSeconds} sec.");
    }

    private static async Task<HttpResponseData> CreateJsonResponseInternalAsync(HttpRequestData request, HttpStatusCode statusCode, object payload)
    {
        var response = request.CreateResponse();

        await response.WriteAsJsonAsync(payload, statusCode);

        return response;
    }

    public static Task<HttpResponseData> CreateJsonResponseAsync<T>(this HttpRequestData request, HttpStatusCode statusCode, T data)
    {
        var payload = new DataPayload<T>
        {
            Data = data
        };

        return CreateJsonResponseInternalAsync(request, statusCode, payload);
    }

    public static Task<HttpResponseData> CreateErrorResponseAsync(this HttpRequestData request, HttpStatusCode statusCode, Exception exception)
    {
        var payload = new ErrorPayload
        {
            Type = exception.GetType().Name,
            Message = exception.Message
        };

        return CreateJsonResponseInternalAsync(request, statusCode, payload);
    }

    public static HttpResponseData CreateStatusResponse(this HttpRequestData request, HttpStatusCode statusCode)
    {
        var response = request.CreateResponse();

        response.StatusCode = statusCode;

        return response;
    }

    public static async Task SendStatusResponseAsync(this FunctionContext context, HttpStatusCode statusCode)
    {
        var request = await context.GetHttpRequestDataAsync();
        var response = request.CreateStatusResponse(statusCode);

        context.GetInvocationResult().Value = response;
    }

    public sealed class ErrorPayload
    {
        public string Type { get; set; }
        public string Message { get; set; }
    }

    public sealed class DataPayload<T>
    {
        public T Data { get; set; }
    }

    public static IEnumerable<T> WaitAll<T>(this IEnumerable<Task<T>> tasks)
    {
        var taskArray = tasks.ToArray();

        Task.WaitAll(taskArray);

        return taskArray.Select(task => task.Result);
    }

    public static void WaitAll(this IEnumerable<Task> tasks)
    {
        var taskArray = tasks.ToArray();

        Task.WaitAll(taskArray);
    }

    public static async Task<TValue> GetOrAddAsync<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, Func<TKey, Task<TValue>> valueFactory)
    {
        if (dictionary.TryGetValue(key, out var value))
            return value;

        return dictionary.GetOrAdd(key, await valueFactory(key));
    }

    public static Task<JObject> GetJObjectAsync(this IFlurlRequest request, CancellationToken cancellationToken = default, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
        => request.GetJsonAsync<JObject>(cancellationToken, completionOption);


    public static Task<JObject> GetJObjectAsync(this Url url, CancellationToken cancellationToken = default, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
        => url.GetJsonAsync<JObject>(cancellationToken, completionOption);

    public static Task<JObject> GetJObjectAsync(this string url, CancellationToken cancellationToken = default, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
        => url.GetJsonAsync<JObject>(cancellationToken, completionOption);

    public static Guid? GetObjectId(this JwtSecurityToken token)
        => Guid.TryParse(token.Claims.FirstOrDefault(claim => claim.Type.Equals("oid"))?.Value, out var value) ? value : null;

    public static Guid? GetTenantId(this JwtSecurityToken token)
        => Guid.TryParse(token.Claims.FirstOrDefault(claim => claim.Type.Equals("tid"))?.Value, out var value) ? value : null;

    public static Guid? GetClientId(this JwtSecurityToken token)
        => Guid.TryParse(token.Claims.FirstOrDefault(claim => claim.Type.Equals("appid"))?.Value, out var value) ? value : null;

    public static Guid? GetObjectId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.Claims.FirstOrDefault(claim => claim.Type.Equals("oid"))?.Value, out var value) ? value : null;

    public static Guid? GetTenantId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.Claims.FirstOrDefault(claim => claim.Type.Equals("tid"))?.Value, out var value) ? value : null;

    public static Guid? GetClientId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.Claims.FirstOrDefault(claim => claim.Type.Equals("appid"))?.Value, out var value) ? value : null;
}
