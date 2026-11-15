<p align="center">
  <img src="docs/images/e-detection-logo.svg" alt="E-Detection 品牌标志" width="128" />
</p>

<h1 align="center">E-Detection</h1>

<p align="center">
  <em>面向电力监控与 SCADA CSV 日报的 Windows 原生异常检测工作台。</em>
</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/C%23-.NET%2011-512BD4?logo=dotnet&logoColor=white" alt="C# / .NET 11" /></a>
  <a href="https://learn.microsoft.com/windows/apps/winui/winui3/"><img src="https://img.shields.io/badge/UI-WinUI%203-0078D4?logo=windows&logoColor=white" alt="WinUI 3" /></a>
  <a href="https://www.microsoft.com/windows/"><img src="https://img.shields.io/badge/Windows-10%2F11%20x64-0078D4?logo=windows&logoColor=white" alt="Windows 10/11 x64" /></a>
  <a href="https://jrsoftware.org/isinfo.php"><img src="https://img.shields.io/badge/Installer-Inno%20Setup%207-3B6EA5" alt="Inno Setup 7" /></a>
</p>

<p align="center">
  <a href="https://github.com/osGex0o0II/E-Detection/releases/latest">最新版本</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="#验证与发布">验证与发布</a> ·
  <a href="docs/architecture.md">架构</a> ·
  <a href="docs/data-model.md">数据模型</a> ·
  <a href="docs/状态.md">状态</a> ·
  <a href="docs/交接.md">交接</a>
</p>

---

## 从 CSV 到可追溯的检测报告

E-Detection 在本机进程内完成 CSV 解析、规则检测、结果汇总和 Excel 报告生成。发布物使用 **C# + .NET 11 + WinUI 3**，不携带 Python 运行时、wheelhouse 或进程桥接层。

| 输入 | 处理 | 输出 |
| --- | --- | --- |
| 递归发现 CSV；识别 UTF-8 BOM、UTF-8、GB18030 与 GBK | 归一化电压、电流、功率、功率因数、温度等列名，逐行执行规则检测 | 检测概览、异常明细、设备汇总、异常分类统计、传感器状态、检测配置 6 张工作表 |
| 目录结构、文件名与时间列 | 提取建筑、变压器、日期与设备路径；统计离线、冻结、传感器和 CT/PT 异常 | 带严重等级、建议处置和原始测点值的 `.xlsx` 报告 |
| 运行事件流 | `run_started` → `file_result` / `file_progress` → `report_summary` → `report_written` → `run_completed` | 工作台摘要、任务栏进度、桌面通知与报告历史 |

## 核心能力

| 能力 | 实现 |
| --- | --- |
| 原生检测核心 | `EDetection.NativeCore` 不依赖 WinUI 或 Windows App SDK，可在无交互 CI Runner 中直接运行。 |
| CSV 兼容 | 递归发现 `*.csv`，跳过 reparse point，按严格解码候选依次尝试 UTF-8 BOM、UTF-8、GB18030、GBK。 |
| 规则覆盖 | 电压过低/过高、PT 接线异常、相电压不平衡、电流过大/不平衡、CT 极性、有功功率、功率因数、温度、数据冻结/恒定、设备离线、传感器故障或未配置。 |
| 可追溯报告 | 报告包含输入/输出目录、生成时间、文件统计、高风险设备、全部异常明细、设备时间范围、问题分类和检测配置。 |
| 桌面集成 | 单实例、托盘、关闭到托盘、登录启动、任务栏进度、桌面通知、报告历史、设置迁移和本地诊断。 |
| 发布安全 | 包健康检查拒绝旧 Python 资产；报告先写临时文件并校验 XML，再原子改名；`v2.0.2` 发布物明确未签名。 |

## 工作流

```text
CSV 根目录
  └─ 递归发现（忽略不可访问目录，跳过 reparse point）
      └─ 编码探测（BOM / UTF-8 / GB18030 / GBK）
          └─ 列名归一化（Uab/Ubc/Uca、Ia/Ib/Ic、P/Q/PF、A/B/C 相温度）
              └─ 逐行规则检测
                  ├─ file_result   （状态、异常数、异常类型、位置）
                  └─ file_progress （已处理 / 总数 / 百分比）
                      └─ 文件与设备汇总
                          └─ report_summary
                              └─ .xlsx：临时文件 → XML 校验 → 原子改名
                                  └─ report_written
                                      └─ run_completed
                                          └─ 工作台摘要 / 通知 / 报告历史
```

## 实际软件截图

<p align="center">
  <img src="docs/images/e-detection-workbench.png" alt="E-Detection 检测工作台：输入配置、风险指标、异常明细与报告历史" width="100%" />
</p>

<p align="center"><em>检测工作台：输入配置、风险指标、异常明细与报告历史</em></p>

<p align="center">
  <img src="docs/images/e-detection-settings.png" alt="E-Detection 设置页：SettingsCard 行、分组导航与响应式布局" width="100%" />
</p>

<p align="center"><em>设置页：SettingsCard 行、分组导航与响应式布局</em></p>

工作台把输入目录、报告开关、运行前检查、风险摘要、异常明细和报告历史放在同一窗口中；操作员不需要在外部脚本与多个工具之间切换。

## 项目结构

```text
.
├── README.md                              # 项目入口与快速开始
├── config.json                            # 默认检测阈值与规则开关
├── global.json                            # .NET SDK 11.0.100；xUnit v3 使用 Microsoft.Testing.Platform
├── Directory.Build.props                  # 统一语言版本、可空性与代码分析
├── Directory.Packages.props               # 中央包版本管理
├── desktop/
│   ├── EDetection.Domain/                 # 纯领域标记程序集，无 UI / IO 依赖
│   ├── EDetection.NativeCore/             # 原生 CSV 检测、规则、事件与 OpenXML 报告
│   ├── EDetection.Infrastructure/         # IDetectionBackendService 的 DI 适配
│   ├── EDetection.Desktop.Core/           # WinUI-free 应用服务、模型与用例编排
│   ├── EDetection.Desktop/                # WinUI 3 应用壳、视图、视图模型与系统集成
│   ├── EDetection.Tests/                  # xUnit v3 组合根、架构与规则管线测试
│   ├── EDetection.Desktop.Tests/          # net11.0 无界面原生核心冒烟与回归测试
│   ├── installer/                         # Inno Setup 安装器定义
│   └── scripts/                           # 构建、发布、包健康、安装与桌面烟测脚本
├── docs/
│   ├── images/e-detection-workbench.png   # 真实工作台截图
│   ├── images/e-detection-settings.png    # 真实设置页截图
│   ├── architecture.md                    # 分层架构、依赖、运行与发布流程
│   ├── data-model.md                      # CSV、DTO、设置、报告与规则数据模型
│   ├── 状态.md                            # 当前验证状态与已知限制
│   └── 交接.md                            # 交接快照、不变量与常见问题
├── .github/workflows/desktop.yml          # Windows CI、发布与安装器验证
└── artifacts/                             # 本地发布产物，不纳入版本库
```

## 文件说明

| 路径 | 作用 |
| --- | --- |
| [`desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Detection.cs`](desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Detection.cs) | 运行入口、文件枚举、事件发出、汇总与报告调用。 |
| [`desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Analysis.cs`](desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Analysis.cs) | 单文件解析、组装 `DetectionRowContext`、调用规则管线、离线/传感器判定。 |
| [`desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Rules.cs`](desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Rules.cs) | 离线无效观测计数与传感器状态辅助；电压/电流/功率/温度/冻结规则已移入 `Detection/`。 |
| [`desktop/EDetection.NativeCore/Detection/`](desktop/EDetection.NativeCore/Detection/DetectionRowRulePipeline.cs) | 行级规则管线：`DetectionRowContext`、`DetectionRuleOptions`、`IDetectionRowRule`、固定顺序 `DetectionRowRulePipeline` 与 `FreezeRowDetector`。 |
| [`desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.RuleOptions.cs`](desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.RuleOptions.cs) | `NativeDetectionConfig` → `DetectionRuleOptions` 映射，保持配置加载与纯规则管线解耦。 |
| [`desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Csv.cs`](desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Csv.cs) | 编码候选、CSV 分词、列名归一化、无效值与统计尾行处理。 |
| [`desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Reporting.cs`](desktop/EDetection.NativeCore/Services/NativeDetectionBackendService.Reporting.cs) | 6 张工作表的 OpenXML 生成、XML 校验与原子发布。 |
| [`desktop/EDetection.NativeCore/Models/DetectionRequest.cs`](desktop/EDetection.NativeCore/Models/DetectionRequest.cs) | 检测请求契约。 |
| [`desktop/EDetection.NativeCore/Models/DetectionBackendEvent.cs`](desktop/EDetection.NativeCore/Models/DetectionBackendEvent.cs) | 运行事件与报告摘要契约。 |
| [`desktop/EDetection.Desktop.Core/Models/AppSettings.cs`](desktop/EDetection.Desktop.Core/Models/AppSettings.cs) | 用户设置模型；设置版本当前为 10。 |
| [`desktop/EDetection.Desktop.Core/Services/SettingsService.cs`](desktop/EDetection.Desktop.Core/Services/SettingsService.cs) | `settings.json` 原子读写与迁移。 |
| [`desktop/EDetection.Desktop.Core/Services/ReportSummaryComposer.cs`](desktop/EDetection.Desktop.Core/Services/ReportSummaryComposer.cs) | 生成可复制的检测汇报文本。 |
| [`desktop/EDetection.Desktop/ViewModels/MainViewModel.RunCommands.cs`](desktop/EDetection.Desktop/ViewModels/MainViewModel.RunCommands.cs) | 开始/取消、进度事件、摘要、通知与报告历史编排。 |
| [`desktop/EDetection.Desktop/Views/SettingsView.xaml`](desktop/EDetection.Desktop/Views/SettingsView.xaml) | 设置页布局；17 个 `SettingsCard`、17 处 `{x:Bind}`、`SettingsLayoutStates` 三态 VSM；响应式/搜索/导航/动作拆为多个 partial。 |
| [`desktop/EDetection.Desktop/Views/Settings/DetectionRulesSettingsSection.xaml`](desktop/EDetection.Desktop/Views/Settings/DetectionRulesSettingsSection.xaml) | 检测规则分区试点 `UserControl`；在 `SettingsView` 中以 `x:Name="DetectionRulesSection"` 承载，供搜索和分类导航定位。 |
| [`desktop/EDetection.Tests/DetectionRulePipelineTests.cs`](desktop/EDetection.Tests/DetectionRulePipelineTests.cs) | 15 个规则边界与冻结检测 xUnit 测试；xUnit 总计 22 项。 |
| [`desktop/scripts/Test-DesktopArchitecture.ps1`](desktop/scripts/Test-DesktopArchitecture.ps1) | 架构门禁：UI-free 断言、包版本集中管理、行数上限与 xUnit。 |
| [`desktop/scripts/Test-DesktopNativeBackendSmoke.ps1`](desktop/scripts/Test-DesktopNativeBackendSmoke.ps1) | 无界面原生核心冒烟与 20 项命名回归。 |
| [`desktop/scripts/Publish-Desktop.ps1`](desktop/scripts/Publish-Desktop.ps1) | 自包含发布、资源校验、包健康与便携 ZIP。 |
| [`desktop/scripts/Build-DesktopInstaller.ps1`](desktop/scripts/Build-DesktopInstaller.ps1) | 调用 Inno Setup 编译安装器。 |
| [`desktop/installer/E-Detection.Desktop.iss`](desktop/installer/E-Detection.Desktop.iss) | 按用户安装、升级清理、安全目录校验与卸载集成。 |
| [`.github/workflows/desktop.yml`](.github/workflows/desktop.yml) | Windows 构建、格式、架构门禁、原生烟测、发布与安装器验证。 |

## 快速开始

需要 Windows 10/11 x64、[.NET SDK 11.0.100](https://dotnet.microsoft.com/) 与 PowerShell 7。Windows App SDK 2.6.5 由 NuGet 还原，不需要单独安装运行时。

```powershell
dotnet restore .\desktop\EDetection.Desktop.slnx
dotnet build .\desktop\EDetection.Desktop.slnx -c Debug --no-restore
dotnet test .\desktop\EDetection.Tests\EDetection.Tests.csproj -c Debug --no-build
dotnet run --project .\desktop\EDetection.Desktop\EDetection.Desktop.csproj -c Debug
```

默认阈值和规则开关位于 [`config.json`](config.json)。首次启动会把默认配置复制到 `%LOCALAPPDATA%\E-Detection\Desktop\detection-config.json`。配置缺失时使用默认值；单个非有限或无法解析的数值回退到默认值；JSON 根节点不是对象、无法解析或电压/温度上下限倒置时，检测会返回 `error`。

## 验证与发布

```powershell
dotnet build .\desktop\EDetection.Desktop.slnx -c Debug --no-restore
dotnet format .\desktop\EDetection.Desktop.slnx --verify-no-changes --no-restore
dotnet test .\desktop\EDetection.Tests\EDetection.Tests.csproj -c Debug --no-build
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopArchitecture.ps1 -NoBuild
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopNativeBackendSmoke.ps1 -NoBuild
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Publish-Desktop.ps1 -RuntimeIdentifier win-x64
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopPackageHealth.ps1 -PackagePath .\artifacts\desktop\win-x64\publish
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Build-DesktopInstaller.ps1 -RuntimeIdentifier win-x64
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopInstallerSmoke.ps1 -InstallerPath .\artifacts\desktop\win-x64\installer\EDetection-Setup-win-x64.exe
```

发布脚本会生成 `artifacts\desktop\win-x64\publish`、`EDetection-win-x64.zip` 与 `EDetection-Setup-win-x64.exe`。`v2.0.2` 发布物有意保持未签名；Windows 可能显示“未知发布者”或 SmartScreen 提示。SHA-256 仅验证文件完整性，不能验证发布者身份。检测只读取输入 CSV，报告写入输出目录；项目脚本不会改写输入遥测。

## 架构与文档

| 文档 | 内容 |
| --- | --- |
| [`docs/architecture.md`](docs/architecture.md) | 项目依赖、DI 组合、运行生命周期、线程与取消、报告生成、发布模型、架构门禁。 |
| [`docs/data-model.md`](docs/data-model.md) | CSV 编码与列映射、请求/事件/报告 DTO、设置与密钥、工作簿列、规则与运行状态。 |
| [`docs/状态.md`](docs/状态.md) | 2026-11-16 验证快照、重构阶段、已知限制与后续路线。 |
| [`docs/交接.md`](docs/交接.md) | 交接用构建/测试/发布说明、关键文件、架构不变量与常见问题。 |
| [`desktop/README.md`](desktop/README.md) | 原生开发者指南：依赖、运行、验证矩阵、发布与排障。 |
| [`desktop/NATIVE_BACKEND_MIGRATION.md`](desktop/NATIVE_BACKEND_MIGRATION.md) | 从 Python 进程桥接到进程内原生后端的迁移记录，现为历史说明。 |

## 致谢

- [.NET](https://dotnet.microsoft.com/) 与 [Windows App SDK / WinUI 3](https://github.com/microsoft/WindowsAppSDK)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [Microsoft.Extensions.Hosting](https://learn.microsoft.com/dotnet/core/extensions/generic-host)
- [WinUIEx](https://github.com/dotMorten/WinUIEx)
- [xUnit v3](https://xunit.net/)
- [Inno Setup](https://jrsoftware.org/isinfo.php)

## 许可证

仓库当前未包含 `LICENSE` 文件，也未在项目文件中声明许可证条款；许可证状态未指定。
