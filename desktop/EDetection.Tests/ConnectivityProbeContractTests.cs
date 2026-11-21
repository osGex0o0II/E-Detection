using EDetection.Desktop.Services;

namespace EDetection.Tests;

public sealed class ConnectivityProbeContractTests
{
    [Fact]
    public void Probes_AreHttpsAndNotEmpty()
    {
        Assert.NotEmpty(NetworkProxyService.ConnectivityProbes);
        foreach (var probe in NetworkProxyService.ConnectivityProbes)
        {
            Assert.True(
                Uri.TryCreate(probe, UriKind.Absolute, out var uri),
                $"Connectivity probe '{probe}' is not an absolute URI.");
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
        }
    }

    [Fact]
    public void Probes_DoNotUseTheRetiredCaptivePortalHost()
    {
        // www.msftconnecttest.com now resolves to an Akamai edge that presents
        // a certificate valid only for a248.e.akamai.net, so .NET rejects it with
        // RemoteCertificateNameMismatch on every machine.  Using it as the proxy
        // self-test made "测试代理" fail even when the proxy worked.
        Assert.DoesNotContain(
            NetworkProxyService.ConnectivityProbes,
            probe => probe.Contains("msftconnecttest", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            NetworkProxyService.ConnectivityProbes,
            probe => probe.Contains("msftncsi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Probes_ContainTheStandardGenerate204Endpoints()
    {
        Assert.Contains(
            NetworkProxyService.ConnectivityProbes,
            probe => probe.Contains("generate_204", StringComparison.OrdinalIgnoreCase));
    }
}
