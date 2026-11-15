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
        using var response = await client.GetAsync("https://www.msftconnecttest.com/connecttest.txt", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!body.Contains("Microsoft Connect Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("代理返回了非预期的连接测试响应。");
        }
    }

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
