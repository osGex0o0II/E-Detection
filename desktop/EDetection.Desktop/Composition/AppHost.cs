using EDetection.Desktop.Services;
using EDetection.Desktop.ViewModels;
using EDetection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EDetection.Desktop.Composition;

public static class AppHost
{
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = [],
        });
        builder.Logging.ClearProviders();

        var services = builder.Services;
        services.AddSingleton<SecureCredentialService>();
        services.AddSingleton<IAppCredentialStore>(
            provider => provider.GetRequiredService<SecureCredentialService>());
        services.AddSingleton<IStartupIntegrationProvider>(
            _ => new FallbackStartupIntegrationProvider(
                new TaskSchedulerStartupIntegrationProvider(),
                new RegistryRunStartupIntegrationProvider()));
        services.AddDesktopCore();
        services.AddInfrastructure();
        services.AddSingleton<DesktopHealthService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return builder.Build();
    }
}
