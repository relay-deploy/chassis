using System.Reflection;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Relay.Chassis.Hosting;
using Relay.Chassis.Postgres.Jobs;
using Relay.Chassis.Postgres.Migrations;
using Relay.Chassis.Postgres.Sessions;

namespace Relay.Chassis.Postgres;

public static class PostgresRegistration
{
    public static IServiceCollection AddRelayPostgres(this IServiceCollection services, Assembly migrations)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<PostgresOptions>().BindConfiguration(PostgresOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<JobOptions>().BindConfiguration(JobOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString) { SearchPath = options.Schema };
            return new NpgsqlDataSourceBuilder(connection.ConnectionString).Build();
        });
        services.AddSingleton(new MigrationSource(typeof(PostgresRegistration).Assembly));
        services.AddSingleton(new MigrationSource(migrations));
        services.AddHostedService<SchemaMigrator>();
        services.AddScoped<PostgresSession>();
        services.AddScoped<IJobScheduler, PostgresJobScheduler>();
        services.AddHostedService<JobWorker>();
        services.AddHealthChecks().AddCheck<PostgresReadiness>("postgres", tags: [ServiceDefaults.ReadinessTag]);
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
}
