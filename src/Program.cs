using System.Text.Json.Serialization;
using System.Text.Json;

using IPAlloc.Model;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Newtonsoft.Json;

using Serilog;
using IPAlloc.Serialization;

internal class Program
{
    private static void Main(string[] args)
    {
        
        var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices(services =>
            {
                var logger = new LoggerConfiguration()
                    .WriteTo.Console()
                    .CreateLogger();

                services
                    .AddLogging(logging => logging.AddSerilog(logger, true));

                services.Configure<JsonSerializerOptions>(options =>
                {
                    options.AllowTrailingCommas = true;
                    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    options.PropertyNameCaseInsensitive = true;

                    options.Converters.Add(new JsonStringEnumConverter());
                    options.Converters.Add(new IPAddressConverter());
                    options.Converters.Add(new IPEndPointConverter());
                    options.Converters.Add(new IPNetworkConverter());
                });

                services
                    .AddSingleton<AllocationRepository>();
            })
            .Build();

        host.Run();
    }
}