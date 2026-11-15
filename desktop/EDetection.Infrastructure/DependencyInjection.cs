using EDetection.NativeCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EDetection.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDetectionBackendService, NativeDetectionBackendService>();
        return services;
    }
}
