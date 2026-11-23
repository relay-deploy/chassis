using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Relay.Chassis.Hosting;

public static class ServiceDefaults
{
    public const string ReadinessTag = "ready";

    public static TBuilder AddRelayServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        });
        builder.Services.AddHealthChecks();
        return builder;
    }

    public static WebApplication MapRelayServiceDefaults(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessTag) });
        return app;
    }
}
