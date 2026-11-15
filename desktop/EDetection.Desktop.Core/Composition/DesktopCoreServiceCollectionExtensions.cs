using EDetection.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EDetection.Desktop.Composition;

/// <summary>
/// Registers the WinUI-free application services.  The shell registers the
/// platform-specific implementations (credential store, startup providers,
/// desktop notifications) on top of this.
/// </summary>
public static class DesktopCoreServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopCore(this IServiceCollection services)
    {
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DetectionConfigService>();
        services.AddSingleton<DesktopDiagnosticsService>();
        services.AddSingleton<ReportHistoryService>();
        services.AddSingleton<RuntimeLogService>();
        services.AddSingleton<RunTelemetryService>();
        services.AddSingleton<RunEventService>();
        services.AddSingleton<ReportDetailPreviewService>();
        services.AddSingleton<ReportSummaryComposer>();
        services.AddSingleton<RunStateService>();
        services.AddSingleton<NetworkProxyService>();
        services.AddSingleton<NtfyNotificationService>();
        services.AddSingleton<LlmAssistantService>();
        services.AddSingleton<UpdateCheckService>();
        services.AddSingleton<PoetryStatusService>();
        services.AddSingleton<StartupService>();
        return services;
    }
}
