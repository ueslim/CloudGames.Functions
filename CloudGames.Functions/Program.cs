using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System;
using System.Net.Http;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // Serilog configuration
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.Console()
            .CreateLogger();

        services.AddLogging(lb => lb.AddSerilog(Log.Logger, dispose: true));

        // OpenTelemetry configuration
        var serviceName = "CloudGames.Functions.Payments";
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(serviceName)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o =>
                {
                    var endpoint = config["OTLP:Endpoint"] ?? config["OTLP__Endpoint"] ?? "http://localhost:4318";
                    o.Endpoint = new Uri(endpoint.TrimEnd('/') + "/v1/traces");
                }))
            .WithMetrics(m => m
                .AddRuntimeInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o =>
                {
                    var endpoint = config["OTLP:Endpoint"] ?? config["OTLP__Endpoint"] ?? "http://localhost:4318";
                    o.Endpoint = new Uri(endpoint.TrimEnd('/') + "/v1/metrics");
                }));

        // HttpClient for Games API
        services.AddHttpClient("games", (sp, client) =>
        {
            var baseUrl = config["Games:BaseUrl"] ?? config["Games__BaseUrl"] ?? "http://localhost:5002";
            client.BaseAddress = new Uri(baseUrl);
        });
    })
    .Build();

host.Run();
