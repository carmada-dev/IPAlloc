using System.Text.Json;
using System.Text.Json.Serialization;

using IPAlloc.Model;
using IPAlloc.Serialization;
using IPAlloc.Threading;

using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

internal class Program
{
    private static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var host = new HostBuilder()
                .UseSerilog((context, services, configuration) =>
                {
                    configuration
                        .MinimumLevel.Information()
                        .Enrich.FromLogContext()
                        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                        .WriteTo.ApplicationInsights(TelemetryConfiguration.Active, TelemetryConverter.Traces);

                }, preserveStaticLogger: false)
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureServices(services =>
                {
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

                    services.AddLogging(builder =>
                    {
                        Log.Information("Cleaning up logging providers.");

                        builder
                            .ClearProviders()
                            .AddSerilog();
                    });

                    services
                        .AddSingleton<AllocationRepository>()
                        .AddSingleton<IDistributedLockManager, BlobStorageDistributedLockManager>();
                })
                .Build();

            host.Run();
        }
        catch (Exception exc)
        {
            Log.Fatal(exc, "Failed to start the application.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}