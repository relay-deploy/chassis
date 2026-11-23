using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relay.Chassis.Hosting;
using Relay.Chassis.Postgres.Jobs;

namespace Relay.Chassis.Postgres;

public static class PostgresRegistration
{
    public static IServiceCollection AddRelayPostgres<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext, IJobContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging(logging => logging.AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Warning));
        services.AddOptions<PostgresOptions>().BindConfiguration(PostgresOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<JobOptions>().BindConfiguration(JobOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddDbContext<TContext>((provider, builder) =>
            builder.UseRelayPostgres(provider.GetRequiredService<IOptions<PostgresOptions>>().Value.ConnectionString, schema));
        services.AddHostedService<DatabaseMigrator<TContext>>();
        services.AddScoped<IJobScheduler, EfJobScheduler<TContext>>();
        services.AddHostedService<JobWorker<TContext>>();
        services.AddHealthChecks().AddCheck<PostgresReadiness<TContext>>("postgres", tags: [ServiceDefaults.ReadinessTag]);
        return services;
    }

    public static IServiceCollection AddRelayJob<TJob, THandler>(this IServiceCollection services)
        where TJob : IJob
        where THandler : class, IJobHandler<TJob>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new JobRegistration(TJob.Kind, (provider, payload, attempt, cancellationToken) =>
        {
            var job = JsonSerializer.Deserialize<TJob>(payload, JobSerialization.Options)
                ?? throw new JsonException($"Job payload for {TJob.Kind} is empty.");
            return provider.GetRequiredService<THandler>().Handle(job, attempt, cancellationToken);
        }));
        return services;
    }

    public static ModelBuilder AddRelayJobs(this ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new JobRecordConfiguration());
}
