# E-Detection 原生桌面架构

本文描述桌面端的模块职责与运行流程。历史实现见 [后端迁移记录](../desktop/NATIVE_BACKEND_MIGRATION.md)。

## 概览

- **系统**：Windows 桌面工作台，输入 SCADA/电力监控 CSV 日报，输出可追溯的电气异常 Excel 报告。
- **UI**：WinUI 3 + Windows App SDK 2.6.5，`WindowsPackageType=None`，自包含发布，不依赖预装 Windows App Runtime。
- **领域层**：`EDetection.Domain` 目前是纯标记程序集，无项目引用；业务模型仍主要位于 `NativeCore` 与 `Desktop.Core`。
- **检测层**：`EDetection.NativeCore` 是纯 `net11.0` 程序集，包含 CSV 解析、规则、事件、汇总和 OpenXML 报告，不依赖 WinUI 或 Windows App SDK。
- **基础设施层**：`EDetection.Infrastructure` 只负责把 `IDetectionBackendService` 适配到 `NativeDetectionBackendService`。
- **应用层**：`EDetection.Desktop.Core` 承载 WinUI-free 的设置、诊断、历史、事件映射、运行状态和报告摘要服务。
- **打包层**：`dotnet publish` 自包含发布 + Inno Setup 7 按用户安装器。
- **运行后端**：`native` 是唯一实现；不存在 Python 运行时、wheelhouse 或双后端分支。

## 分层与依赖

逻辑上希望保持“领域 → 检测核心 → 基础设施/应用服务 → 桌面壳”的单向依赖。实际 `csproj` 不是一条单链：`Infrastructure` 与 `Desktop.Core` 并列引用 `Domain` 和 `NativeCore`，彼此不引用。

```text
Desktop
├── Desktop.Core ──► Domain
│                └─► NativeCore
├── Infrastructure ──► Domain
│                  └─► NativeCore
└── NativeCore          （无项目引用）
    ├── Services/       （CSV、编排、汇总、报告）
    └── Detection/      （纯行级规则管线）
```

| 项目 | 目标框架 | 直接项目引用 | 关键约束 |
| --- | --- | --- | --- |
| `EDetection.Domain` | `net11.0` | — | 仅 `DomainAssemblyMarker`；不得引用 WinUI / Windows App SDK。 |
| `EDetection.NativeCore` | `net11.0` | — | 纯检测核心；不得引用 WinUI、Windows App SDK 或 `Desktop.Core`；`Detection/` 规则管线不依赖后端服务或文件 IO。 |
| `EDetection.Infrastructure` | `net11.0` | `Domain`、`NativeCore` | 只做 DI 适配，不承载规则。 |
| `EDetection.Desktop.Core` | `net11.0` | `Domain`、`NativeCore` | WinUI-free；不得引用 `Infrastructure`。 |
| `EDetection.Desktop` | `net11.0-windows10.0.19041.0` | `Desktop.Core`、`Infrastructure`、`NativeCore` | 唯一 WinUI 壳；不承载检测规则。 |
| `EDetection.Tests` | `net11.0` | `Desktop.Core`、`Domain`、`Infrastructure` | xUnit v3，组合根与架构测试。 |
| `EDetection.Desktop.Tests` | `net11.0` | `Desktop.Core`、`NativeCore` | 无界面控制台冒烟；启动时断言未加载 UI 运行时。 |

依赖方向只允许向下。新增项目引用时，先检查它是否破坏 `Domain` / `Desktop.Core` 的 WinUI-free 约束和 `NativeCore` 的可独立运行能力。

## 项目职责

### `EDetection.NativeCore`

| 文件 | 职责 |
| --- | --- |
| `NativeDetectionBackendService.Detection.cs` | `RunDetectionAsync` 入口、输入校验、文件枚举、事件发出、汇总与报告调用。 |
| `NativeDetectionBackendService.Configuration.cs` | 配置文件加载、阈值合法性检查、CSV 递归枚举。 |
| `NativeDetectionBackendService.Csv.cs` | 编码候选、CSV 分词、列名归一化、无效值与统计尾行。 |
| `NativeDetectionBackendService.Analysis.cs` | 单文件分析：解析行、组装 `DetectionRowContext`、调用规则管线、离线与传感器状态。 |
| `NativeDetectionBackendService.RuleOptions.cs` | `NativeDetectionConfig` → `DetectionRuleOptions` 映射，配置加载与纯规则管线之间的适配层。 |
| `NativeDetectionBackendService.Rules.cs` | 仅保留离线无效观测计数与传感器状态辅助；规则实现已移入 `Detection/`。 |
| `Detection/DetectionRowContext.cs` | 行上下文 `DetectionRowContext` 与规则接口 `IDetectionRowRule`。 |
| `Detection/DetectionRuleOptions.cs` | 规则阈值与开关记录；默认值与 `NativeDetectionConfig` 一致。 |
| `Detection/DetectionRowRulePipeline.cs` | 固定顺序规则管线及电压/电流/有功功率/功率因数/温度规则实现。 |
| `Detection/FreezeRowDetector.cs` | 行序列冻结/恒定检测、待机行抑制与 `FreezeDetectionRow`。 |
| `Detection/DetectionMath.cs` | 相别映射、中位数、电流活跃判断等内部数学辅助。 |
| `NativeDetectionBackendService.Details.cs` | 异常详情、PT/不平衡/CT 文本、HMAC 对等追踪。 |
| `NativeDetectionBackendService.Summaries.cs` | 设备汇总、问题类型统计、严重等级、时间顺序。 |
| `NativeDetectionBackendService.Reporting.cs` | OpenXML 工作簿生成、样式、XML 校验与原子发布。 |
| `NativeDetectionBackendService.Models.cs` | 内部配置、列组、位置、行/文件分析记录。 |

公开类型按职责划分：

- **检测与运行**：`DetectionRequest`、`DetectionBackendEvent`、`IDetectionBackendService`、`DetectionExecutionService`、`NativeDetectionBackendService`、`DetectionBackendServiceFactory`。
- **报告与诊断**：`NativeParityTrace`、`ReportDetailPreview`、`ReportDeviceSummary`、`ReportIssueType`、`ReportSensorOverview`。
- **检测规则**：`DetectionRowContext`、`DetectionRuleOptions`、`IDetectionRowRule`、`DetectionRowRulePipeline`、`FreezeDetectionRow`、`FreezeRowDetector`。
- **版本处理**：`SemanticVersionComparer`。

配置、列组、行与文件分析、传感器状态等辅助类型保持 `private`。规则可脱离后端服务独立测试。

### 检测规则管线

行级规则位于 `EDetection.NativeCore/Detection/`，是纯 `net11.0` 子命名空间，不引用后端 partial、WinUI 或文件 IO。`NativeDetectionBackendService` 负责解析与编排，规则只消费行上下文：

```text
NativeDetectionBackendService.Analysis.cs
  ├─ BuildRowVoltageValues / BuildRowNumericValues
  │     └─ 重复列组先取平均；只保留本行存在且未被无效值规则剔除的数值
  ├─ new DetectionRowContext(电压、电流、有功、功率因数、温度, 电压列数)
  ├─ DetectionRowRulePipeline.Default.Evaluate(context, ToRuleOptions(config))
  │     ├─ VoltageAnomalyRule
  │     ├─ CurrentAnomalyRule
  │     ├─ ActivePowerAnomalyRule
  │     ├─ PowerFactorAnomalyRule
  │     └─ TemperatureAnomalyRule
  └─ FreezeRowDetector.Detect(FreezeDetectionRow[], options, cancellationToken)
        └─ rowTypes → NativeRowAnalysis → ReportDetailPreview / 汇总 / 工作簿
```

规则顺序是检测契约的一部分，固定为 **电压 → 电流 → 有功功率 → 功率因数 → 温度**；冻结/恒定检测在行级规则之后按整列序列执行，并把 `数据冻结` / `数据恒定` 追加到对应行。后续 `IssueRuleOrder` 只调整结构化明细与摘要的展示顺序，不改变管线输出顺序。

| 契约 | 职责 |
| --- | --- |
| `DetectionRowContext` | 每行的电压/电流/有功功率/功率因数/温度字典；键是归一化列名，字典只含本行可用的测点。`VoltageColumnCount` 是文件发现的电压列组总数（含空单元格），用于区分“全零行”和“缺相行”。 |
| `DetectionRuleOptions` | 16 个阈值与开关，默认值与 `config.json` / `NativeDetectionConfig` 一致；由 `NativeDetectionBackendService.RuleOptions.cs` 的 `ToRuleOptions` 映射。 |
| `IDetectionRowRule` | `Evaluate(context, options, ICollection<string> issueTypes)`；规则只追加异常类型字符串。 |
| `DetectionRowRulePipeline` | `public sealed`，`Default` 为单例；构造器可注入自定义规则，供测试替换顺序或规则集。 |
| `FreezeDetectionRow` / `FreezeRowDetector` | 冻结检测只依赖核心字典（`Uab/Ubc/Uca/Ia/Ib/Ic/有功功率`）与辅助字典（`无功功率/功率因数`）；`Detect` 返回与输入行等长的逐行类型列表。 |
| `DetectionMath` | `internal` 辅助：相别到列名/列名到相别、`Median`、`IsCurrentActive`、可选值读取。 |

稳定性与取消：

- 相别文本集中在 `DetectionMath.VoltagePhaseColumns` / `PhaseForCurrentColumn`；数值格式使用 `InvariantCulture`（`F1`/`F3`），不受运行线程区域设置影响。
- 关闭 `detail_output` 时，电流过大与温度异常按超限相数重复通用标签（例如两个“电流过大”），保留既有行级重数；后续结构化明细只做一次汇总。
- 行级规则本身无 IO，不单独接收取消令牌；`Analysis.cs` 在逐行循环检查取消，`FreezeRowDetector` 在辅助列、核心列、滑窗与输出行逐项检查。
- `DetectionRulePipelineTests.cs` 的 15 项测试覆盖全零行、PT 接线、相电压不平衡（含逐相详情）、多相电压、电流重数、电流不平衡开关、CT 极性、有功下限、功率因数开关、温度相别与通用标签重数、数据冻结、待机行抑制与数据恒定；xUnit 总计 22 项。

### `EDetection.Desktop.Core`

WinUI-free 应用服务，供 WinUI 壳和无界面测试共同引用：

- `SettingsService`：`settings.json` 原子读写、版本迁移、设置目录覆盖。
- `DetectionConfigService`：用户阈值配置的创建、加载、归一化、保存与校验。
- `DesktopDiagnosticsService`：输入目录、配置文件和输出目录的运行前检查。
- `ReportHistoryService`：最近报告增删、筛选、上限裁剪和“最新”标记。
- `RuntimeLogService`、`RunTelemetryService`、`RunEventService`、`RunStateService`：运行日志、遥测、事件映射与状态快照。
- `ReportDetailPreviewService`：异常明细的搜索、等级/类型筛选、排序。
- `ReportSummaryComposer`：生成可复制的检测汇报文本。
- `NetworkProxyService`、`NtfyNotificationService`、`LlmAssistantService`、`UpdateCheckService`、`PoetryStatusService`：可选网络能力。
- `StartupService`、`StartupDiagnostics`：登录启动与启动诊断。

### `EDetection.Desktop`

WinUI 3 壳只负责界面与系统集成：

- `App.xaml.cs`：单实例注册、Host 构建、启动参数、窗口创建。
- `MainWindow.xaml.cs`：窗口放置、标题栏、托盘、任务栏图标、桌面通知、会话结束处理。
- `Views/AppShellView.xaml`：标题栏、状态带、工作台/设置页切换。
- `Views/RunSetupView.xaml`：输入、输出、运行开关、报告历史。
- `Views/DetectionWorkbenchView.xaml`：指标卡、风险设备、异常类型、异常明细、运行记录。
- `Views/SettingsView.xaml` 及其 partial 文件：设置分区、搜索、导航和动作；通过 `SettingsLayoutStates` 适配窗口宽度。
- `Views/Settings/DetectionRulesSettingsSection.xaml`：独立检测规则设置分区；可由设置页搜索与分类导航定位。
- 设置卡片使用 `CommunityToolkit.WinUI.Controls.SettingsControls`；`UpdateResponsiveLayout` 更新响应式状态。
- `ViewModels/MainViewModel*.cs`：命令、属性、设置持久化、报告文本、运行事件编排。
- `Composition/AppHost.cs`：WinUI 壳的 DI 组合根。
- `Services/DesktopHealthService.cs`：应用、通知、启动集成、设置存储和包健康状态。
- `Services/SecureCredentialService.cs`：Windows Credential Locker 封装。

## 组合根与 DI

`AppHost.Build()` 使用 `Microsoft.Extensions.Hosting` 创建 Host，清空默认日志提供程序，然后按以下顺序注册：

```csharp
services.AddSingleton<SecureCredentialService>();
services.AddSingleton<IAppCredentialStore>(
    provider => provider.GetRequiredService<SecureCredentialService>());
services.AddSingleton<IStartupIntegrationProvider>(
    _ => new FallbackStartupIntegrationProvider(
        new TaskSchedulerStartupIntegrationProvider(),
        new RegistryRunStartupIntegrationProvider()));
services.AddDesktopCore();
services.AddInfrastructure();
services.AddSingleton<DesktopHealthService>();
services.AddSingleton<MainViewModel>();
services.AddSingleton<MainWindow>();
```

`AddDesktopCore()` 注册设置、报告、运行状态与可选网络服务；`AddInfrastructure()` 注册 `IDetectionBackendService` 的原生实现。`AppHost` 另外注册凭据存储、启动集成、健康检查和 UI 对象。

所有服务均为单例。`MainViewModel` 的命令入口当前通过 `DetectionBackendServiceFactory.CreateDefault()` 创建检测后端；容器中注册的 `IDetectionBackendService` 与工厂返回同一实现，便于组合根测试和无界面替换。

## 启动与单实例

```text
App.OnLaunched
  ├─ AppInstance.FindOrRegisterForKey("EDetection.Desktop.MainInstance")
  │    ├─ 已有实例：RedirectActivationToAsync（静默启动除外）→ Exit(0)
  │    └─ 当前实例：注册 Activated，继续
  ├─ AppHost.Build()
  ├─ SettingsService.Load()
  ├─ MainWindow（构造 MainViewModel、托盘、通知、窗口消息）
  ├─ 需要时隐藏到托盘（--startup-minimized / --background-startup / 设置）
  └─ MainWindow.Activate()
```

`App.xaml.cs` 还捕获 WinUI 与 AppDomain 未处理异常并写入启动诊断；`MainWindow` 在关闭时可取消关闭并隐藏到托盘，退出托盘或系统会话结束时才真正释放 Host。

## 运行生命周期

```text
MainWindow / RunSetupView
  └─ MainViewModel.StartCommand
      ├─ EnsureReadyToStartAsync        # 输入、配置、输出目录检查
      ├─ SaveDetectionConfig()          # 保存用户阈值
      ├─ EnsureEffectiveConfigPath()    # 确保存在用户 config
      ├─ ResetRunState()                # 清理上次摘要、筛选、日志、进度
      ├─ new DetectionRequest { ... }
      ├─ new CancellationTokenSource()
      ├─ new Progress<DetectionBackendEvent>(HandleBackendEvent)
      └─ DetectionExecutionService.RunAsync(
             DetectionBackendServiceFactory.CreateDefault(),
             request, progress, token)
             └─ Task.Run(...)
                 └─ NativeDetectionBackendService.RunDetectionAsync
```

`RunDetectionAsync` 的顺序：

1. 校验 `InputDirectory` 非空且存在；解析输入/输出根目录。
2. 加载 `ConfigPath`；缺失时使用默认值，JSON 无效或阈值倒置时发出 `error` 并返回 1。
3. 枚举 `*.csv`（递归、忽略不可访问目录、跳过 reparse point、按 `OrdinalIgnoreCase` 排序）。
4. 发出 `run_started`。
5. 逐文件分析；每个文件发出 `file_result` 与 `file_progress`。
6. 有可报告内容时构建 `report_summary`（高风险设备 Top 10、问题类型 Top 8、明细预览前 100 条）。
7. `WriteReport=true` 且存在汇总时生成工作簿，发出 `report_written`。
8. 发出 `run_completed`（总数、正常/异常文件、异常记录、跳过文件、报告路径、耗时）。

### 事件契约

| 事件 | 发出时机 | 关键字段 |
| --- | --- | --- |
| `run_started` | 输入/配置校验并完成文件枚举后 | `input_dir`、`output_dir`、`total_files`、`write_report`、`generated_at` |
| `file_result` | 每个文件处理结束 | `source_file`、`relative_path`、`building`、`transformer`、`status`、`message`、`anomaly_count`、`anomaly_types`、`native_parity_trace` |
| `file_progress` | 每个文件处理结束，紧随 `file_result` | `processed_files`、`total_files`、`percent` |
| `report_summary` | 存在异常、传感器状态行或跳过行 | `normal_files`、`anomaly_files`、`anomaly_records`、`skipped_files`、`device_count`、`high_risk_devices`、`top_issue_types`、`sensor_overview`、`detail_preview_count`、`detail_preview` |
| `report_written` | 工作簿成功发布后 | `report_path`、`anomaly_records` |
| `run_completed` | 正常运行结束（无论是否写报告） | 汇总计数、`report_path`、`duration_seconds`、`generated_at` |
| `error` | 输入/配置无效或报告写入失败 | `error_type`、`message`；返回退出码 1，不再发 `run_completed` |

`RunEventService` 把事件映射为 `RunEventAction`：更新总数/已处理数/百分比、当前文件、状态文本、任务栏进度、日志、通知、摘要和最近报告。`stderr` 分支是 UI 兼容路径，原生后端当前不发出该事件。

## 线程、取消与进度

- `DetectionExecutionService.RunAsync` 用 `Task.Run` 把 CPU 密集的同步检测循环移出 UI 线程；`NativeDetectionBackendService.RunDetectionAsync` 本身先 `Task.Yield()`，但主要工作是同步循环。
- `Progress<DetectionBackendEvent>` 在 UI 线程创建，捕获 WinUI 同步上下文；`HandleBackendEvent` 在 UI 线程更新 `ObservableCollection`、属性和任务栏，工作线程不直接改动 UI 集合。
- `CancellationToken` 在文件枚举、编码探测、逐行读取、逐行分析与规则管线编排、冻结检测、明细排序和工作簿行生成中检查；单个大文件也可以被取消。
- `MainViewModel.CancelCommand` 采用两步确认：第一次点击/按 Esc 进入“再次确认取消”，5 秒内第二次才调用 `_runCts.Cancel()`；超时自动恢复运行状态。
- 取消时抛出 `OperationCanceledException`，UI 记录“检测已取消”，任务栏暂停，不再发完成通知。失败时任务栏错误，发出一次失败通知。
- `IsRunning` 同时限制“开始检测”和托盘开始命令；`finally` 中释放 CTS 并恢复命令可用性。

## 报告生成与 OpenXML 工作簿

```text
reportDetails（按严重等级排序的全部明细）
  ├─ BuildOverviewRows        → 检测概览
  ├─ BuildDetailRows          → 异常明细（23 列）
  ├─ BuildDeviceRows          → 设备汇总（按 P1/P2/P3 + 异常数排序）
  ├─ BuildIssueRows           → 异常分类统计（类型/建筑/变压器）
  ├─ BuildSensorRows          → 传感器状态（含跳过行）
  └─ BuildConfigRows          → 检测配置 + 运行信息
        └─ ZipArchive 临时文件
            ├─ [Content_Types].xml
            ├─ _rels/.rels
            ├─ xl/workbook.xml
            ├─ xl/_rels/workbook.xml.rels
            ├─ xl/styles.xml
            └─ xl/worksheets/sheet1.xml … sheet6.xml
                └─ ValidateWorkbookArchive（条目存在 + XML 可解析）
                    └─ File.Move → 电气异常报告_yyyyMMdd_HHmmss.xlsx
```

- 输出文件名使用本地时间 `yyyyMMdd_HHmmss`；同名时追加 `_1` 到 `_99`，仍冲突则追加 GUID。
- 工作簿先写入同目录的 `.{reportName}.{guid}.tmp`，成功校验后 `File.Move` 到最终名；任何异常都会删除临时文件，避免留下半成品 `.xlsx`。
- 工作表首行冻结并启用自动筛选；列宽按内容截断到 42 个字符；表头使用深蓝底白字。
- 明细表按严重等级着色：高/中/低分别使用不同底色；与异常类型对应的测点值使用强调色。
- 单元格以 `inlineStr` 写入；以 `= + - @` 开头的文本会加前导单引号，防止 Excel 公式注入；XML 1.0 不允许的控制字符直接丢弃，合法的代理对保留。
- 零 CSV 文件时不会产生 `report_summary`，也不会写报告；只要处理过文件（包括全部正常），传感器状态行会触发汇总与报告生成。
- 工作簿列定义、工作表名和逐列含义见 [`data-model.md`](data-model.md#报告工作簿)。

## 发布与打包模型

桌面端使用 `dotnet publish` 自包含发布，并由 Inno Setup 生成按用户安装器。关键属性：

- `WindowsPackageType=None`：未打包 WinUI 3 应用。
- `WindowsAppSDKSelfContained=true`：发布包携带 Windows App SDK 运行时。
- `PublishSingleFile=false`、`PublishTrimmed=false`：保留 WinUI 资源和依赖文件。
- Windows CI 验证构建、测试、包健康和安装器；交互式 UI 烟测默认跳过。

详细命令、产物路径和故障处理见 [桌面开发指南](../desktop/README.md)。签名状态以对应 Release 为准；SHA-256 仅验证文件完整性，不能验证发布者身份。

## 架构门禁与已知约束

`Test-DesktopArchitecture.ps1` 检查模块依赖、中央包管理、源文件行数上限及旧构建产物。变更涉及规则、设置迁移或发布流程时应执行相应回归测试。当前限制见 [状态](状态.md)。
