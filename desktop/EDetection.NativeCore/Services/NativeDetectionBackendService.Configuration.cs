using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using EDetection.NativeCore.Models;

namespace EDetection.NativeCore.Services;

public sealed partial class NativeDetectionBackendService : IDetectionBackendService
{
    private static NativeConfigLoadResult LoadConfig(string? configPath)
    {
        var config = new NativeDetectionConfig();
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return NativeConfigLoadResult.Success(config);
        }

        try
        {
            var resolvedPath = Path.GetFullPath(configPath);
            if (!File.Exists(resolvedPath))
            {
                return NativeConfigLoadResult.Success(config);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(resolvedPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return NativeConfigLoadResult.Error(
                    "ValueError",
                    $"配置文件格式错误: {resolvedPath}，根节点必须是对象。");
            }

            var root = document.RootElement;
            var currentOverloadEnabled = GetBool(root, "current_overload", config.CurrentOverloadEnabled);
            var currentUnbalanceEnabled = GetBool(root, "current_unbalance", config.CurrentUnbalanceEnabled);
            if (root.TryGetProperty("current", out var legacyCurrent)
                && legacyCurrent.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                currentOverloadEnabled = legacyCurrent.GetBoolean();
                currentUnbalanceEnabled = legacyCurrent.GetBoolean();
            }

            var loaded = new NativeDetectionConfig(
                GetFiniteDouble(root, "V_MIN_THRESHOLD", config.VoltageMinThreshold),
                GetFiniteDouble(root, "V_MAX_THRESHOLD", config.VoltageMaxThreshold),
                GetFiniteDouble(root, "V_IMBALANCE_THRESHOLD", config.VoltageImbalanceThreshold),
                GetFiniteDouble(root, "I_MAX_THRESHOLD", config.CurrentMaxThreshold),
                GetFiniteDouble(root, "I_UNBALANCE_MAX_THRESHOLD", config.CurrentUnbalanceMaxThreshold),
                GetFiniteDouble(root, "I_MIN_ACTIVE_THRESHOLD", config.CurrentActiveMinThreshold),
                GetFiniteDouble(root, "P_ACTIVE_MIN_THRESHOLD", config.ActivePowerMinThreshold),
                GetFiniteDouble(root, "PF_MIN_THRESHOLD", config.PowerFactorMinThreshold),
                GetFiniteDouble(root, "T_MIN_THRESHOLD", config.TemperatureMinThreshold),
                GetFiniteDouble(root, "T_MAX_THRESHOLD", config.TemperatureMaxThreshold),
                currentOverloadEnabled,
                currentUnbalanceEnabled,
                GetBool(root, "power_factor", config.PowerFactorEnabled),
                GetBool(root, "detail_output", config.DetailOutputEnabled),
                GetPositiveInt(root, "FREEZE_COUNT_THRESHOLD", config.FreezeCountThreshold),
                GetFiniteDouble(root, "FREEZE_STD_THRESHOLD", config.FreezeStdThreshold));
            if (loaded.VoltageMinThreshold > loaded.VoltageMaxThreshold)
            {
                return NativeConfigLoadResult.Error(
                    "ValueError",
                    "配置文件数值无效: 电压下限不能高于电压上限。");
            }

            if (loaded.TemperatureMinThreshold > loaded.TemperatureMaxThreshold)
            {
                return NativeConfigLoadResult.Error(
                    "ValueError",
                    "配置文件数值无效: 温度下限不能高于温度上限。");
            }

            return NativeConfigLoadResult.Success(loaded);
        }
        catch (JsonException ex)
        {
            return NativeConfigLoadResult.Error(
                "ValueError",
                $"配置文件无法解析: {ResolveDisplayPath(configPath)} ({ex.Message})");
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException)
        {
            return NativeConfigLoadResult.Error(
                ex.GetType().Name,
                $"配置文件不可读取: {ResolveDisplayPath(configPath)} ({ex.Message})");
        }
        catch (Exception ex) when (ex is FormatException
                                   or OverflowException
                                   or InvalidOperationException)
        {
            return NativeConfigLoadResult.Error(
                "ValueError",
                $"配置文件数值无效: {ResolveDisplayPath(configPath)} ({ex.Message})");
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return NativeConfigLoadResult.Error(
                ex.GetType().Name,
                $"配置文件路径错误: {configPath} ({ex.Message})");
        }
    }

    private static string ResolveDisplayPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return path;
        }
    }

    private static List<string> EnumerateCsvFiles(
        string inputRoot,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     inputRoot,
                     "*.csv",
                     new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true,
                         MatchCasing = MatchCasing.CaseInsensitive,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                     }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(path);
        }

        cancellationToken.ThrowIfCancellationRequested();
        files.Sort(StringComparer.OrdinalIgnoreCase);
        cancellationToken.ThrowIfCancellationRequested();
        return files;
    }

}
