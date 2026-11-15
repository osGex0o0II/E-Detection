using EDetection.Desktop.Models;
using EDetection.Desktop.Services;

namespace EDetection.Tests;

internal sealed class FakeCredentialStore : IAppCredentialStore
{
    public string GetProxyPassword() => string.Empty;

    public string GetNtfyToken() => string.Empty;

    public string GetLlmApiKey() => string.Empty;
}

internal sealed class FakeStartupProvider : IStartupIntegrationProvider
{
    public StartupIntegrationSnapshot GetStatus() =>
        new(false, false, "test", "test", string.Empty, null);

    public void SetEnabled(bool enabled)
    {
    }
}
