using EDetection.Desktop.Services;

namespace EDetection.Tests;

public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("not a url")]
    [InlineData("github.com/osGex0o0II/E-Detection/releases/latest")]
    public async Task CheckLatest_RejectsMalformedFeedWithActionableMessage(string feedUrl)
    {
        // The feed is a free-text settings field; a bare host name is the
        // common mistake, and the old UriFormatException surfaced a localised
        // framework message that named neither the field nor the fix.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateCheckService().CheckLatestAsync(
                feedUrl,
                "2.0.3",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("更新源", exception.Message, StringComparison.Ordinal);
        Assert.Contains("https://", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckLatest_RejectsNonHttpsFeed()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateCheckService().CheckLatestAsync(
                "http://example.com/feed",
                "2.0.3",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckLatest_RejectsEmptyFeed()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => new UpdateCheckService().CheckLatestAsync(
                "   ",
                "2.0.3",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("更新源", exception.Message, StringComparison.Ordinal);
    }
}
