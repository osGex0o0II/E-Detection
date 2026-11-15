using EDetection.Desktop.Models;

namespace EDetection.Desktop.Services;

public sealed class StartupService
{
    private readonly IStartupIntegrationProvider _provider;

    // The concrete providers are Windows-specific and are composed by the shell
    // (see MainWindow), so this service stays testable and free of platform
    // APIs.  There is deliberately no parameterless constructor.
    public StartupService(IStartupIntegrationProvider provider)
    {
        _provider = provider;
    }

    public StartupIntegrationSnapshot GetStatus() => _provider.GetStatus();

    public bool IsEnabled() => GetStatus().IsEnabled;

    public void SetEnabled(bool enabled) => _provider.SetEnabled(enabled);
}
