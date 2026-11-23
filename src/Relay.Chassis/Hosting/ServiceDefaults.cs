using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Relay.Chassis.Hosting;

public static class ServiceDefaults
{
    public const string ReadinessTag = "ready";

    public static WebApplicationBuilder AddRelayServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<EndpointOptions>()
            .BindConfiguration(EndpointOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            var endpoints = kestrel.ApplicationServices.GetRequiredService<IOptions<EndpointOptions>>().Value;
            kestrel.Listen(IPAddress.Any, endpoints.GrpcPort, listen => listen.Protocols = HttpProtocols.Http2);
            kestrel.Listen(IPAddress.Any, endpoints.ManagementPort, listen => listen.Protocols = HttpProtocols.Http1);
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        });
        builder.Logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId | ActivityTrackingOptions.Baggage);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddGrpc();
        builder.Services.AddHealthChecks();
        return builder;
    }

    public static WebApplication MapRelayServiceDefaults(this WebApplication app)
    {
        var management = $"*:{app.Services.GetRequiredService<IOptions<EndpointOptions>>().Value.ManagementPort}";
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).RequireHost(management);
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessTag) }).RequireHost(management);
        return app;
    }
}
