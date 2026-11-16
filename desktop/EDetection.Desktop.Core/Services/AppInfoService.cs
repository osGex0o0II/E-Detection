using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using EDetection.Desktop.Models;

namespace EDetection.Desktop.Services;

public sealed class AppInfoService
{
    private const int DisplayBuildHashLength = 7;

    public static string FormatDisplayVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex < 0 || metadataIndex == version.Length - 1)
        {
            return version;
        }

        var metadata = version[(metadataIndex + 1)..];
        var shortLength = Math.Min(metadata.Length, DisplayBuildHashLength);
        return $"{version[..(metadataIndex + 1)]}{metadata[..shortLength]}";
    }

    public AppInfo GetInfo(Assembly? hostAssembly = null)
    {
        var assembly = hostAssembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
        var version = versionInfo.ProductVersion
            ?? versionInfo.FileVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        return new AppInfo(
            string.IsNullOrWhiteSpace(versionInfo.ProductName)
                ? "EDetection"
                : versionInfo.ProductName,
            version,
            string.IsNullOrWhiteSpace(versionInfo.FileDescription)
                ? "Windows native workbench for electrical anomaly analysis."
                : versionInfo.FileDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
    }
}
