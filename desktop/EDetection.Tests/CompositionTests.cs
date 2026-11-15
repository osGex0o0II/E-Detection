using EDetection.Desktop.Composition;
using EDetection.Desktop.Services;
using EDetection.Infrastructure;
using EDetection.NativeCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EDetection.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void AddDesktopCore_ResolvesRegisteredServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppCredentialStore, FakeCredentialStore>();
        services.AddSingleton<IStartupIntegrationProvider, FakeStartupProvider>();
        services.AddDesktopCore();
        services.AddInfrastructure();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider.GetRequiredService<SettingsService>());
        Assert.NotNull(provider.GetRequiredService<NetworkProxyService>());
        Assert.NotNull(provider.GetRequiredService<NtfyNotificationService>());
        Assert.NotNull(provider.GetRequiredService<LlmAssistantService>());
        Assert.NotNull(provider.GetRequiredService<ReportSummaryComposer>());
        Assert.NotNull(provider.GetRequiredService<StartupService>());
        Assert.IsType<NativeDetectionBackendService>(
            provider.GetRequiredService<IDetectionBackendService>());
    }
}
