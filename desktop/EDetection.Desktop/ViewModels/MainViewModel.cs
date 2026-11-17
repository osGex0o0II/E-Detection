using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDetection.Desktop.Models;
using EDetection.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace EDetection.Desktop.ViewModels;

public partial class MainViewModel
    : ObservableObject,
        INetworkProxySettings,
        INtfyNotificationSettings,
        ILlmAssistantSettings
{
    public const int MinWindowWidthDip = 960;
    public const int MinWindowHeightDip = 700;
    public const int DefaultWindowWidthDip = 1240;
    public const int DefaultWindowHeightDip = 760;
    private const string WindowsNotificationSettingsUri = "ms-settings:notifications";
    private const string WindowsProxySettingsUri = "ms-settings:network-proxy";
    private const string WindowsStartupAppsSettingsUri = "ms-settings:startupapps";
    private const string OfficialReleaseBaseUrl = "https://github.com/osGex0o0II/E-Detection/releases/";

    private readonly SettingsService _settings;
    private readonly DetectionConfigService _detectionConfig;
    private readonly DesktopDiagnosticsService _diagnostics;
    private readonly RunEventService _runEvents;
    private readonly ReportDetailPreviewService _detailPreview;
    private readonly RunStateService _runState;
    private readonly StartupService _startup;
    private readonly SecureCredentialService _credentials;
    private readonly NtfyNotificationService _ntfyNotifications;
    private readonly LlmAssistantService _llmAssistant;
    private readonly NetworkProxyService _networkProxy;
    private readonly UpdateCheckService _updateCheck;
    private readonly PoetryStatusService _poetryStatus;
    private readonly ReportSummaryComposer _reportSummaryComposer;
    private readonly Stopwatch _runStopwatch = new();
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _cancelPromptCts;
    private bool _settingsLoaded;
    private bool _desktopNotificationSent;
    private bool _syncingStartupPreference;
    private bool _isShellShutdownRequested;
    private bool _applyingDetectionConfig;
    private string _activeDetectionConfigPath = "";
    private DetectionConfigSettings _loadedDetectionConfig = new();
    private string _lastNotifiedUpdateVersion = "";

    public MainViewModel(
        SettingsService settings,
        DetectionConfigService detectionConfig,
        DesktopDiagnosticsService diagnostics,
        ReportHistoryService reportHistory,
        RuntimeLogService runtimeLogs,
        RunTelemetryService runTelemetry,
        RunEventService runEvents,
        ReportDetailPreviewService detailPreview,
        RunStateService runState,
        StartupService startup,
        SecureCredentialService? credentials = null,
        NtfyNotificationService? ntfyNotifications = null,
        LlmAssistantService? llmAssistant = null,
        NetworkProxyService? networkProxy = null,
        UpdateCheckService? updateCheck = null,
        PoetryStatusService? poetryStatus = null,
        DesktopHealthService? desktopHealth = null,
        ReportSummaryComposer? reportSummaryComposer = null)
    {
        _settings = settings;
        _detectionConfig = detectionConfig;
        _diagnostics = diagnostics;
        _runEvents = runEvents;
        _detailPreview = detailPreview;
        _runState = runState;
        _startup = startup;
        _credentials = credentials ?? new SecureCredentialService();
        _ntfyNotifications = ntfyNotifications ?? new NtfyNotificationService(_credentials);
        _llmAssistant = llmAssistant ?? new LlmAssistantService(_credentials);
        _networkProxy = networkProxy ?? new NetworkProxyService(_credentials);
        _updateCheck = updateCheck ?? new UpdateCheckService();
        _poetryStatus = poetryStatus ?? new PoetryStatusService();
        _reportSummaryComposer = reportSummaryComposer ?? new ReportSummaryComposer();
        Diagnostics = new DiagnosticsViewModel();
        RunTelemetry = new RunTelemetryViewModel(runTelemetry);
        RuntimeLogs = new RuntimeLogViewModel(runtimeLogs);
        ReportHistory = new ReportHistoryViewModel(reportHistory);
        DesktopHealth = new DesktopHealthViewModel(
            desktopHealth ?? new DesktopHealthService(),
            _startup,
            _settings);
        var saved = _settings.Load();
        InputDirectory = saved.InputDirectory;
        OutputDirectory = saved.OutputDirectory;
        InitializeDetectionConfig(saved.ConfigPath);
        WriteReport = saved.WriteReport;
        CloseToTrayOnClose = saved.CloseToTrayOnClose;
        StartMinimizedToTray = saved.StartMinimizedToTray;
        var startupStatus = _startup.GetStatus();
        AutoStartOnSignIn = startupStatus.IsEnabled;
        UpdateStartupIntegrationStatus(startupStatus);
        EnableDesktopNotifications = saved.EnableDesktopNotifications;
        EnableLlmAssistant = saved.EnableLlmAssistant;
        LlmEndpoint = saved.LlmEndpoint;
        LlmModel = saved.LlmModel;
        UseProxyForLlm = saved.UseProxyForLlm;
        EnableNtfyNotifications = saved.EnableNtfyNotifications;
        NtfyServerUrl = saved.NtfyServerUrl;
        NtfyTopic = saved.NtfyTopic;
        SelectedNtfyPriorityIndex = Math.Clamp(saved.SelectedNtfyPriorityIndex, 0, 4);
        UseProxyForNotifications = saved.UseProxyForNotifications;
        EnableNetworkProxy = saved.EnableNetworkProxy;
        ProxyAddress = saved.ProxyAddress;
        ProxyRequiresAuthentication = saved.ProxyRequiresAuthentication;
        ProxyUserName = saved.ProxyUserName;
        EnableUpdateChecks = saved.EnableUpdateChecks;
        UseProxyForUpdates = saved.UseProxyForUpdates;
        SelectedUpdateChannelIndex = Math.Clamp(saved.SelectedUpdateChannelIndex, 0, 1);
        UpdateFeedUrl = saved.UpdateFeedUrl;
        var fullVersion = new AppInfoService().GetInfo(typeof(MainViewModel).Assembly).Version;
        UpdateStatusText = $"当前版本 {AppInfoService.FormatDisplayVersion(fullVersion)}";
        UpdateStatusToolTipText = $"当前版本 {fullVersion}";
        RefreshSecureCredentialStatus();
        RuntimeLogs.SelectedRetentionIndex = Math.Clamp(saved.SelectedLogRetentionIndex, 0, 3);
        SelectedThemeIndex = Math.Clamp(saved.SelectedThemeIndex, 0, 2);
        SelectedBackdropIndex = Math.Clamp(saved.SelectedBackdropIndex, 0, 2);
        EnablePoetryStatus = saved.EnablePoetryStatus;
        PoetryServiceUrl = string.IsNullOrWhiteSpace(saved.PoetryServiceUrl)
            ? "https://poetry.palemoky.com/"
            : saved.PoetryServiceUrl;
        SelectedPoetryLanguageIndex = Math.Clamp(saved.SelectedPoetryLanguageIndex, 0, 1);
        WindowLeft = saved.WindowLeft;
        WindowTop = saved.WindowTop;
        WindowWidth = saved.WindowWidth;
        WindowHeight = saved.WindowHeight;
        IsWindowMaximized = saved.IsWindowMaximized;
        ReportHistory.Load(saved.RecentReports, saved.SelectedRecentReportLimitIndex);
        ReportHistory.PropertyChanged += ReportHistory_PropertyChanged;
        Diagnostics.PropertyChanged += Diagnostics_PropertyChanged;
        RunTelemetry.PropertyChanged += RunTelemetry_PropertyChanged;
        RuntimeLogs.PropertyChanged += RuntimeLogs_PropertyChanged;
        RefreshLocalDiagnostics();
        RefreshDesktopHealth();

        _settingsLoaded = true;
    }

    public ObservableCollection<DetectionLogItem> LogItems => RuntimeLogs.LogItems;

    public ObservableCollection<DetectionLogItem> FilteredLogItems => RuntimeLogs.FilteredLogItems;

    public ObservableCollection<string> LogKindFilters => RuntimeLogs.LogKindFilters;

    public ObservableCollection<RecentReport> RecentReports => ReportHistory.RecentReports;

    public ObservableCollection<RecentReport> FilteredRecentReports => ReportHistory.FilteredRecentReports;

    public ObservableCollection<ReportDeviceSummary> HighRiskDevices { get; } = [];

    public ObservableCollection<ReportIssueType> TopIssueTypes { get; } = [];

    public ObservableCollection<ReportDetailPreview> DetailPreview { get; } = [];

    public ObservableCollection<ReportDetailPreview> FilteredDetailPreview { get; } = [];

    public ObservableCollection<string> DetailIssueTypeFilters { get; } = ["全部类型"];

    public bool HasSelectedDetail => SelectedDetail is not null;

    public bool IsIdle => !IsRunning;

    public bool CanEditRunConfiguration => !IsRunning;

    public Visibility CancelCommandVisibility => IsRunning
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility StatusProgressVisibility => IsRunning
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RunTelemetryVisibility =>
        SelectedReport is null
        && (IsRunning
            || TotalFiles > 0
            || ProcessedFiles > 0
            || !string.IsNullOrWhiteSpace(CurrentFileText))
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility CancelConfirmationVisibility => IsCancelConfirmationPending
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string CancelButtonText => IsCancelConfirmationPending ? "确认取消" : "取消";

    public string CancelConfirmationText =>
        "再次按 Esc 或点击“确认取消”停止检测。当前检测任务会被终止。";

    public Visibility CompletionActionsVisibility => ShouldShowCompletionActions
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string CompletionActionTitleText => AnomalyRecords > 0
        ? $"检测完成，发现 {AnomalyRecords} 条异常"
        : "检测完成，未发现异常";

    public string CompletionActionBodyText
    {
        get
        {
            var reportText = string.IsNullOrWhiteSpace(ReportPath)
                ? "未生成 Excel 报告"
                : $"报告已生成: {Path.GetFileName(ReportPath)}";
            return $"{ProcessedSummary} 个文件 · 异常文件 {AnomalyFiles} · 跳过 {SkippedFiles} · {reportText}";
        }
    }

    private bool ShouldShowCompletionActions =>
        !IsRunning && SelectedReport is null && StatusText == "检测完成";

    public string ProcessedSummary => TotalFiles > 0
        ? $"{ProcessedFiles}/{TotalFiles}"
        : ProcessedFiles.ToString();

    public string ProgressText => $"{ProgressPercent:0}%";

    public string SensorOverviewText =>
        $"离线 {SensorOfflineDevices} · 传感器故障 {SensorFaultRows} · 未配置 {SensorMissingRows} · 跳过 {SensorSkippedRows}";

    public string DetailPreviewStatusText => DetailPreviewTotalCount > 0
        ? $"{FilteredDetailPreview.Count}/{DetailPreviewTotalCount} 条预览"
        : "暂无异常明细";

    public string LogStatusText => RuntimeLogs.StatusText;

    public int LogRetentionLimit => RuntimeLogs.RetentionLimit;

    public string LogRetentionText => RuntimeLogs.RetentionText;

    public int RecentReportLimit => ReportHistory.RecentReportLimit;

    public string RecentReportLimitText => ReportHistory.RecentReportLimitText;

    public int WorkbenchReportDeviceCount => SelectedReport?.DeviceCount ?? ReportDeviceCount;

    public IEnumerable<ReportDeviceSummary> WorkbenchHighRiskDevices =>
        SelectedReport is { } report ? report.HighRiskDevices : HighRiskDevices;

    public IEnumerable<ReportIssueType> WorkbenchTopIssueTypes =>
        SelectedReport is { } report ? report.TopIssueTypes : TopIssueTypes;

    public string WorkbenchSensorOverviewText => SelectedReport is { } report
        ? SensorOverviewTextFrom(report.SensorOverview)
        : SensorOverviewText;

    public int WorkbenchDetailPreviewTotalCount =>
        SelectedReport?.DetailPreviewCount ?? DetailPreviewTotalCount;

    public IEnumerable<ReportDetailPreview> WorkbenchFilteredDetailPreview =>
        SelectedReport is { } report
            ? _detailPreview.Filter(
                report.DetailPreview,
                DetailIssueTypeFilters,
                BuildDetailFilterState())
            : FilteredDetailPreview;

    public string WorkbenchDetailPreviewStatusText => WorkbenchDetailPreviewTotalCount > 0
        ? $"{WorkbenchFilteredDetailPreview.Count()}/{WorkbenchDetailPreviewTotalCount} 条预览"
        : "暂无异常明细";

    public string ReportHistoryStatusText => ReportHistory.StatusText;

    public bool IsShowingReportSnapshot => SelectedReport is not null;

    public string WorkbenchModeText => IsShowingReportSnapshot ? "历史报告快照" : "当前检测状态";

    public string WorkbenchStatusText => SelectedReport is { } report
        ? report.FileName
        : StatusText;

    public string WorkbenchProgressText => SelectedReport is { TotalFiles: > 0 }
        ? "历史"
        : ProgressText;

    public double WorkbenchProgressPercent => SelectedReport is { TotalFiles: > 0 }
        ? 100
        : ProgressPercent;

    public string WorkbenchProcessedSummary => SelectedReport is { } report
        ? (report.TotalFiles > 0 ? $"{report.ProcessedFiles}/{report.TotalFiles}" : "-")
        : ProcessedSummary;

    public int WorkbenchAnomalyFiles => SelectedReport?.AnomalyFiles ?? AnomalyFiles;

    public int WorkbenchAnomalyRecords => SelectedReport?.AnomalyRecords ?? AnomalyRecords;

    public int WorkbenchSkippedFiles => SelectedReport?.SkippedFiles ?? SkippedFiles;

    public string WorkbenchReportPath => SelectedReport?.Path ?? ReportPath;

    public string WorkbenchReportPathText => string.IsNullOrWhiteSpace(WorkbenchReportPath)
        ? "尚未生成报告"
        : WorkbenchReportPath;

    public string WorkbenchReportButtonText => IsShowingReportSnapshot ? "打开选中报告" : "打开最新报告";

    public string WorkbenchReportFolderButtonText => IsShowingReportSnapshot ? "选中报告目录" : "打开所在目录";

    public Visibility ReportActionsVisibility => string.IsNullOrWhiteSpace(WorkbenchReportPath) || ShouldShowCompletionActions
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility WorkbenchActivityVisibility =>
        IsRunning
        || IsCancelConfirmationPending
        || ShouldShowCompletionActions
        || HasActionableFailure
        || IsShowingReportSnapshot
        || !string.IsNullOrWhiteSpace(WorkbenchReportPath)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string WorkbenchActivityTitleText => IsShowingReportSnapshot
        ? "报告快照"
        : IsRunning
            ? "运行详情"
                : HasActionableFailure
                    ? "处理建议"
                    : ShouldShowCompletionActions
                        ? "完成摘要"
                        : "报告操作";

    public string WorkbenchContextText => SelectedReport is { } report
        ? $"{report.SourceText} · {report.RunMetaText}"
        : IsRunning && !string.IsNullOrWhiteSpace(ActiveRunSummaryText)
            ? ActiveRunSummaryText
            : !string.IsNullOrWhiteSpace(ReportPath)
                ? $"最新运行结果 · {Path.GetFileName(ReportPath)}"
                : "等待检测任务";

    public string WorkbenchStatusBadgeText => SelectedReport is not null
        ? ReportHistoryStatusText
        : IsRunning
            ? CurrentFileText
                : !string.IsNullOrWhiteSpace(ReportPath)
                    ? "报告已生成"
                    : "待开始";

    public Visibility WorkbenchStatusBadgeVisibility =>
        SelectedReport is not null
        || IsRunning
        || !string.IsNullOrWhiteSpace(ReportPath)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string RunSetupReadinessText
    {
        get
        {
            if (IsRunning)
            {
                return "正在检测";
            }

            if (IsDetectionConfigDirty)
            {
                return "检测阈值等待保存";
            }

            if (string.IsNullOrWhiteSpace(_activeDetectionConfigPath))
            {
                return "需要加载有效阈值文件";
            }

            if (StatusText == "无法开始检测")
            {
                return "需要处理后再开始";
            }

            if (StatusText == "检测失败")
            {
                return "检测未完成";
            }

            if (!string.IsNullOrWhiteSpace(ReportPath))
            {
                return "检测完成";
            }

            if (!IsLocalInputReady())
            {
                return "选择检测数据";
            }

            if (!IsConfigReady())
            {
                return "确认阈值设置";
            }

            return "准备开始检测";
        }
    }

    public string RunSetupReadinessDetailText
    {
        get
        {
            if (IsRunning)
            {
                return string.IsNullOrWhiteSpace(CurrentFileText)
                    ? "检测正在运行，完成后可在右侧查看结果。"
                    : CurrentFileText;
            }

            if (IsDetectionConfigDirty)
            {
                return "请先在设置中保存修改后的检测阈值，再开始检测。";
            }

            if (string.IsNullOrWhiteSpace(_activeDetectionConfigPath))
            {
                return "当前阈值文件尚未通过校验，请在设置中加载或另存为有效文件。";
            }

            if (StatusText == "无法开始检测" || StatusText == "检测失败")
            {
                return LastFailureText == "暂无失败"
                    ? DiagnosticActionText
                    : LastFailureText;
            }

            if (!string.IsNullOrWhiteSpace(ReportPath))
            {
                return $"最新报告已生成: {Path.GetFileName(ReportPath)}";
            }

            if (!IsLocalInputReady())
            {
                return "选择包含 CSV 文件的目录后即可开始。";
            }

            if (!IsConfigReady())
            {
                return "请在设置中确认阈值设置和检测规则。";
            }

            return "选择完成后可直接开始，必要检查会自动执行。";
        }
    }

    public ShellStatusSnapshot ShellStatus => new(
        StatusText,
        IsRunning,
        TaskbarProgressKind,
        TaskbarProgressPercent);

    public Visibility ActiveRunSummaryVisibility => string.IsNullOrWhiteSpace(ActiveRunSummaryText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FailureActionsVisibility => HasActionableFailure
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string FailureActionTitleText => StatusText == "无法开始检测"
        ? "运行前检查未通过"
        : "检测失败";

    public string FailureActionBodyText => LastFailureText == "暂无失败"
        ? DiagnosticActionText
        : LastFailureText;

    public Visibility SnapshotActionsVisibility => IsShowingReportSnapshot
        ? Visibility.Visible
        : Visibility.Collapsed;

    private bool HasActionableFailure =>
        !IsRunning && (StatusText == "检测失败" || StatusText == "无法开始检测");

    public Visibility FirstRunGuideVisibility =>
        ShouldShowFirstRunGuide ? Visibility.Visible : Visibility.Collapsed;

    public string FirstRunGuideTitleText => StatusText == "无法开始检测"
        ? "运行前检查未通过"
        : IsLocalInputReady()
            ? "准备开始检测"
            : "选择检测数据";

    public string FirstRunGuideSubtitleText
    {
        get
        {
            if (StatusText == "无法开始检测")
            {
                return DiagnosticActionText;
            }

            if (!IsLocalInputReady())
            {
                return "左侧选择 CSV 根目录后即可进行就绪检查。";
            }

            if (!IsConfigReady())
            {
                return "确认阈值设置和检测规则后检查状态。";
            }

            if (BackendDiagnosticText.Contains("可导入", StringComparison.Ordinal))
            {
                return "运行环境就绪，可以开始检测。";
            }

            return DiagnosticActionText;
        }
    }

    public string FirstRunInputStepText => IsLocalInputReady()
        ? InputDiagnosticText
        : "未选择可用的 CSV 根目录";

    public string FirstRunConfigStepText => ConfigDiagnosticText;

    public string FirstRunBackendStepText => BackendDiagnosticText;

    public string FirstRunOutputStepText => OutputDiagnosticText;

    private bool ShouldShowFirstRunGuide =>
        !IsRunning
        && SelectedReport is null
        && TotalFiles == 0
        && string.IsNullOrWhiteSpace(ReportPath);

    public string ThemeMode => SelectedThemeIndex switch
    {
        1 => "Light",
        2 => "Dark",
        _ => "Default",
    };

    public string BackdropMode => SelectedBackdropIndex switch
    {
        1 => "Acrylic",
        2 => "None",
        _ => "Mica",
    };

    public event EventHandler? AppearanceChanged;

    public event EventHandler<DesktopNotificationRequest>? DesktopNotificationRequested;

    public string SettingsStoreStatusText => _settings.StoreStatusText;

    public DesktopHealthViewModel DesktopHealth { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public RunTelemetryViewModel RunTelemetry { get; }

    public RuntimeLogViewModel RuntimeLogs { get; }

    public ReportHistoryViewModel ReportHistory { get; }
}
