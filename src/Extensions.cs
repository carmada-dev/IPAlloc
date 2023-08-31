using System.Net;

using Microsoft.Azure.Functions.Worker.Http;

namespace IPAlloc
{
    internal static class Extensions
    {
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
    }
}
