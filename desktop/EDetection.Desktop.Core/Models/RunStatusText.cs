namespace EDetection.Desktop.Models;

/// <summary>
/// The run-state strings that <c>StatusText</c> carries.  They used to be
/// literals spread across the backend event mapper and six UI decisions, so
/// editing one copy silently broke the completion panel, the failure panel and
/// the readiness copy while everything still compiled.  Write and read them
/// from here instead.
/// </summary>
public static class RunStatusText
{
    public const string Ready = "就绪";

    public const string Preparing = "正在准备检测...";

    public const string Completed = "检测完成";

    public const string Cancelled = "检测已取消";

    public const string Failed = "检测失败";

    public const string Blocked = "无法开始检测";

    /// <summary>Statuses that leave the workbench showing an actionable failure.</summary>
    public static bool IsFailure(string? statusText) =>
        statusText is Failed or Blocked;
}
