using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Relay.Chassis.Grpc;

public static class GrpcClients
{
    public static IHttpClientBuilder AddRelayGrpcClient<TClient>(this IServiceCollection services, Func<IServiceProvider, Uri> address)
        where TClient : class =>
        services
            .AddGrpcClient<TClient>((provider, options) => options.Address = address(provider))
            .ConfigureChannel(channel => channel.ServiceConfig = new ServiceConfig { MethodConfigs = { RetryWhenUnavailable() } });

    private static MethodConfig RetryWhenUnavailable() => new()
    {
        Names = { MethodName.Default },
        RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 4,
            InitialBackoff = TimeSpan.FromMilliseconds(200),
            MaxBackoff = TimeSpan.FromSeconds(3),
            BackoffMultiplier = 2,
            RetryableStatusCodes = { StatusCode.Unavailable },
        },
    };
}
