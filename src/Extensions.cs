using System.Net;
using System.Reflection;

using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;

namespace IPAlloc
{
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

        public static IDictionary<string,string> GetSettings(this CloudStorageAccount cloudStorageAccount)
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
    }
}
