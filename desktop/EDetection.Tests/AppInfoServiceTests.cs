using EDetection.Desktop.Services;

namespace EDetection.Tests;

public sealed class AppInfoServiceTests
{
    [Fact]
    public void FormatDisplayVersionUsesSevenCharacterBuildHash()
    {
        const string fullVersion = "2.0.3+71c27b237399a9aac934a2343093824542ccbc5f";

        Assert.Equal("2.0.3+71c27b2", AppInfoService.FormatDisplayVersion(fullVersion));
        Assert.Equal("2.0.3", AppInfoService.FormatDisplayVersion("2.0.3"));
        Assert.Equal("2.0.3+abc", AppInfoService.FormatDisplayVersion("2.0.3+abc"));
        Assert.Equal("", AppInfoService.FormatDisplayVersion(""));
    }
}
