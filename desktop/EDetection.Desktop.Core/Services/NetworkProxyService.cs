using System.Net;


namespace EDetection.Desktop.Services;

public sealed class NetworkProxyService(IAppCredentialStore credentials)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(8);

    public HttpClientHandler BuildHandler(INetworkProxySettings settings, bool useProxy)
    {
        var handler = new HttpClientHandler();
        if (!useProxy
            || !settings.EnableNetworkProxy
            || string.IsNullOrWhiteSpace(settings.ProxyAddress))
        {
            return handler;
        }

        if (!TryBuildProxy(settings, out var proxy))
        {
            // A malformed address is a rejected setting, not a fatal error.
            // Throwing here would escape into background callers and take the
            // process down on every launch until the value is hand-edited out
            // of settings.json.
            return handler;
        }

        handler.UseProxy = true;
        handler.Proxy = proxy;
        return handler;
    }

    public async Task TestProxyAsync(
        INetworkProxySettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.EnableNetworkProxy)
        {
            throw new InvalidOperationException("请先启用网络代理。");
        }

        if (string.IsNullOrWhiteSpace(settings.ProxyAddress))
        {
            throw new InvalidOperationException("请填写代理地址。");
        }

        if (!TryBuildProxy(settings, out _))
        {
            throw new InvalidOperationException("代理地址格式无效，请填写形如 http://host:port 的地址。");
        }

        using var handler = BuildHandler(settings, useProxy: true);
        using var client = new HttpClient(handler)
        {
            Timeout = TestTimeout,
        };

        // Probe several well-known captive-portal endpoints and accept the first
        // that answers 2xx.  The previous single probe was
        // https://www.msftconnecttest.com/connecttest.txt, which now resolves to
        // an Akamai host that serves a certificate for a248.e.akamai.net with no
        // SAN for the requested name - so the test failed with
        // RemoteCertificateNameMismatch on every machine, even behind a working
        // proxy.  The generate_204 endpoints answer 204 over valid TLS.
        Exception? lastFailure = null;
        foreach (var probe in ConnectivityProbes)
        {
            try
            {
                using var response = await client.GetAsync(probe, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastFailure = new HttpRequestException(
                    $"代理连接测试返回 HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       or TaskCanceledException
                                       or OperationCanceledException
                                       or System.Security.Authentication.AuthenticationException)
            {
                lastFailure = ex;
            }
        }

        throw lastFailure ?? new InvalidOperationException("代理连接测试未收到任何响应。");
    }

    /// <summary>
    /// Endpoints that answer a bare 2xx/204 with valid TLS.  Order matters:
    /// the first one that succeeds wins, so a proxy that only allows some hosts
    /// still reports success.  Exposed for the regression test that guards
    /// against re-introducing a host whose certificate no longer matches its
    /// own DNS name (that is what broke the previous single probe).
    /// </summary>
    public static IReadOnlyList<string> ConnectivityProbes { get; } =
    [
        "https://connectivitycheck.gstatic.com/generate_204",
        "https://www.gstatic.com/generate_204",
        "https://cp.cloudflare.com/generate_204",
    ];

    // WebProxy's constructor throws UriFormatException on values that are
    // perfectly ordinary strings but not valid URIs ("http://", a stray space
    // before the port).  The address arrives from a free-text settings box that
    // persists on every keystroke, so it must be validated, never assumed.
    private bool TryBuildProxy(INetworkProxySettings settings, out WebProxy? proxy)
    {
        proxy = null;
        var address = settings.ProxyAddress;
        if (string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        var built = new WebProxy(uri);
        if (settings.ProxyRequiresAuthentication
            && !string.IsNullOrWhiteSpace(settings.ProxyUserName))
        {
            built.Credentials = new NetworkCredential(
                settings.ProxyUserName,
                credentials.GetProxyPassword());
        }

        proxy = built;
        return true;
    }
}
