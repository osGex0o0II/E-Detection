using EDetection.Desktop.Services;

// First automated coverage of the desktop service layer.  Until these services
// were lifted out of the WinUI shell into EDetection.Desktop.Core, this test
// project could not reference them at all, so every defect in that layer was
// unreachable by any gate.
internal static partial class RegressionSuite
{
    // A proxy address is free text that persists on every keystroke.  WebProxy's
    // constructor throws UriFormatException on ordinary-looking but invalid
    // values, and the callers are background tasks started from an async void
    // handler - so a throw here terminated the process ~2 s after every launch,
    // with no way to reach Settings and undo it.
    private static Task MalformedProxyAddressIsRejectedNotThrownAsync()
    {
        string[] malformed = ["http://", "http://127.0.0.1: 7890", "not a uri", ":::", "://host"];
        foreach (var address in malformed)
        {
            var settings = new FakeProxySettings { ProxyAddress = address };
            var service = new NetworkProxyService(new FakeCredentialStore());
            using var handler = service.BuildHandler(settings, useProxy: true);
            TestSupport.True(
                handler.Proxy is null,
                $"A malformed proxy address must not be installed as a proxy: '{address}'.");
        }

        return Task.CompletedTask;
    }

    // Guards the fix above against over-correction: a well-formed address must
    // still be honoured, otherwise the proxy feature is silently dead.
    private static Task ValidProxyAddressIsHonouredAsync()
    {
        var settings = new FakeProxySettings { ProxyAddress = "http://127.0.0.1:7890" };
        var service = new NetworkProxyService(new FakeCredentialStore());
        using var handler = service.BuildHandler(settings, useProxy: true);
        TestSupport.True(handler.Proxy is not null, "A valid proxy address must be installed.");
        TestSupport.True(handler.UseProxy, "A valid proxy address must enable proxy use.");
        return Task.CompletedTask;
    }

    // The only pre-existing redaction check greps DiagnosticsRedactor.cs for
    // identifier names and never runs Redact, so deleting the UNC replacement
    // line kept it green while exported logs leaked share names.  This asserts
    // the transform instead: secrets in, no secrets out.
    private static Task DiagnosticsRedactionRemovesSecretsAsync()
    {
        const string secret = "S3cr3tP4ss";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var input =
            $"proxy=https://svc:{secret}@ntfy.internal/topic "
            + "share=\\\\fileserver\\telemetry\\2026 "
            + $"output={home}\\reports\\a.xlsx";

        var redacted = DiagnosticsRedactor.Redact(input);

        TestSupport.True(
            !redacted.Contains(secret, StringComparison.Ordinal),
            $"URL credentials must be redacted. Got: {redacted}");
        TestSupport.True(
            redacted.Contains("<redacted>", StringComparison.Ordinal),
            $"Redacted credentials must leave a placeholder. Got: {redacted}");
        TestSupport.True(
            !redacted.Contains("fileserver", StringComparison.OrdinalIgnoreCase),
            $"UNC share names must be redacted. Got: {redacted}");
        TestSupport.True(
            redacted.Contains("{networkPath}", StringComparison.Ordinal),
            $"Redacted UNC paths must leave a placeholder. Got: {redacted}");
        TestSupport.True(
            !redacted.Contains(home, StringComparison.OrdinalIgnoreCase),
            $"The operator's profile path must be redacted. Got: {redacted}");

        return Task.CompletedTask;
    }

    private sealed class FakeProxySettings : INetworkProxySettings
    {
        public bool EnableNetworkProxy => true;

        public string ProxyAddress { get; init; } = "";

        public bool ProxyRequiresAuthentication => false;

        public string ProxyUserName => "";
    }

    private sealed class FakeCredentialStore : IAppCredentialStore
    {
        public string GetProxyPassword() => "proxy-password";

        public string GetNtfyToken() => "ntfy-token";

        public string GetLlmApiKey() => "llm-key";
    }
}
