using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relay.Chassis.Hosting;
using Relay.Chassis.Postgres.Events;
using Relay.Chassis.Postgres.Jobs;
using Relay.Chassis.Postgres.Outbox;

namespace Relay.Chassis.Postgres;

public static partial class PostgresRegistration
{
    private static readonly JsonParser eventParser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    public static IServiceCollection AddRelayPostgres<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging(logging => logging.AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Warning));
        services.AddOptions<PostgresOptions>().BindConfiguration(PostgresOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(new PostgresSchema(SchemaName(schema)));
        services.AddDbContext<TContext>((provider, builder) => builder
            .UseRelayPostgres(provider.GetRequiredService<IOptions<PostgresOptions>>().Value.ConnectionString, schema)
            .AddInterceptors(provider.GetServices<IInterceptor>()));
        services.AddHostedService<DatabaseMigrator<TContext>>();
        services.AddHealthChecks().AddCheck<PostgresReadiness<TContext>>("postgres", tags: [ServiceDefaults.ReadinessTag]);
        return services;
    }

    public static IServiceCollection AddRelayJobs<TContext>(this IServiceCollection services)
        where TContext : DbContext, IJobContext
    {
        services.AddOptions<JobOptions>().BindConfiguration(JobOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<IJobScheduler, EfJobScheduler<TContext>>();
        services.AddHostedService<JobWorker<TContext>>();
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

    public static IServiceCollection AddRelayOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext, IOutboxContext
    {
        services.AddScoped<IOutbox, EfOutbox<TContext>>();
        services.AddScoped<IInterceptor, OutboxDoorbell>();
        return services;
    }

    public static IServiceCollection AddRelayEventSubscription<TContext>(this IServiceCollection services, string source)
        where TContext : DbContext, IEventCursorContext
    {
        var schema = new PostgresSchema(SchemaName(source));
        services.AddOptions<EventSubscriptionOptions>().BindConfiguration(EventSubscriptionOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IHostedService>(provider => ActivatorUtilities.CreateInstance<EventSubscription<TContext>>(provider, schema));
        return services;
    }

    public static IServiceCollection AddRelayEventConsumer<TMessage, THandler>(this IServiceCollection services, string source)
        where TMessage : IMessage<TMessage>, new()
        where THandler : class, IEventConsumer<TMessage>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new EventConsumerRegistration(SchemaName(source), new TMessage().Descriptor.FullName, (provider, payload, envelope, cancellationToken) =>
            provider.GetRequiredService<THandler>().Handle(eventParser.Parse<TMessage>(payload), envelope, cancellationToken)));
        return services;
    }

    public static ModelBuilder MapRelayJobs(this ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new JobRecordConfiguration());

    public static ModelBuilder MapRelayOutbox(this ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new OutboxEventConfiguration());

    public static ModelBuilder MapRelayEventCursors(this ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new EventCursorConfiguration());

    private static string SchemaName(string value) =>
        SchemaPattern().IsMatch(value) ? value : throw new ArgumentException($"'{value}' is not a valid schema name.", nameof(value));

    [GeneratedRegex("^[a-z][a-z0-9_]{0,40}$")]
    private static partial Regex SchemaPattern();
}
