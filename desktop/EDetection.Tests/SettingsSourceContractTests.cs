using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EDetection.Tests;

public sealed class SettingsSourceContractTests
{
    [Fact]
    public void SearchEntries_MatchNavigationCategories()
    {
        var source = ReadSource("desktop/EDetection.Desktop/Views/SettingsView.Search.cs");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DefaultsSection"] = "检测",
            ["ThresholdsSection"] = "检测",
            ["DetectionRulesSection"] = "检测",
            ["ReportsSection"] = "报告与数据",
            ["LogsSection"] = "报告与数据",
            ["WindowSection"] = "常规",
            ["AppearanceSection"] = "常规",
            ["NotificationsSection"] = "通知",
            ["LlmSection"] = "连接与集成",
            ["ProxySection"] = "连接与集成",
            ["UpdatesSection"] = "更新与维护",
            ["AdvancedSection"] = "更新与维护",
        };
        var entries = Regex.Matches(source,
            "new\\(\"([^\"\\r\\n]+)\",\\s*\"([^\"\\r\\n]+)\",\\s*\"[^\"\\r\\n]*\",\\s*\"([^\"\\r\\n]+)\"");

        Assert.True(entries.Count >= 30, "Expected a complete settings search index.");
        foreach (Match entry in entries)
        {
            var title = entry.Groups[1].Value;
            var label = entry.Groups[2].Value;
            var section = entry.Groups[3].Value;
            Assert.True(expected.TryGetValue(section, out var category), $"Unknown category for {title}: {section}");
            Assert.Equal(category, label);
        }
    }

    [Fact]
    public void SettingsBindings_DoNotEditActivePathOrKeepStaleAccessibilityNames()
    {
        var xaml = ReadSource("desktop/EDetection.Desktop/Views/SettingsView.xaml");
        Assert.Contains("Text=\"{Binding PendingConfigPath, Mode=TwoWay", xaml);
        Assert.DoesNotContain("Text=\"{Binding ConfigPath, Mode=TwoWay", xaml);
        Assert.Contains("AutomationProperties.Name=\"报告历史上限\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"运行记录上限\"", xaml);
        Assert.Contains("Click=\"LoadDetectionConfig_Click\"", xaml);
        Assert.Contains("Click=\"SaveDetectionConfigAs_Click\"", xaml);
    }

    [Fact]
    public void DetailFlyout_UsesViewCommandHandlersRatherThanUnboundFlyoutDataContext()
    {
        var xaml = ReadSource("desktop/EDetection.Desktop/Views/DetectionWorkbenchView.xaml");
        var behind = ReadSource("desktop/EDetection.Desktop/Views/DetectionWorkbenchView.xaml.cs");
        Assert.Contains("DetailActionsFlyout_Opening", xaml);
        Assert.Contains("CopyDetailPathMenuItem_Click", behind);
        Assert.Contains("CopyFilteredDetailsMenuItem_Click", behind);
        Assert.Contains("ExplainSelectedDetailMenuItem_Click", behind);
        Assert.DoesNotContain("Command=\"{Binding CopyFilteredDetailsCommand}\"", xaml);
    }

    [Fact]
    public void SettingsNavigation_ExposesStableUiAutomationIdentifiers()
    {
        var xaml = ReadSource("desktop/EDetection.Desktop/Views/SettingsView.xaml");
        var smoke = ReadSource("desktop/scripts/Test-DesktopSettingsSmoke.ps1");

        foreach (var id in new[]
        {
            "SettingsNav_Detection",
            "SettingsNav_Reports",
            "SettingsNav_General",
            "SettingsNav_Notifications",
            "SettingsNav_Integrations",
            "SettingsNav_Maintenance",
        })
        {
            Assert.Contains($"AutomationProperties.AutomationId=\"{id}\"", xaml);
            Assert.Contains($"\"{id}\"", smoke);
        }

        Assert.Contains("AutomationIdProperty", smoke);
        Assert.Contains("SelectionItemPattern", smoke);
    }

    private static string ReadSource(string relativePath, [CallerFilePath] string sourcePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(sourcePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));
        return File.ReadAllText(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
