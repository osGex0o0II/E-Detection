using System.Globalization;
using System.Text.Json;
using EDetection.Desktop.Models;

namespace EDetection.Desktop.Services;

public sealed class DetectionConfigService
{
    public static readonly string DefaultUserConfigPath = Path.Combine(
        SettingsService.SettingsDirectory,
        "detection-config.json");

    private const string VoltageMinThresholdKey = "V_MIN_THRESHOLD";
    private const string VoltageMaxThresholdKey = "V_MAX_THRESHOLD";
    private const string CurrentMaxThresholdKey = "I_MAX_THRESHOLD";
    private const string CurrentUnbalanceMaxThresholdKey = "I_UNBALANCE_MAX_THRESHOLD";
    private const string ActivePowerMinThresholdKey = "P_ACTIVE_MIN_THRESHOLD";
    private const string PowerFactorMinThresholdKey = "PF_MIN_THRESHOLD";
    private const string TemperatureMinThresholdKey = "T_MIN_THRESHOLD";
    private const string TemperatureMaxThresholdKey = "T_MAX_THRESHOLD";
    private const string CurrentActiveMinThresholdKey = "I_MIN_ACTIVE_THRESHOLD";
    private const string FreezeCountThresholdKey = "FREEZE_COUNT_THRESHOLD";
    private const string FreezeStdThresholdKey = "FREEZE_STD_THRESHOLD";
    private const string VoltageImbalanceThresholdKey = "V_IMBALANCE_THRESHOLD";
    private const string CurrentOverloadKey = "current_overload";
    private const string CurrentUnbalanceKey = "current_unbalance";
    private const string LegacyCurrentKey = "current";
    private const string PowerFactorKey = "power_factor";
    private const string DetailOutputKey = "detail_output";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public DetectionConfigSettings CreateDefault() => new();

    public string EnsureUserConfig(string? preferredPath = null)
    {
        try
        {
            var targetPath = ResolveEffectiveConfigPath(preferredPath);
            if (File.Exists(targetPath))
            {
                return targetPath;
            }

            var bundledConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            var initialConfig = File.Exists(bundledConfigPath)
                ? Load(bundledConfigPath)
                : CreateDefault();
            Save(targetPath, initialConfig);
            return targetPath;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return DefaultUserConfigPath;
        }
    }

    public static string ResolveEffectiveConfigPath(string? preferredPath)
    {
        if (string.IsNullOrWhiteSpace(preferredPath))
        {
            return DefaultUserConfigPath;
        }

        try
        {
            return IsBundledDefaultConfigPath(preferredPath)
                ? DefaultUserConfigPath
                : ResolveConfigPath(preferredPath);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return DefaultUserConfigPath;
        }
    }

    private static readonly Dictionary<string, (double Min, double Max)> NumericBounds = new(StringComparer.Ordinal)
    {
        [VoltageMinThresholdKey] = (0, 1000),
        [VoltageMaxThresholdKey] = (0, 1000),
        [CurrentMaxThresholdKey] = (0, 100000),
        [CurrentUnbalanceMaxThresholdKey] = (0, 1),
        [ActivePowerMinThresholdKey] = (double.NegativeInfinity, double.PositiveInfinity),
        [PowerFactorMinThresholdKey] = (0, 1),
        [TemperatureMinThresholdKey] = (-50, 200),
        [TemperatureMaxThresholdKey] = (-50, 200),
        [CurrentActiveMinThresholdKey] = (0.001, 100000),
        [FreezeCountThresholdKey] = (1, 1000),
        [FreezeStdThresholdKey] = (0, 1),
        [VoltageImbalanceThresholdKey] = (0.001, 1),
    };

    private static readonly HashSet<string> BooleanKeys = new(StringComparer.Ordinal)
    {
        CurrentOverloadKey,
        CurrentUnbalanceKey,
        LegacyCurrentKey,
        PowerFactorKey,
        DetailOutputKey,
    };

    public DetectionConfigSettings Load(string path)
    {
        try
        {
            var resolvedPath = ResolveConfigPath(path);
            if (!File.Exists(resolvedPath))
            {
                return CreateDefault();
            }

            using var document = JsonDocument.Parse(File.ReadAllText(resolvedPath));
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? Normalize(ParseSettings(document.RootElement))
                : CreateDefault();
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return CreateDefault();
        }
    }

    public bool TryLoadValidated(string path, out DetectionConfigSettings settings, out string? error)
    {
        error = ReadValidatedFile(path, out settings);
        return error is null;
    }

    public static string? ValidateConfigFile(string path) => ReadValidatedFile(path, out _);

    private static string? ReadValidatedFile(string path, out DetectionConfigSettings settings)
    {
        settings = new DetectionConfigSettings();
        try
        {
            var resolvedPath = ResolveConfigPath(path);
            if (!File.Exists(resolvedPath))
            {
                return $"阈值配置文件不存在: {resolvedPath}";
            }

            using var document = JsonDocument.Parse(File.ReadAllText(resolvedPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "阈值配置文件必须是 JSON 对象。";
            }

            var root = document.RootElement;
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var recognizedFields = 0;
            foreach (var item in root.EnumerateObject())
            {
                if (NumericBounds.TryGetValue(item.Name, out var bounds))
                {
                    recognizedFields++;
                    if (!seenKeys.Add(item.Name))
                    {
                        return $"阈值配置字段重复: {item.Name}";
                    }

                    if (!TryReadNumeric(item.Value, out var value)
                        || !double.IsFinite(value)
                        || value < bounds.Min
                        || value > bounds.Max
                        || (item.NameEquals(FreezeCountThresholdKey) && value != Math.Truncate(value)))
                    {
                        return $"阈值配置字段无效或超出范围: {item.Name}";
                    }
                }
                else if (BooleanKeys.Contains(item.Name))
                {
                    recognizedFields++;
                    if (!seenKeys.Add(item.Name)
                        || item.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        return $"阈值配置布尔字段无效或重复: {item.Name}";
                    }
                }
            }

            if (recognizedFields == 0)
            {
                return "阈值配置文件没有可识别的检测参数。";
            }

            var parsed = ParseSettings(root);
            var validationError = ValidateSettingsValues(parsed);
            if (validationError is not null)
            {
                return validationError;
            }

            settings = parsed;
            return null;
        }
        catch (JsonException ex)
        {
            return $"阈值配置文件无法解析: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return $"阈值配置文件不可读取或路径无效: {ex.Message}";
        }
    }

    private static bool TryReadNumeric(JsonElement value, out double parsed)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out parsed);
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
        }

        parsed = default;
        return false;
    }

    private static DetectionConfigSettings ParseSettings(JsonElement root)
    {
        var settings = new DetectionConfigSettings();
        settings.VoltageMinThreshold = GetDouble(root, VoltageMinThresholdKey, settings.VoltageMinThreshold);
        settings.VoltageMaxThreshold = GetDouble(root, VoltageMaxThresholdKey, settings.VoltageMaxThreshold);
        settings.CurrentMaxThreshold = GetDouble(root, CurrentMaxThresholdKey, settings.CurrentMaxThreshold);
        settings.CurrentUnbalanceMaxThreshold = GetDouble(root, CurrentUnbalanceMaxThresholdKey, settings.CurrentUnbalanceMaxThreshold);
        settings.ActivePowerMinThreshold = GetDouble(root, ActivePowerMinThresholdKey, settings.ActivePowerMinThreshold);
        settings.PowerFactorMinThreshold = GetDouble(root, PowerFactorMinThresholdKey, settings.PowerFactorMinThreshold);
        settings.TemperatureMinThreshold = GetDouble(root, TemperatureMinThresholdKey, settings.TemperatureMinThreshold);
        settings.TemperatureMaxThreshold = GetDouble(root, TemperatureMaxThresholdKey, settings.TemperatureMaxThreshold);
        settings.CurrentActiveMinThreshold = GetDouble(root, CurrentActiveMinThresholdKey, settings.CurrentActiveMinThreshold);
        settings.FreezeCountThreshold = GetDouble(root, FreezeCountThresholdKey, settings.FreezeCountThreshold);
        settings.FreezeStdThreshold = GetDouble(root, FreezeStdThresholdKey, settings.FreezeStdThreshold);
        settings.VoltageImbalanceThreshold = GetDouble(root, VoltageImbalanceThresholdKey, settings.VoltageImbalanceThreshold);
        settings.CurrentOverloadEnabled = GetBool(root, CurrentOverloadKey, settings.CurrentOverloadEnabled);
        settings.CurrentUnbalanceEnabled = GetBool(root, CurrentUnbalanceKey, settings.CurrentUnbalanceEnabled);
        settings.PowerFactorEnabled = GetBool(root, PowerFactorKey, settings.PowerFactorEnabled);
        settings.DetailOutputEnabled = GetBool(root, DetailOutputKey, settings.DetailOutputEnabled);

        if (root.TryGetProperty(LegacyCurrentKey, out var legacyCurrent)
                && legacyCurrent.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            settings.CurrentOverloadEnabled = legacyCurrent.GetBoolean();
            settings.CurrentUnbalanceEnabled = legacyCurrent.GetBoolean();
        }

        return settings;
    }

    private static string? ValidateSettingsValues(DetectionConfigSettings settings)
    {
        (string Name, double Value)[] values =
        [
            (VoltageMinThresholdKey, settings.VoltageMinThreshold),
            (VoltageMaxThresholdKey, settings.VoltageMaxThreshold),
            (CurrentMaxThresholdKey, settings.CurrentMaxThreshold),
            (CurrentUnbalanceMaxThresholdKey, settings.CurrentUnbalanceMaxThreshold),
            (ActivePowerMinThresholdKey, settings.ActivePowerMinThreshold),
            (PowerFactorMinThresholdKey, settings.PowerFactorMinThreshold),
            (TemperatureMinThresholdKey, settings.TemperatureMinThreshold),
            (TemperatureMaxThresholdKey, settings.TemperatureMaxThreshold),
            (CurrentActiveMinThresholdKey, settings.CurrentActiveMinThreshold),
            (FreezeCountThresholdKey, settings.FreezeCountThreshold),
            (FreezeStdThresholdKey, settings.FreezeStdThreshold),
            (VoltageImbalanceThresholdKey, settings.VoltageImbalanceThreshold),
        ];

        foreach (var (name, value) in values)
        {
            var (min, max) = NumericBounds[name];
            if (!double.IsFinite(value) || value < min || value > max
                || (name == FreezeCountThresholdKey && value != Math.Truncate(value)))
            {
                // The raw JSON key is meaningless to a person reading the
                // settings footer, so name the visible field and its range.
                var label = ThresholdLabels.TryGetValue(name, out var known)
                    ? known
                    : name;
                var hint = name == FreezeCountThresholdKey
                    ? "必须是整数"
                    : $"范围 {FormatBound(min)}-{FormatBound(max)}";
                return $"{label}超出允许范围（{hint}）。";
            }
        }

        return ValidateThresholdOrder(settings);
    }

    private static readonly Dictionary<string, string> ThresholdLabels = new(StringComparer.Ordinal)
    {
        [VoltageMinThresholdKey] = "电压下限",
        [VoltageMaxThresholdKey] = "电压上限",
        [CurrentMaxThresholdKey] = "电流上限",
        [CurrentUnbalanceMaxThresholdKey] = "电流不平衡上限",
        [ActivePowerMinThresholdKey] = "有功功率下限",
        [PowerFactorMinThresholdKey] = "功率因数下限",
        [TemperatureMinThresholdKey] = "温度下限",
        [TemperatureMaxThresholdKey] = "温度上限",
        [CurrentActiveMinThresholdKey] = "电流激活下限",
        [FreezeCountThresholdKey] = "连续不变化点数",
        [FreezeStdThresholdKey] = "数据波动容差",
        [VoltageImbalanceThresholdKey] = "相电压不平衡阈值",
    };

    private static string FormatBound(double value) =>
        double.IsInfinity(value) ? "不限" : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    public void Save(string path, DetectionConfigSettings settings)
    {
        var resolvedPath = ResolveConfigPath(path);
        var thresholdIssue = ValidateSettingsValues(settings);
        var normalized = Normalize(settings);
        if (thresholdIssue is not null)
        {
            throw new ArgumentException(thresholdIssue, nameof(settings));
        }

        var config = new Dictionary<string, object>
        {
            [VoltageMinThresholdKey] = normalized.VoltageMinThreshold,
            [VoltageMaxThresholdKey] = normalized.VoltageMaxThreshold,
            [CurrentMaxThresholdKey] = normalized.CurrentMaxThreshold,
            [CurrentUnbalanceMaxThresholdKey] = normalized.CurrentUnbalanceMaxThreshold,
            [ActivePowerMinThresholdKey] = normalized.ActivePowerMinThreshold,
            [PowerFactorMinThresholdKey] = normalized.PowerFactorMinThreshold,
            [TemperatureMinThresholdKey] = normalized.TemperatureMinThreshold,
            [TemperatureMaxThresholdKey] = normalized.TemperatureMaxThreshold,
            [CurrentActiveMinThresholdKey] = normalized.CurrentActiveMinThreshold,
            [FreezeCountThresholdKey] = (int)Math.Round(normalized.FreezeCountThreshold),
            [FreezeStdThresholdKey] = normalized.FreezeStdThreshold,
            [VoltageImbalanceThresholdKey] = normalized.VoltageImbalanceThreshold,
            [CurrentOverloadKey] = normalized.CurrentOverloadEnabled,
            [CurrentUnbalanceKey] = normalized.CurrentUnbalanceEnabled,
            [PowerFactorKey] = normalized.PowerFactorEnabled,
            [DetailOutputKey] = normalized.DetailOutputEnabled,
        };

        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Stage the complete file in the same directory, then atomically swap it.
        // Keep the previous valid file as a recoverable .bak on every replacement.
        var temporaryPath = resolvedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(config, JsonOptions) + Environment.NewLine);
            if (File.Exists(resolvedPath))
            {
                File.Replace(temporaryPath, resolvedPath, resolvedPath + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, resolvedPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string ResolveConfigPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DefaultUserConfigPath;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }

    public static bool IsBundledDefaultConfigPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            var resolvedPath = Path.GetFullPath(ResolveConfigPath(path));
            var bundledConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "config.json"));
            return string.Equals(
                resolvedPath,
                bundledConfigPath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return false;
        }
    }

    private static DetectionConfigSettings Normalize(DetectionConfigSettings settings) => new()
    {
        VoltageMinThreshold = ClampFinite(settings.VoltageMinThreshold, 0, 1000, 353.0),
        VoltageMaxThreshold = ClampFinite(settings.VoltageMaxThreshold, 0, 1000, 430.0),
        CurrentMaxThreshold = ClampFinite(settings.CurrentMaxThreshold, 0, 100000, 1000.0),
        CurrentUnbalanceMaxThreshold = ClampFinite(settings.CurrentUnbalanceMaxThreshold, 0, 1, 0.15),
        ActivePowerMinThreshold = FiniteOrDefault(settings.ActivePowerMinThreshold, 0),
        PowerFactorMinThreshold = ClampFinite(settings.PowerFactorMinThreshold, 0, 1, 0.9),
        TemperatureMinThreshold = ClampFinite(settings.TemperatureMinThreshold, -50, 200, 0),
        TemperatureMaxThreshold = ClampFinite(settings.TemperatureMaxThreshold, -50, 200, 70),
        CurrentActiveMinThreshold = ClampFinite(settings.CurrentActiveMinThreshold, 0.001, 100000, 1),
        FreezeCountThreshold = ClampFinite(Math.Round(settings.FreezeCountThreshold), 1, 1000, 3),
        FreezeStdThreshold = ClampFinite(settings.FreezeStdThreshold, 0, 1, 0.01),
        VoltageImbalanceThreshold = ClampFinite(settings.VoltageImbalanceThreshold, 0.001, 1, 0.02),
        CurrentOverloadEnabled = settings.CurrentOverloadEnabled,
        CurrentUnbalanceEnabled = settings.CurrentUnbalanceEnabled,
        PowerFactorEnabled = settings.PowerFactorEnabled,
        DetailOutputEnabled = settings.DetailOutputEnabled,
    };

    private static string? ValidateThresholdOrder(DetectionConfigSettings settings)
    {
        if (settings.VoltageMinThreshold > settings.VoltageMaxThreshold)
        {
            return "电压下限不能高于电压上限。";
        }

        return settings.TemperatureMinThreshold > settings.TemperatureMaxThreshold
            ? "温度下限不能高于温度上限。"
            : null;
    }

    private static double GetDouble(JsonElement root, string key, double fallback)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        try
        {
            return value.ValueKind switch
            {
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.String when TryReadNumeric(value, out var parsed) => parsed,
                _ => fallback,
            };
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static bool GetBool(JsonElement root, string key, bool fallback)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    private static double ClampFinite(double value, double min, double max, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Clamp(value, min, max);

    private static double FiniteOrDefault(double value, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
}
