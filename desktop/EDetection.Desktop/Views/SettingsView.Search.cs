using Microsoft.UI.Xaml.Controls;

namespace EDetection.Desktop.Views;

public sealed partial class SettingsView
{
    private void SettingsSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var query = sender.Text.Trim();
        sender.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? null
            : _searchEntries
                .Where(entry => entry.Matches(query))
                .Take(8)
                .ToList();
    }

    private void SettingsSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not SettingsSearchEntry entry)
        {
            return;
        }

        sender.Text = entry.Title;
        NavigateToSearchEntry(entry);
    }

    private void SettingsSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SettingsSearchEntry chosenEntry)
        {
            NavigateToSearchEntry(chosenEntry);
            return;
        }

        var query = args.QueryText.Trim();
        var entry = _searchEntries.FirstOrDefault(item => item.Matches(query));
        if (entry is not null)
        {
            NavigateToSearchEntry(entry);
        }
    }

    private void NavigateToSearchEntry(SettingsSearchEntry entry)
    {
        ExpandSearchEntryContainers(entry);
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateResponsiveLayout();
            SelectCategoryBySection(entry.SectionName);
            NavigateToElementByName(entry.TargetName ?? entry.SectionName);
        });
    }

    private static SettingsSearchEntry[] BuildSearchEntries() =>
    [
        new("外观", "常规", "外观 主题 背景 mica acrylic 深色 浅色 跟随系统 诗词", "AppearanceSection"),
        new("应用主题", "常规", "外观 主题 深色 浅色 跟随系统", "AppearanceSection"),
        new("窗口背景", "常规", "外观 背景 mica acrylic", "AppearanceSection"),
        new("顶部诗词", "常规", "诗词 poetry palemoky 顶部 信息栏 来源", "AppearanceSection", TargetName: "PoetryOptionsExpander", ExpanderName: "PoetryOptionsExpander"),
        new("检测参数", "检测", "检测 参数 阈值 设置 规则", "DefaultsSection"),
        new("阈值设置", "检测", "阈值 电压 电流 功率 温度 数据不变化 不平衡", "ThresholdsSection"),
        new("阈值文件路径", "检测", "阈值 配置 文件 json config 路径", "ThresholdsSection", TargetName: "ThresholdConfigPathRow"),
        new("电压阈值", "检测", "阈值 电压 下限 上限 不平衡", "ThresholdsSection", TargetName: "VoltageMinThresholdRow"),
        new("电流阈值", "检测", "阈值 电流 上限 不平衡 激活", "ThresholdsSection", true, "CurrentMaxThresholdRow"),
        new("功率因数阈值", "检测", "阈值 功率 因数 下限", "ThresholdsSection", TargetName: "PowerFactorThresholdRow"),
        new("温度阈值", "检测", "阈值 温度 上限 下限", "ThresholdsSection", true, "TemperatureMaxThresholdRow"),
        new("连续不变化点数", "检测", "阈值 冻结 数据不变化 连续点数 样本点 波动 容差", "ThresholdsSection", true, "FreezeCountThresholdRow"),
        new("温度下限", "检测", "阈值 温度 下限", "ThresholdsSection", true, "TemperatureMinThresholdRow"),
        new("电流激活下限", "检测", "阈值 电流 激活 下限", "ThresholdsSection", true, "CurrentActiveMinThresholdRow"),
        new("数据波动容差", "检测", "阈值 冻结 数据 波动 容差", "ThresholdsSection", true, "FreezeStdThresholdRow"),
        new("检测规则", "检测", "规则 电流过载 电流不平衡 功率因数 详细异常输出", "DetectionRulesSection"),
        new("报告设置", "报告与记录", "报告 excel 历史 清空", "ReportsSection"),
        new("运行记录", "报告与记录", "日志 运行记录 保留 清空", "LogsSection"),
        new("窗口设置", "常规", "窗口 托盘 自启动 启动应用 通知", "WindowSection"),
        new("Windows 启动应用设置", "常规", "Windows 系统 启动 应用 设置 startupapps", "WindowSection"),
        new("桌面通知测试", "通知", "桌面通知 通知 测试 toast Windows 系统设置", "WindowSection"),
        new("Windows 通知设置", "通知", "Windows 系统 通知 设置 ms-settings", "WindowSection"),
        new("智能助手", "连接与集成", "llm ai 智能助手 模型 api key 代理 测试", "LlmSection"),
        new("LLM 服务地址", "连接与集成", "llm endpoint 服务地址 模型 api key", "LlmSection", TargetName: "LlmEndpointRow", ExpanderName: "LlmConnectionExpander"),
        new("LLM API Key", "连接与集成", "llm api key 凭据 密钥", "LlmSection", TargetName: "LlmApiKeyRow", ExpanderName: "LlmConnectionExpander"),
        new("消息推送", "通知", "ntfy 通知 推送 主题 token 优先级 代理 测试", "NotificationsSection", TargetName: "NotificationsSection"),
        new("ntfy 服务地址", "通知", "ntfy 服务地址 topic token", "NotificationsSection", TargetName: "NtfyServerRow", ExpanderName: "NtfySettingsExpander"),
        new("ntfy Token", "通知", "ntfy token 凭据 推送", "NotificationsSection", TargetName: "NtfyTokenRow", ExpanderName: "NtfySettingsExpander"),
        new("网络代理", "连接与集成", "代理 proxy 地址 认证 用户名 密码 测试 Windows 系统设置", "ProxySection"),
        new("Windows 代理设置", "连接与集成", "Windows 系统 网络 代理 设置 ms-settings", "ProxySection"),
        new("软件更新", "更新与维护", "更新 版本 检查 通道 代理 更新源 release", "UpdatesSection"),
        new("更新代理", "更新与维护", "更新 网络代理 proxy", "UpdatesSection", TargetName: "UpdateProxyRow", ExpanderName: "AdvancedUpdateExpander"),
        new("更新检查方式", "更新与维护", "更新 自动检查 手动检查", "UpdatesSection", TargetName: "UpdateChannelRow", ExpanderName: "AdvancedUpdateExpander"),
        new("更新源", "更新与维护", "更新源 feed release github", "UpdatesSection", TargetName: "UpdateFeedRow", ExpanderName: "AdvancedUpdateExpander"),
        new("维护", "更新与维护", "维护 应用状态 检测核心 设置存储", "AdvancedSection"),
    ];

    private sealed record SettingsSearchEntry(
        string Title,
        string SectionTitle,
        string Keywords,
        string SectionName,
        bool ExpandMoreThresholds = false,
        string? TargetName = null,
        string? ExpanderName = null)
    {
        public bool Matches(string query) =>
            !string.IsNullOrWhiteSpace(query)
            && (Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || Keywords.Contains(query, StringComparison.OrdinalIgnoreCase));

        public override string ToString() => Title;
    }
}
