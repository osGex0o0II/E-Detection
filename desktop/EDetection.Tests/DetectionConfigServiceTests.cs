using System.Text.Json;
using EDetection.Desktop.Models;
using EDetection.Desktop.Services;

namespace EDetection.Tests;

public sealed class DetectionConfigServiceTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"unknown_only\": 1}")]
    [InlineData("{\"V_MIN_THRESHOLD\": \"invalid\"}")]
    [InlineData("{\"I_MAX_THRESHOLD\": 100001}")]
    [InlineData("{\"FREEZE_COUNT_THRESHOLD\": 2.5}")]
    [InlineData("{\"power_factor\": \"true\"}")]
    [InlineData("{\"V_MIN_THRESHOLD\": 353, \"V_MIN_THRESHOLD\": 354}")]
    [InlineData("{\"V_MIN_THRESHOLD\": 450, \"V_MAX_THRESHOLD\": 430}")]
    public void TryLoadValidated_RejectsMalformedOrUnsafeContent(string json)
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "invalid.json");
            File.WriteAllText(path, json);
            var service = new DetectionConfigService();

            Assert.False(service.TryLoadValidated(path, out _, out var error));
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryLoadValidated_PreservesPartialLegacyConfiguration()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "legacy.json");
            File.WriteAllText(path, "{\"current\":true,\"V_MIN_THRESHOLD\":355}");
            var service = new DetectionConfigService();

            Assert.True(service.TryLoadValidated(path, out var config, out var error), error);
            Assert.Equal(355, config.VoltageMinThreshold);
            Assert.True(config.CurrentOverloadEnabled);
            Assert.True(config.CurrentUnbalanceEnabled);
            Assert.Equal(430, config.VoltageMaxThreshold);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_AtomicallyReplacesAndKeepsPriorVersion()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var other = Path.Combine(directory, "unrelated.json");
            File.WriteAllText(other, "{\"unchanged\":true}");
            var service = new DetectionConfigService();

            service.Save(path, new DetectionConfigSettings { VoltageMinThreshold = 355 });
            service.Save(path, new DetectionConfigSettings { VoltageMinThreshold = 356 });
            Assert.True(File.Exists(path + ".bak"));
            Assert.Equal(355, JsonDocument.Parse(File.ReadAllText(path + ".bak"))
                .RootElement.GetProperty("V_MIN_THRESHOLD").GetDouble());
            Assert.Equal(356, JsonDocument.Parse(File.ReadAllText(path))
                .RootElement.GetProperty("V_MIN_THRESHOLD").GetDouble());
            Assert.Equal("{\"unchanged\":true}", File.ReadAllText(other));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_InvalidValueDoesNotReplaceExistingFile()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var service = new DetectionConfigService();
            service.Save(path, new DetectionConfigSettings { VoltageMinThreshold = 355 });
            var original = File.ReadAllText(path);

            Assert.Throws<ArgumentException>(() => service.Save(path,
                new DetectionConfigSettings { VoltageMinThreshold = 700, VoltageMaxThreshold = 430 }));
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryLoadValidated_DoesNotCreateNonexistentPath()
    {
        var directory = CreateDirectory();
        try
        {
            var missing = Path.Combine(directory, "missing.json");
            var service = new DetectionConfigService();

            Assert.False(service.TryLoadValidated(missing, out _, out _));
            Assert.False(File.Exists(missing));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "e-detection-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
