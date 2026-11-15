# E-Detection 数据模型

本文覆盖 CSV 输入、规则管线、检测请求/事件、报告 DTO、设置与密钥、工作簿列、异常/严重等级/传感器模型，以及运行状态。架构与线程模型见 [`architecture.md`](architecture.md)。

## 数据流总览

```text
CSV 文件
  ├─ 编码探测
  ├─ 行/列解析
  ├─ 列名归一化
  └─ 逐行规则检测
        ├─ DetectionRowContext（本行可用测点）
        ├─ DetectionRowRulePipeline.Evaluate（电压 → 电流 → 有功 → 功率因数 → 温度）
        ├─ FreezeRowDetector.Detect（整列冻结/恒定）
        ├─ NativeRowAnalysis（行级中间结果）
        ├─ ReportDetailPreview（可报告异常明细）
        │     └─ ReportDeviceSummary / ReportIssueType（设备与问题汇总）
        └─ NativeSensorStatusRow（文件级传感器状态）
              └─ ReportSensorOverview（传感器总览）
                    └─ DetectionBackendEvent（事件流）
                          └─ .xlsx 六张工作表
```

## CSV 输入

### 文件发现

`NativeDetectionBackendService` 使用同一套枚举策略：

```csharp
new EnumerationOptions
{
    RecurseSubdirectories = true,
    IgnoreInaccessible = true,
    MatchCasing = MatchCasing.CaseInsensitive,
    AttributesToSkip = FileAttributes.ReparsePoint,
}
```

- 只发现扩展名为 `.csv` 的文件，递归子目录，忽略不可访问目录，跳过 reparse point（目录联接/符号链接），避免越出输入树。
- 结果按 `StringComparer.OrdinalIgnoreCase` 排序，保证运行顺序稳定。
- 文件名包含“高压”（不区分大小写）的文件直接跳过，计入 `skipped_files`，并写入传感器状态表的“跳过”行。
- 文件为空（不足 2 行）、读取失败、未匹配关键参数或列名歧义时，文件记为 `skipped`；不会中止整个运行。输入目录本身为空时，`run_started` 的 `total_files` 为 0，随后直接 `run_completed`。

### 编码

静态构造函数注册 `CodePagesEncodingProvider`，因此可以使用代码页 936 和 54936。`ReadCsvLines` 依次尝试严格解码器；任何 `DecoderFallbackException` 都会尝试下一个候选。

| 优先级 | 编码 | 触发条件 |
| --- | --- | --- |
| 1 | UTF-8 BOM | 文件头为 `EF BB BF`。 |
| 2 | UTF-8（无 BOM） | 前 4096 字节可被严格 UTF-8 解码。 |
| 3 | GB18030（54936） | 非 UTF-8 且未识别 BOM 时的首选中文编码。 |
| 4 | GBK（936） | 后续固定候选的第一项。 |
| 5 | UTF-8 BOM | 后续固定候选。 |
| 6 | GB18030（54936） | 后续固定候选；若已作为首选尝试过则跳过。 |
| 7 | UTF-8（无 BOM） | 后续固定候选；若已作为首选尝试过则跳过。 |

候选按代码页去重。`StreamReader` 同时启用 `detectEncodingFromByteOrderMarks: true`。如果所有严格候选都失败，最后一次读取会退回非严格 UTF-8；若列名仍无法识别，文件按“未匹配到关键参数”跳过。

### CSV 行解析

- 分隔符为逗号；支持双引号包裹字段，字段内的 `""` 表示一个字面量引号；每个单元格去除首尾空白。
- 使用 `ReadLine()` 逐行读取，因此不支持字段内跨行换行；SCADA 日报按单行记录处理。
- 时间列取第一个包含“时间”“时刻”“Date”或“Time”的表头；找不到时使用第 0 列仅作行标签，但不会启用统计尾行检测。
- 统计尾行只匹配“整个时间单元格”等于以下标签之一（忽略大小写，允许末尾冒号）：`最大值`、`最小值`、`平均值`、`合计`、`总计`、`Total`、`Max`、`Min`、`Maximum`、`Minimum`、`Average`、`Avg`、`Sum`。只丢弃该行，不截断后续数据。

### 表头归一化

`NormalizeColumnName` 先剥离末尾单位注释，再匹配相别 token 或中文列名：

| 归一化列 | 识别来源（示例） |
| --- | --- |
| `Uab` | `Uab`、`Ua`、`A相电压`、`电压A相` |
| `Ubc` | `Ubc`、`Ub`、`B相电压`、`电压B相` |
| `Uca` | `Uca`、`Uc`、`C相电压`、`电压C相` |
| `Ia` | `Ia`、`A相电流`、`电流A相` |
| `Ib` | `Ib`、`B相电流`、`电流B相` |
| `Ic` | `Ic`、`C相电流`、`电流C相` |
| `有功功率` | 包含“有功” |
| `无功功率` | 包含“无功” |
| `功率因数` | 包含“功率因数”或独立 token `PF` |
| `A相温度` / `B相温度` / `C相温度` | `Ta`/`Tb`/`Tc`、`A相温`/`温度A相` 等 |

规则：

- 末尾的 `(...)`、`[...]`、`（...）`、`【...】` 会先被剥离。`B相电流(A)` 中的 `A` 是单位，不会误判为 A 相。
- Latin token 必须处于词边界。`Uab` 不会被 `Ua` 规则误吞，裸 `A` 也不会作为相别标记。
- 环境温度、油温、柜温不会映射成绕组温度；只有明确带相别的温度列参与温度规则。
- 规则评估列包括 `Uab/Ubc/Uca`、`Ia/Ib/Ic`、`有功功率`、`无功功率`、`功率因数`、`A/B/C相温度`；其他自由文本列保留原样，不参与规则。
- 两个不同的原始表头归一化到同一个规则列时，文件按“列名歧义”跳过。完全相同的重复表头允许存在，组内数值按平均值合并。

### 无效值

以下值视为不可用测量：

- 数值 `-1`（容差 `1e-6`）。
- 数值 `2867.2`（容差 `1e-6`）。
- 空白、缺失、无法解析、`NaN`、`+Infinity`、`-Infinity`。

`TryParseDouble` 要求 `double.IsFinite`。对每个列组，组内可解析值取平均后再判断是否为 `-1` 或 `2867.2`；组内没有任何可解析值时也计为无效观测。

设备离线判定：

```text
无效观测数 / (数据行数 × 关键列组数) > 0.5
  → 设备离线
```

关键列组只包括电压与电流（`Uab/Ubc/Uca`、`Ia/Ib/Ic`）。离线文件会追加一条高等级“设备离线(列名...)”明细；文件异常计数为 `1 + 传感器故障数`。

### 位置与日期

| 字段 | 规则 |
| --- | --- |
| `Building` | 相对路径的第一段；文件直接在输入根目录时使用 `根目录`。 |
| `Transformer` | 文件名中第一个匹配 `(\d+TM[a-zA-Z]*\d*|充电桩)` 的值；没有匹配时使用文件名去掉扩展名。 |
| `DevicePath` | 相对路径去掉文件名后的目录部分；没有目录、绝对路径、包含 `..` 或只有文件名时使用 `根目录`。 |
| `Date` | 文件名中最后一个匹配 `(\d{4}[_\-]?[01]\d[_\-]?[0-3]\d|\d{8})` 的日期；解析为 `yyyy-MM-dd`，否则为 `日期未知`。 |
| `Time` | 每行时间列原值；设备汇总的首次/末次时间会解析 `Date + Time`，解析失败时退回明细顺序。 |

## 检测请求

```csharp
public sealed class DetectionRequest
{
    public required string InputDirectory { get; init; }
    public string? OutputDirectory { get; init; }
    public string? ConfigPath { get; init; }
    public bool WriteReport { get; init; } = true;
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `InputDirectory` | `string` | 是 | CSV 根目录；为空或不存在返回退出码 1 和 `error` 事件。 |
| `OutputDirectory` | `string?` | 否 | 报告目录；为空时使用输入目录。不会自动创建输入目录。 |
| `ConfigPath` | `string?` | 否 | 阈值 JSON 路径；为空时使用内置默认值。 |
| `WriteReport` | `bool` | 否 | 默认 `true`；为 `false` 时仍检测并发出事件，但不写 `.xlsx`。 |

## 运行事件

`DetectionBackendEvent` 是检测核心与 UI 之间的唯一事件契约，JSON 属性名使用 snake_case。

### 上下文与状态字段

| 属性 | JSON 名 | 类型 | 说明 |
| --- | --- | --- | --- |
| `EventName` | `event` | `string` | `run_started`、`file_result`、`file_progress`、`report_summary`、`report_written`、`run_completed`、`error`。 |
| `GeneratedAt` | `generated_at` | `string?` | 本地时间 `yyyy-MM-dd HH:mm:ss`。 |
| `SourceFile` | `source_file` | `string?` | 文件名。 |
| `RelativePath` | `relative_path` | `string?` | 相对于输入根目录的路径。 |
| `Building` | `building` | `string?` | 建筑/第一级目录。 |
| `Transformer` | `transformer` | `string?` | 变压器或文件名主干。 |
| `Status` | `status` | `string?` | `normal`、`anomaly`、`skipped`。 |
| `Message` | `message` | `string?` | 文件结果、错误或日志文本。 |
| `ErrorType` | `error_type` | `string?` | 异常类型名或 `ValueError`。 |
| `InputDirectory` | `input_dir` | `string?` | 规范化输入根目录。 |
| `OutputDirectory` | `output_dir` | `string?` | 规范化输出根目录。 |
| `WriteReport` | `write_report` | `bool?` | 本次是否写报告。 |
| `ReportPath` | `report_path` | `string?` | 成功发布后的 `.xlsx` 路径。 |

### 计数与进度字段

| 属性 | JSON 名 | 类型 | 说明 |
| --- | --- | --- | --- |
| `TotalFiles` | `total_files` | `int?` | 发现的 CSV 总数。 |
| `ProcessedFiles` | `processed_files` | `int?` | 已处理数（含跳过）。 |
| `NormalFiles` | `normal_files` | `int?` | 正常文件数。 |
| `AnomalyFiles` | `anomaly_files` | `int?` | 至少一条可报告异常的文件数。 |
| `AnomalyRecords` | `anomaly_records` | `int?` | 文件级异常计数之和。 |
| `AnomalyCount` | `anomaly_count` | `int?` | 单文件异常计数。 |
| `AnomalyTypes` | `anomaly_types` | `string?` | 单文件异常类型摘要；离线文件为“设备离线”。 |
| `SkippedFiles` | `skipped_files` | `int?` | 高压、空文件、读取失败、列名歧义等跳过数。 |
| `Percent` | `percent` | `double?` | `0.0`–`1.0` 的文件进度。 |
| `DurationSeconds` | `duration_seconds` | `double?` | 运行耗时。 |
| `DeviceCount` | `device_count` | `int?` | 有异常明细的设备数。 |

### 汇总与嵌套字段

| 属性 | JSON 名 | 类型 | 说明 |
| --- | --- | --- | --- |
| `HighRiskDevices` | `high_risk_devices` | `List<ReportDeviceSummary>?` | 最高等级为“高”的设备，最多 10 个。 |
| `TopIssueTypes` | `top_issue_types` | `List<ReportIssueType>?` | 问题类型计数前 8。 |
| `SensorOverview` | `sensor_overview` | `ReportSensorOverview?` | 离线、传感器故障、未配置、跳过统计。 |
| `DetailPreviewCount` | `detail_preview_count` | `int?` | 可报告明细总数（不限于预览 100 条）。 |
| `DetailPreview` | `detail_preview` | `List<ReportDetailPreview>?` | 按严重等级排序后的前 100 条。 |
| `NativeParityTrace` | `native_parity_trace` | `NativeParityTrace?` | 仅诊断模式；为 `null` 时不写入 JSON。 |

### 事件发出条件

| 事件 | 条件 |
| --- | --- |
| `run_started` | 输入/配置校验通过且文件枚举完成后；`total_files=0` 也会发出。 |
| `file_result` | 每个文件处理结束后。 |
| `file_progress` | 每个文件处理结束后，紧随 `file_result`。 |
| `report_summary` | `anomaly_records > 0` 或存在传感器状态行或存在跳过行。 |
| `report_written` | `WriteReport=true` 且存在 `report_summary`，工作簿成功发布后。 |
| `run_completed` | 正常运行结束时；即使 `total_files=0` 或 `WriteReport=false` 也会发出。 |
| `error` | 输入为空/不存在、配置无效、阈值倒置、报告写入失败；返回 1，不再发 `run_completed`。 |

## 诊断追踪：`NativeParityTrace`

`NativeParityTrace` 是可选、隐私保护的结构对等追踪，用来比较两套实现的行级/汇总输出。它只包含计数和 HMAC-SHA-256 十六进制摘要，不包含源路径、文件名、遥测值或规则标签。

| 属性 | JSON 名 | 说明 |
| --- | --- | --- |
| `SchemaVersion` | `schema_version` | 当前为 `1`。 |
| `AnalysisState` | `analysis_state` | `analyzed` 或 `not_analyzed`。 |
| `DataRowCount` | `data_row_count` | 数据行数（跳过空行与统计尾行后）。 |
| `RawRowLabelCount` | `raw_row_label_count` | 原始行级标签总数。 |
| `ReportableRowLabelCount` | `reportable_row_label_count` | 可报告行级标签总数。 |
| `StructuredDetailCount` | `structured_detail_count` | 结构化明细条数。 |
| `RawRowLabelHashes` | `raw_row_label_hashes` | 原始行标签 HMAC 列表。 |
| `ReportableRowLabelHashes` | `reportable_row_label_hashes` | 可报告行标签 HMAC 列表。 |
| `StructuredDetailHashes` | `structured_detail_hashes` | 结构化明细 HMAC 列表。 |
| `FinalAnomalyTypesHash` | `final_anomaly_types_hash` | 最终 `anomaly_types` 的 HMAC。 |

启用方式：

```powershell
$env:EDETECTION_NATIVE_PARITY_TRACE = "<本地随机密钥>"
```

变量值为空、`0`、`false` 或 `off` 时关闭。非空值本身作为 HMAC 密钥；两套实现必须使用同一密钥才能比较摘要。该对象只在设置环境变量时填充，正常发布输出不包含它。

## 报告明细 DTO

### `ReportDetailPreview`

| 属性 | JSON 名 | 类型 | 说明 |
| --- | --- | --- | --- |
| `Building` | `building` | `string?` | 建筑/第一级目录。 |
| `Transformer` | `transformer` | `string?` | 变压器或文件名主干。 |
| `RelativePath` | `relative_path` | `string?` | 相对路径；为空时由外层补入。 |
| `SourceFile` | `source_file` | `string?` | 来源文件名。 |
| `Date` | `date` | `string?` | 从文件名提取的日期。 |
| `Time` | `time` | `string?` | 时间列原文；数字会通过 `FlexibleStringJsonConverter` 转成字符串。 |
| `Severity` | `severity` | `string?` | `高`、`中`、`低`。 |
| `IssueType` | `issue_type` | `string?` | 归一化异常类型。 |
| `IssueDetail` | `issue_detail` | `string?` | 详细描述；可能包含相别、数值或偏差。 |
| `IssueValue` | `issue_value` | `string?` | 关联测点值文本。 |
| `RecommendedAction` | `recommended_action` | `string?` | 建议处置。 |
| `ReportValues` | —（`JsonIgnore`） | `Dictionary<string,double>` | 报告用测点值，不进入事件 JSON。 |

只读计算属性：`LocationText`（建筑/变压器或来源文件）、`TimeText`（日期+时间或来源文件）、`IssueText`（类型·详情）、`ValueText`（空值显示 `-`）。`Matches(query, severity, issueType)` 用于 UI 搜索与筛选；`ToClipboardText()` 生成多行复制文本。

### `ReportDeviceSummary`

| 属性 | JSON 名 | 说明 |
| --- | --- | --- |
| `Building` | `building` | 建筑。 |
| `DevicePath` | `device_path` | 相对目录；根目录为 `根目录`。 |
| `Transformer` | `transformer` | 变压器。 |
| `AnomalyRecords` | `anomaly_records` | 该设备明细条数。 |
| `MainIssueTypes` | `main_issue_types` | 前 5 个问题类型及计数，`；` 分隔。 |
| `HighestSeverity` | `highest_severity` | `高`、`中`、`低`。 |
| `Priority` | `priority` | `P1`（高）、`P2`（中）、`P3`（低）。 |

设备按 `P1 → P2 → P3`、同优先级按异常数降序排列。计算属性 `Title` 为 `建筑 / 变压器`，`Subtitle` 为 `优先级 · 最高等级 · 条数 · 设备路径`。

### `ReportIssueType`

| 属性 | JSON 名 | 说明 |
| --- | --- | --- |
| `Name` | `name` | 异常类型。 |
| `Count` | `count` | 出现次数。 |

问题类型通过 `;`、`；`、`|` 拆分 `IssueType` 后计数；事件取前 8，工作簿写入全部。

### `ReportSensorOverview`

| 属性 | JSON 名 | 说明 |
| --- | --- | --- |
| `TotalRows` | `total_rows` | 传感器状态行数 + 跳过文件数。 |
| `OfflineDevices` | `offline_devices` | 离线状态行数。 |
| `SensorFaultRows` | `sensor_fault_rows` | 含传感器故障的状态行数。 |
| `SensorMissingRows` | `sensor_missing_rows` | 含未配置传感器的状态行数。 |
| `SkippedRows` | `skipped_rows` | 跳过文件数。 |

### 报告文本与历史 DTO

- `ReportSummaryContext` 是 `ReportSummaryComposer` 的无状态输入快照，包含输入目录、明细、高风险设备、Top 问题、文件/异常/跳过计数、设备数、传感器计数、耗时和报告路径。
- `RecentReport` 保存报告历史：`Path`、`CreatedAt`、`CompletedAt`、输入/输出目录、文件/异常/跳过计数、耗时、设备数、高风险设备、Top 问题、传感器总览、明细预览、`DetailPreviewCount`；`IsLatest` 与文件名/状态文本为计算属性。
- `ReportHistoryService` 按路径去重、插入到最前、按最近报告上限裁剪，并重新标记 `IsLatest`。

## 设置模型

### `AppSettings`

默认路径：`%LOCALAPPDATA%\E-Detection\Desktop\settings.json`。可用环境变量 `EDETECTION_DESKTOP_SETTINGS_DIR` 覆盖目录。设置通过临时文件 + `File.Replace`/`File.Move` 原子写入；当前 `SettingsVersion = 10`。

| 分组 | 字段 |
| --- | --- |
| 运行 | `InputDirectory`、`OutputDirectory`、`ConfigPath`、`WriteReport` |
| 窗口/托盘 | `CloseToTrayOnClose`、`StartMinimizedToTray`、`AutoStartOnSignIn`、`WindowLeft`、`WindowTop`、`WindowWidth`、`WindowHeight`、`IsWindowMaximized` |
| 通知 | `EnableDesktopNotifications` |
| LLM | `EnableLlmAssistant`、`LlmEndpoint`、`LlmModel`、`UseProxyForLlm` |
| ntfy | `EnableNtfyNotifications`、`NtfyServerUrl`、`NtfyTopic`、`SelectedNtfyPriorityIndex`（0–4）、`UseProxyForNotifications` |
| 代理 | `EnableNetworkProxy`、`ProxyAddress`、`ProxyRequiresAuthentication`、`ProxyUserName` |
| 更新 | `EnableUpdateChecks`、`UseProxyForUpdates`、`SelectedUpdateChannelIndex`（0–1）、`UpdateFeedUrl` |
| 日志/历史 | `SelectedLogRetentionIndex`（0–3）、`SelectedRecentReportLimitIndex`、`RecentReports` |
| 外观 | `SelectedThemeIndex`（0–2）、`SelectedBackdropIndex`（0–2） |
| 诗词状态 | `EnablePoetryStatus`、`PoetryServiceUrl`、`SelectedPoetryLanguageIndex`（0–1） |

设置 JSON 仅保存非敏感值和 UI 选项索引；LLM API Key、ntfy Token、代理密码不在其中。

```json
{
  "SettingsVersion": 10,
  "InputDirectory": "D:\\data\\2026-11-16",
  "OutputDirectory": "D:\\reports",
  "WriteReport": true,
  "EnableDesktopNotifications": true,
  "NtfyServerUrl": "https://ntfy.sh",
  "UpdateFeedUrl": "https://github.com/osGex0o0II/E-Detection/releases/latest",
  "SelectedThemeIndex": 0,
  "EnablePoetryStatus": false
}
```

### 密钥：Windows Credential Locker

`SecureCredentialService` 使用 `Windows.Security.Credentials.PasswordVault`，用户名固定为 `default`，资源名如下：

| 敏感项 | 资源名 | 读取/保存 |
| --- | --- | --- |
| LLM API Key | `E-Detection.Desktop.LLM.ApiKey` | `GetLlmApiKey` / `SaveLlmApiKey` / `ClearLlmApiKey` |
| ntfy Token | `E-Detection.Desktop.Ntfy.Token` | `GetNtfyToken` / `SaveNtfyToken` / `ClearNtfyToken` |
| 代理密码 | `E-Detection.Desktop.Proxy.Password` | `GetProxyPassword` / `SaveProxyPassword` / `ClearProxyPassword` |

保存空字符串会被忽略；读取失败返回空字符串；UI 只显示“已保存到 Windows 凭据”或“未保存”。密钥不会写入 `settings.json`、日志或诊断快照。

### 检测阈值：`DetectionConfigSettings`

默认路径：`%LOCALAPPDATA%\E-Detection\Desktop\detection-config.json`。首次启动时，如果用户配置不存在，会从发布目录的 `config.json` 复制；如果 `ConfigPath` 指向捆绑的 `config.json`，会重定向到用户配置路径。

| 属性 | 配置键 | 默认值 | 归一化范围/说明 |
| --- | --- | --- | --- |
| `VoltageMinThreshold` | `V_MIN_THRESHOLD` | `353.0` | 0–1000；不得高于上限。 |
| `VoltageMaxThreshold` | `V_MAX_THRESHOLD` | `430.0` | 0–1000；不得低于下限。 |
| `VoltageImbalanceThreshold` | `V_IMBALANCE_THRESHOLD` | `0.02` | 0.001–1。 |
| `CurrentMaxThreshold` | `I_MAX_THRESHOLD` | `1000.0` | 0–100000。 |
| `CurrentUnbalanceMaxThreshold` | `I_UNBALANCE_MAX_THRESHOLD` | `0.15` | 0–1。 |
| `CurrentActiveMinThreshold` | `I_MIN_ACTIVE_THRESHOLD` | `1.0` | 0.001–100000。 |
| `ActivePowerMinThreshold` | `P_ACTIVE_MIN_THRESHOLD` | `0.0` | 有限数值。 |
| `PowerFactorMinThreshold` | `PF_MIN_THRESHOLD` | `0.90` | 0–1。 |
| `TemperatureMinThreshold` | `T_MIN_THRESHOLD` | `0.0` | -50–200；不得高于上限。 |
| `TemperatureMaxThreshold` | `T_MAX_THRESHOLD` | `70.0` | -50–200；不得低于下限。 |
| `FreezeCountThreshold` | `FREEZE_COUNT_THRESHOLD` | `3` | 1–1000；保存时四舍五入为整数。 |
| `FreezeStdThreshold` | `FREEZE_STD_THRESHOLD` | `0.01` | 0–1。 |
| `CurrentOverloadEnabled` | `current_overload` | `true` | 电流过载规则开关。 |
| `CurrentUnbalanceEnabled` | `current_unbalance` | `false` | 电流不平衡规则开关。 |
| `PowerFactorEnabled` | `power_factor` | `false` | 功率因数规则开关。 |
| `DetailOutputEnabled` | `detail_output` | `false` | 详细异常文本开关。 |

兼容旧键 `current`：如果它是布尔值，会同时设置 `CurrentOverloadEnabled` 和 `CurrentUnbalanceEnabled`。非有限数值、无法解析的值或缺失键回退到默认值；下限高于上限时 `error`，不静默交换。旧版本迁移（`<6`、`<8`）会补全 ntfy、更新源和诗词默认值，但不会主动开启诗词状态。

## 报告工作簿

生成器直接写 OpenXML 包（ZIP + XML），不依赖第三方 Excel 库。工作表名称固定：

| 序号 | 工作表 | 用途 |
| --- | --- | --- |
| 1 | `检测概览` | 运行指标与高风险设备 Top 10。 |
| 2 | `异常明细` | 全部可报告异常及测点值。 |
| 3 | `设备汇总` | 按建筑/设备路径/变压器聚合。 |
| 4 | `异常分类统计` | 按问题类型、建筑、变压器计数。 |
| 5 | `传感器状态` | 每个文件的离线、故障、未配置、跳过状态。 |
| 6 | `检测配置` | 实际阈值、规则开关与运行信息。 |

### `检测概览`

两列表头 `指标` / `值`，依次写入：

`输入目录`、`输出目录`、`文件总数`、`已处理文件`、`正常文件`、`异常文件`、`异常记录`、`跳过文件`、`生成时间`，随后空行，再写 `高风险设备 Top 10` / `异常记录数`，每个设备一行为 `建筑 / 变压器 (设备路径)` 与异常记录数。

### `异常明细`

固定 23 列：

| 序号 | 列 | 来源 |
| --- | --- | --- |
| 1 | `建筑` | `Building` |
| 2 | `相对路径` | `RelativePath` |
| 3 | `变压器` | `Transformer` |
| 4 | `来源文件` | `SourceFile` |
| 5 | `日期` | `Date` |
| 6 | `时间` | `Time` |
| 7 | `严重等级` | `Severity` |
| 8 | `异常类型` | `IssueType` |
| 9 | `异常详情` | `IssueDetail` |
| 10 | `异常值` | `IssueValue` |
| 11 | `Uab` | `ReportValues["Uab"]` |
| 12 | `Ubc` | `ReportValues["Ubc"]` |
| 13 | `Uca` | `ReportValues["Uca"]` |
| 14 | `Ia` | `ReportValues["Ia"]` |
| 15 | `Ib` | `ReportValues["Ib"]` |
| 16 | `Ic` | `ReportValues["Ic"]` |
| 17 | `有功功率` | `ReportValues["有功功率"]` |
| 18 | `无功功率` | `ReportValues["无功功率"]` |
| 19 | `功率因数` | `ReportValues["功率因数"]` |
| 20 | `A相温度` | `ReportValues["A相温度"]` |
| 21 | `B相温度` | `ReportValues["B相温度"]` |
| 22 | `C相温度` | `ReportValues["C相温度"]` |
| 23 | `建议处置` | `RecommendedAction` |

行按严重等级排序（高→中→低）。高/中/低分别使用不同底色；与异常类型相关的测点值使用强调色。

### `设备汇总`

列：`建筑`、`设备路径`、`变压器`、`异常记录数`、`主要异常类型`、`首次异常时间`、`末次异常时间`、`最高严重等级`、`建议优先级`。首次/末次时间按 `Date + Time` 解析后取最早/最晚，解析失败时退回明细顺序。

### `异常分类统计`

列：`统计维度`、`名称`、`数量`。依次写入：

1. `异常类型` 的完整问题类型计数。
2. `建筑` 的明细条数。
3. `变压器` 的明细条数。

### `传感器状态`

列：`来源文件`、`建筑`、`相对路径`、`变压器`、`是否离线`、`传感器故障`、`传感器未配置`、`状态`、`原因`。

- `是否离线` 写 `是`/`否`。
- 故障与未配置列表用 `；` 连接。
- `状态` 为 `正常`、`异常`、`离线`、`跳过`。
- `原因` 由离线比例、故障传感器计数和未配置列名组成。

### `检测配置`

两列 `配置项` / `值`，依次写入：

`backend_mode=native`、`report_backend=NativeDetectionBackendService`、`V_MIN_THRESHOLD`、`V_MAX_THRESHOLD`、`V_IMBALANCE_THRESHOLD`、`I_MAX_THRESHOLD`、`I_UNBALANCE_MAX_THRESHOLD`、`I_MIN_ACTIVE_THRESHOLD`、`P_ACTIVE_MIN_THRESHOLD`、`PF_MIN_THRESHOLD`、`T_MIN_THRESHOLD`、`T_MAX_THRESHOLD`、`current_overload`、`current_unbalance`、`power_factor`、`detail_output`、`FREEZE_COUNT_THRESHOLD`、`FREEZE_STD_THRESHOLD`；空行后写运行信息 `input_dir`、`output_dir`、`total_files`、`processed_files`、`normal_files`、`anomaly_files`、`anomaly_records`、`skipped_files`、`generated_at`。

### 发布安全

- 临时文件名：`.{reportName}.{guid}.tmp`；成功校验后 `File.Move` 到最终名。
- 校验：11 个必需 ZIP 条目存在，且每个 XML 条目可被 `XDocument.Load` 解析。
- 失败时删除临时文件；不会留下最终名半成品。
- 文本单元格做 XML 转义；以 `= + - @` 开头的文本加前导单引号；XML 1.0 非法控制字符丢弃，合法代理对保留。
- 首行冻结、自动筛选、列宽按内容截断到 42。

## 规则管线数据模型

行级规则在 `EDetection.NativeCore/Detection/` 内完成，输入是后端解析后的行上下文，输出是异常类型字符串列表；`NativeDetectionBackendService` 只负责把结果转为 `NativeRowAnalysis`、`ReportDetailPreview` 和报告行。

### `DetectionRowContext`

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `VoltageValues` | `IReadOnlyDictionary<string,double>` | 键为 `Uab` / `Ubc` / `Uca`。 |
| `CurrentValues` | `IReadOnlyDictionary<string,double>` | 键为 `Ia` / `Ib` / `Ic`。 |
| `ActivePowerValues` | `IReadOnlyDictionary<string,double>` | 键为 `有功功率`。 |
| `PowerFactorValues` | `IReadOnlyDictionary<string,double>` | 键为 `功率因数`。 |
| `TemperatureValues` | `IReadOnlyDictionary<string,double>` | 键为 `A相温度` / `B相温度` / `C相温度`。 |
| `VoltageColumnCount` | `int` | 文件发现的电压列组数，含空单元格；判断全零行时要求 `VoltageValues.Count == VoltageColumnCount`。 |

- 字典只包含本行存在、可解析，且未被 `-1` / `2867.2` 无效值规则剔除的测点；重复表头先按列组取平均。
- 缺失相不会进入字典，因此不会被误判为全零行；全零行要求至少 2 个电压列，且所有已发现电压列都 `< 1.0`。
- 行规则彼此独立；只有冻结/恒定检测读取整列序列。

### `DetectionRuleOptions`

`DetectionRuleOptions` 是运行期记录，不参与 JSON 序列化。`NativeDetectionBackendService.RuleOptions.cs` 的 `ToRuleOptions` 把已校验的 `NativeDetectionConfig` 逐字段映射到它；管线本身不重新校验或交换上下限。

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `VoltageMinThreshold` | `353.0` | 相电压过低阈值。 |
| `VoltageMaxThreshold` | `430.0` | 相电压过高阈值。 |
| `VoltageImbalanceThreshold` | `0.02` | 相电压偏离均值的比例阈值。 |
| `CurrentMaxThreshold` | `1000.0` | 相电流过载阈值。 |
| `CurrentUnbalanceMaxThreshold` | `0.15` | 电流不平衡比例阈值。 |
| `CurrentActiveMinThreshold` | `1.0` | 判定“电流活跃”的最小相电流。 |
| `ActivePowerMinThreshold` | `0.0` | 有功功率下限。 |
| `PowerFactorMinThreshold` | `0.90` | 功率因数下限。 |
| `TemperatureMinThreshold` | `0.0` | 相温度下限。 |
| `TemperatureMaxThreshold` | `70.0` | 相温度上限。 |
| `CurrentOverloadEnabled` | `true` | 电流过载规则开关。 |
| `CurrentUnbalanceEnabled` | `false` | 电流不平衡规则开关。 |
| `PowerFactorEnabled` | `false` | 功率因数规则开关。 |
| `DetailOutputEnabled` | `false` | 输出相别/数值详情；关闭时使用通用标签。 |
| `FreezeCountThreshold` | `3` | 连续平坦点数量。 |
| `FreezeStdThreshold` | `0.01` | 相邻点差值小于该值视为平坦。 |

### 规则契约与输出字符串

`IDetectionRowRule.Evaluate(context, options, issueTypes)` 只追加异常类型字符串。`DetectionRowRulePipeline.Default` 固定按 **电压 → 电流 → 有功功率 → 功率因数 → 温度** 执行；构造器可注入自定义规则，但生产入口始终使用 `Default`。

| 规则 | 触发摘要 | `detail_output=true` | `detail_output=false` |
| --- | --- | --- | --- |
| 全零行 | 至少 2 个电压列，所有已发现电压列 `< 1.0` | 不产生电压类异常 | 同左。 |
| PT 接线异常 | 三相电压齐全、均值 `> 30`、中位数非 0；某相偏离中位数 `> 30%`，其余相最大偏离 `< 15%` | `A相电压疑似PT接线异常(偏差xx.x%)` | `疑似PT接线异常`。 |
| 相电压不平衡 | 至少 2 个电压值、均值 `> 30`、非全零、无 PT 异常；某相偏离均值 `> V_IMBALANCE_THRESHOLD` | `A相电压电压不平衡:380.0:偏差0.053` | `相电压不平衡`（整行一条）。 |
| 电压过低/过高 | 非全零行，任一相电压超出上下限 | `A相电压过低` / `A相电压过高`（逐相） | `电压异常`（每个超限值一条）。 |
| 电流过大 | `CurrentOverloadEnabled` 且任一相电流 `> I_MAX_THRESHOLD` | `A相电流过大`（逐相） | `电流过大`（每个超限相一条）。 |
| 电流不平衡 | `CurrentUnbalanceEnabled`、至少 2 个电流值且均值非 0；`(max-min)/mean > I_UNBALANCE_MAX_THRESHOLD` | `电流不平衡` | 同左。 |
| CT 极性异常 | 有功功率存在、电流活跃、有功功率 `< 0` | `CT极性异常` | 同左。 |
| 有功功率异常 | 有功功率存在、电流活跃、有功功率 `< P_ACTIVE_MIN_THRESHOLD` 且非负 | `有功功率异常` | 同左。 |
| 功率因数过低 | `PowerFactorEnabled`、电流活跃、功率因数 `< PF_MIN_THRESHOLD` | `功率因数过低` | 同左。 |
| 温度异常 | 相温度 `< T_MIN_THRESHOLD` 或 `> T_MAX_THRESHOLD` | `A相温度过高` / `A相温度过低`（逐相） | `温度异常`（每个超限相一条）。 |
| 数据冻结 | 核心列中至少 2 列连续平坦，且不是待机行 | `数据冻结` | 同左。 |
| 数据恒定 | 无功功率或功率因数列出现连续平坦段 | `数据恒定` | 同左。 |

“电流活跃”定义为任一相电流 `>= CurrentActiveMinThreshold`。待机行要求至少存在一个电流列，且所有存在的电流列 `< CurrentActiveMinThreshold`，同时所有存在的电压列都在 `[VoltageMinThreshold, VoltageMaxThreshold]` 内；无电流列的行不按待机行处理。

### `FreezeDetectionRow` 与 `FreezeRowDetector`

| 成员 | 说明 |
| --- | --- |
| `FreezeDetectionRow.CoreValues` | 冻结检测的核心测点：`Uab` / `Ubc` / `Uca` / `Ia` / `Ib` / `Ic` / `有功功率`。 |
| `FreezeDetectionRow.AuxValues` | 恒定检测的辅助测点：`无功功率` / `功率因数`。 |
| `FreezeRowDetector.Detect(rows, options, cancellationToken)` | 返回与输入行等长的 `IReadOnlyList<IReadOnlyList<string>>`；每行只包含该行命中的 `数据冻结` / `数据恒定`。 |

- 辅助列先检查：全零列跳过；否则对连续差 `< FreezeStdThreshold` 的平坦段（长度达到 `FreezeCountThreshold`）逐行标记 `数据恒定`。
- 核心列再检查：至少 2 个核心列同时处于平坦段，且该行不是待机行，才追加 `数据冻结`。
- 当前样本或前一样本缺失时按平坦处理，保持既有 pandas `fillna(0)` 语义；全零列不会触发冻结。
- `Detect` 在辅助列、核心列、滑窗和输出行检查 `CancellationToken`；空输入返回空列表。

### 进入报告输出

```text
DetectionRowRulePipeline.Evaluate → rowTypes
  └─ FreezeRowDetector.Detect 追加冻结/恒定
      ├─ 仅冻结/恒定类型 → 整行跳过，不生成 ReportDetailPreview
      └─ OrderBy(IssueRuleOrder)
          ├─ detail_output=false：紧凑类型/详情/异常值 → 1 条 ReportDetailPreview
          └─ detail_output=true：按类型展开多条 ReportDetailPreview
                └─ ReportValues（同一行测点）→ 异常明细 23 列
```

- `IssueRuleOrder` 只影响结构化明细与摘要顺序：电压/PT → 电流过大 → 电流不平衡 → 冻结/恒定 → CT/有功 → 功率因数 → 温度。
- `ReportValues` 由同一行的电压、电流、有功、功率因数、温度和 `无功功率` 构建，供明细行、`ReportDetailPreview` 和工作簿使用。
- 设备离线与传感器故障在行规则之后统一判定：离线文件追加高等级“设备离线(列名...)”，异常计数为 `1 + 传感器故障数`。

## 异常、严重等级与传感器模型

### 规则

下表是规则行为契约，由 `Detection/` 的 `DetectionRowRulePipeline` 与 `FreezeRowDetector` 实现；字符串和固定顺序见[规则管线数据模型](#规则管线数据模型)。

| 规则族 | 触发条件 | 典型类型 |
| --- | --- | --- |
| 电压过低/过高 | 非全零行，任一相电压 `< V_MIN_THRESHOLD` 或 `> V_MAX_THRESHOLD` | `A相电压过低`、`电压异常`（关闭详细输出时）。 |
| 全零行 | 至少 2 个电压列，所有电压值 `< 1.0` | 不触发电压低/高与不平衡。 |
| PT 接线异常 | 三相电压齐全、均值 `> 30`、非全零、中位数非 0；某相偏离中位数 `> 30%`，其余相最大偏离 `< 15%` | `A相电压疑似PT接线异常(偏差xx.x%)`；关闭详细输出时为“疑似PT接线异常”。 |
| 相电压不平衡 | 至少 2 个电压值、均值 `> 30`、非全零、无 PT 异常；某相偏离均值 `> V_IMBALANCE_THRESHOLD` | `A相电压电压不平衡:...`；关闭详细输出时为“相电压不平衡”。 |
| 电流过大 | `CurrentOverloadEnabled` 且任一相电流 `> I_MAX_THRESHOLD` | `A相电流过大`；关闭详细输出时为“电流过大”。 |
| 电流不平衡 | `CurrentUnbalanceEnabled`、至少 2 个电流值且均值非 0；`(max-min)/mean > I_UNBALANCE_MAX_THRESHOLD` | `电流不平衡`。 |
| CT 极性异常 | 有功功率存在、电流活跃、有功功率 `< 0` | `CT极性异常`。 |
| 有功功率异常 | 有功功率存在、电流活跃、有功功率 `< P_ACTIVE_MIN_THRESHOLD` 且非负 | `有功功率异常`。 |
| 功率因数过低 | `PowerFactorEnabled`、电流活跃、功率因数 `< PF_MIN_THRESHOLD` | `功率因数过低`。 |
| 温度异常 | 相温度 `< T_MIN_THRESHOLD` 或 `> T_MAX_THRESHOLD` | `A相温度过高`；关闭详细输出时为“温度异常”。 |
| 数据冻结 | 核心列（U/I/P）中至少 2 列连续 `FREEZE_COUNT_THRESHOLD` 个点变化 `< FREEZE_STD_THRESHOLD`，且不是待机行 | `数据冻结`。 |
| 数据恒定 | 无功功率或功率因数列出现连续平坦段 | `数据恒定`。 |
| 设备离线 | 关键列无效观测比例 `> 0.5` | `设备离线(Uab,...)`。 |
| 传感器故障 | 相温度平均值为 `2867.2` | `传感器故障(A相温度:2867.2°C,共N点)`。 |
| 传感器未配置 | 列存在且有观测单元格，但没有任何非零有效值 | 传感器状态“未配置”；不单独生成异常明细。 |

“电流活跃”定义为任一相电流 `>= I_MIN_ACTIVE_THRESHOLD`（默认 1.0）。待机行要求至少存在一个电流列，且所有存在的电流列 `< I_MIN_ACTIVE_THRESHOLD`，同时所有存在的电压列均在 `[V_MIN_THRESHOLD, V_MAX_THRESHOLD]` 内；待机行不判定数据冻结。

### 严重等级与优先级

| 条件 | 严重等级 | 设备优先级 |
| --- | --- | --- |
| 类型包含 `设备离线`、`PT接线异常`、`CT`、`CT极性异常` | 高 | `P1` |
| 冻结/恒定/缺失类（`数据冻结`、`数据恒定`、`传感器缺失`、`恒定`、`缺失`） | 低 | `P3` |
| 其他（电压、电流、功率、功率因数、温度、传感器故障等） | 中 | `P2` |

设备最高等级取该设备所有明细的最高值；`P1` 优先展示。明细按高→中→低排序，`report_summary` 的高风险设备只取最高等级为“高”的前 10 个。

### 传感器状态

每个处理过的文件产生一行 `NativeSensorStatusRow`：

| 字段 | 说明 |
| --- | --- |
| `SourceFile` / `RelativePath` / `Building` / `Transformer` | 文件与位置。 |
| `IsOffline` | 关键列无效观测比例 `> 0.5`。 |
| `SensorFaults` | 相温度平均值为 `2867.2` 的列名列表。 |
| `SensorMissing` | 有观测但无非零有效值的列名列表。 |
| `Status` | 离线 → `离线`；有故障/未配置 → `异常`；否则 `正常`；跳过文件 → `跳过`。 |
| `Reason` | 离线比例、故障计数、未配置列名的中文说明。 |

## 运行状态与生命周期

| 状态 | 触发 | 表现 |
| --- | --- | --- |
| 空闲 | 启动/复位 | `IsRunning=false`，进度 0，可开始。 |
| 运行中 | `run_started` / `file_progress` | 状态文本、当前文件、进度、任务栏进度、遥测更新。 |
| 完成 | `run_completed` | 进度 100，摘要、日志、通知、最近报告。 |
| 失败 | `error` 或退出码非 0/未处理异常 | 状态“检测失败”，错误日志与通知，任务栏错误。 |
| 取消 | 二次确认后 `CancellationToken` | 状态“检测已取消”，任务栏暂停，不再发完成通知。 |

`RunStateService` 生成运行汇总/报告摘要快照；`RunTelemetryService` 维护耗时、剩余时间、当前文件和处理速率；`ReportHistoryService` 在 `report_written` 或汇总时写入最近报告。运行事件与 UI 动作的映射由 `RunEventService` 统一处理，避免视图模型重复解析字符串事件。
