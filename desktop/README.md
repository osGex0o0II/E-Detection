# EDetection Desktop

`desktop/` 包含 E-Detection 的 **C# + .NET 11 + WinUI 3** 原生桌面应用。CSV 解析、规则检测、事件处理、汇总和 Excel 报告都在本机进程内完成，不依赖 Python、wheelhouse 或外部运行时桥接。

产品形态、依赖方向和运行生命周期见 [`../docs/architecture.md`](../docs/architecture.md)；CSV 字段、DTO 和报告列见 [`../docs/data-model.md`](../docs/data-model.md)。

## 产品形态

当前桌面应用是一个未打包（`WindowsPackageType=None`）的 WinUI 3 工作台：

- 一个主窗口，左侧输入/报告/运行配置，右侧风险指标、异常明细和运行记录。
- 设置页覆盖检测阈值、通知、代理、更新、启动、主题和诗词状态。
- 托盘、单实例、关闭到托盘、任务栏进度、桌面通知和报告历史。
- 报告为 6 张工作表的 `.xlsx`：检测概览、异常明细、设备汇总、异常分类统计、传感器状态、检测配置。
- 设置页使用 `CommunityToolkit.WinUI.Controls.SettingsControls` `8.2.251219`：17 个 `SettingsCard`、17 处 `{x:Bind}`、`SettingsLayoutStates` 三态 VSM；检测规则分区已提取为 `DetectionRulesSettingsSection`。
- `native` 是唯一检测后端；旧 Python 入口和进程桥接已移除。

## 当前项目

| 项目 | 目标 | 作用 |
| --- | --- | --- |
| `EDetection.Domain` | `net11.0` | 纯标记程序集；无 UI/IO 依赖。 |
| `EDetection.NativeCore` | `net11.0` | CSV 检测、规则、事件、汇总、OpenXML 报告；无 WinUI 依赖。 |
| `EDetection.Infrastructure` | `net11.0` | `IDetectionBackendService` 的 DI 适配。 |
| `EDetection.Desktop.Core` | `net11.0` | WinUI-free 设置、诊断、历史、事件映射、运行状态、报告摘要。 |
| `EDetection.Desktop` | `net11.0-windows10.0.19041.0` | WinUI 3 壳、视图、视图模型、托盘/通知/窗口集成。 |
| `EDetection.Tests` | `net11.0` | xUnit v3 组合根与架构测试（7 项）。 |
| `EDetection.Desktop.Tests` | `net11.0` | 无界面原生核心冒烟 + 20 项命名回归。 |
| `EDetection.Desktop.slnx` | — | 以上项目的解决方案入口。 |
| `installer/E-Detection.Desktop.iss` | — | Inno Setup 安装器定义。 |
| `scripts/` | — | 发布、包健康、安装/卸载、安装器与桌面烟测。 |

## 环境要求

| 依赖 | 版本/说明 |
| --- | --- |
| Windows | Windows 10 1809+ x64（`TargetPlatformMinVersion=10.0.17763.0`）；Windows 11 推荐。 |
| .NET SDK | `11.0.100`；`global.json` 允许 `latestFeature`。 |
| PowerShell | PowerShell 7（`pwsh`）。部分脚本依赖 .NET Core 的 `ProcessStartInfo.ArgumentList`。 |
| Windows App SDK | `2.6.5`，由 NuGet 还原；不需要单独安装 Windows App Runtime。 |
| Inno Setup | 构建安装器需要 `7.1.1`；脚本兼容 Inno Setup 6。 |
| xUnit | xUnit v3 `4.0.3`，通过 `Microsoft.Testing.Platform` 运行。 |

## 运行与调试

```powershell
dotnet restore .\desktop\EDetection.Desktop.slnx
dotnet build .\desktop\EDetection.Desktop.slnx -c Debug --no-restore
dotnet run --project .\desktop\EDetection.Desktop\EDetection.Desktop.csproj -c Debug
```

也可以在支持 WinUI 3 的 IDE 中启动 `EDetection.Desktop` 项目。开发构建和发布包都启用 `WindowsAppSDKSelfContained=true`，因此不要求机器预装 Windows App Runtime。

首次启动会创建：

- `%LOCALAPPDATA%\E-Detection\Desktop\settings.json`
- `%LOCALAPPDATA%\E-Detection\Desktop\detection-config.json`

设置目录可用 `EDETECTION_DESKTOP_SETTINGS_DIR` 覆盖。默认检测阈值来自发布目录的 `config.json`，首次启动复制为用户配置；之后以用户配置为准。

## 构建、格式与测试

```powershell
dotnet build .\desktop\EDetection.Desktop.slnx -c Debug --no-restore
dotnet format .\desktop\EDetection.Desktop.slnx --verify-no-changes --no-restore
dotnet test .\desktop\EDetection.Tests\EDetection.Tests.csproj -c Debug --no-build
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopArchitecture.ps1 -NoBuild
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopNativeBackendSmoke.ps1 -NoBuild
```

`Test-DesktopArchitecture.ps1` 检查：

- `Domain` / `Desktop.Core` 不引用 WinUI 或 Windows App SDK。
- `build/E-Detection` 未被跟踪。
- 项目文件不使用带 `Version=` 的 `PackageReference`（中央包管理）。
- `MainViewModel.cs` ≤ 600 行，`NativeDetectionBackendService.cs` ≤ 120 行。
- `SettingsView*.cs` ≤ 450 行，后端/`MainViewModel` partial ≤ 800 行。
- 构建与 xUnit 测试。

原生烟测会运行无界面基础冒烟和 `--regression all` 的 20 个命名回归，并在启动时断言未加载 `Microsoft.WindowsAppRuntime`、`Microsoft.UI.Xaml` 或 `WinUIEx`。

## 发布与安装器

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Publish-Desktop.ps1 -RuntimeIdentifier win-x64
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopPackageHealth.ps1 -PackagePath .\artifacts\desktop\win-x64\publish
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Build-DesktopInstaller.ps1 -RuntimeIdentifier win-x64
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopInstallerSmoke.ps1 -InstallerPath .\artifacts\desktop\win-x64\installer\EDetection-Setup-win-x64.exe
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopInstallSmoke.ps1 -PackagePath .\artifacts\desktop\win-x64\publish
```

产物：

| 路径 | 内容 |
| --- | --- |
| `artifacts/desktop/win-x64/publish/` | 自包含应用与交付脚本。 |
| `artifacts/desktop/win-x64/EDetection-win-x64.zip` | 便携包。 |
| `artifacts/desktop/win-x64/installer/EDetection-Setup-win-x64.exe` | Inno Setup 安装器。 |

安装器默认安装到 `%LOCALAPPDATA%\Programs\EDetection`，`PrivilegesRequired=lowest`，按用户安装，不需要管理员权限。升级时按 `install-files.txt` 清理旧产品文件，并移除旧的 `core`、`e_detection`、`python-runtime`、`python-wheelhouse` 目录。卸载默认保留用户设置，只有 `Uninstall-Desktop.ps1 -RemoveSettings` 才删除。

`v2.0.2` 安装器和便携包有意保持未签名；Windows 可能显示“未知发布者”或 SmartScreen 提示。SHA-256 仅验证文件完整性，不能验证发布者身份。

## 包内容与自包含 WinAppSDK

`Publish-Desktop.ps1` 使用：

```powershell
dotnet publish .\desktop\EDetection.Desktop\EDetection.Desktop.csproj `
  -c Release -r win-x64 --self-contained true --no-restore `
  -o .\artifacts\desktop\win-x64\publish
```

发布目录必须包含：

- `EDetection.exe`、`EDetection.dll`。
- `EDetection.NativeCore.dll`、`EDetection.Desktop.Core.dll`、`EDetection.Domain.dll`、`EDetection.Infrastructure.dll`。
- `Microsoft.WindowsAppRuntime.dll`、`Microsoft.UI.Xaml.dll`、`CoreMessagingXP.dll`、`DWriteCore.dll`、`MRM.dll`。
- `App.xbf`、`MainWindow.xbf`、`EDetection.pri`、`Styles/Common.xbf`、5 个视图/设置 `.xbf`：`Views/AppShellView.xbf`、`Views/DetectionWorkbenchView.xbf`、`Views/RunSetupView.xbf`、`Views/SettingsView.xbf`、`Views/Settings/DetectionRulesSettingsSection.xbf`。
- `config.json`、图标、交付脚本、`release-info.txt`、`INSTALL.txt`、`install-files.txt`。

`WindowsAppSDKSelfContained=true` 与 `WindowsPackageType=None` 必须同时保留。前者让 Windows App SDK 二进制进入发布目录，后者允许未打包运行；只设置 `SelfContained=true` 只会打包 .NET 运行时，干净机器仍可能缺少 Windows App Runtime。`PublishSingleFile=false`、`PublishTrimmed=false` 也是有意设置，便于检查 `.xbf` / `.pri` 和依赖 DLL。

## 验证矩阵

| 检查 | 命令 | 覆盖 |
| --- | --- | --- |
| 构建 | `dotnet build ... -c Debug --no-restore` | 全部项目编译；当前为 0 警告/0 错误。 |
| 格式 | `dotnet format ... --verify-no-changes --no-restore` | 代码风格与行尾。 |
| xUnit | `dotnet test ... --no-build` | 组合根、架构、报告摘要（7 项）。 |
| 架构门禁 | `Test-DesktopArchitecture.ps1 -NoBuild` | UI-free、中央包管理、行数上限、构建/测试。 |
| 原生烟测 | `Test-DesktopNativeBackendSmoke.ps1 -NoBuild` | 无界面检测、20 项命名回归、UI 运行时隔离。 |
| 设置页 UI | `Test-DesktopSettingsSmoke.ps1 -Width 1600 -Height 1000` 等 | 1600×1000、1000×900、760×900；17 个 `SettingsCard` 与命名控件 UI Automation 可发现。 |
| 设置迁移 | `Test-DesktopSettingsMigrationSmoke.ps1` | 设置版本迁移、可选网络开关、`MainViewModel*.cs` 扫描。 |
| 脚本安全 | `Test-DesktopScriptSafetySmoke.ps1` | 破坏性路径、启动项路径归属。 |
| 诊断脱敏 | `Test-DesktopDiagnosticsRedactionSmoke.ps1` | 诊断包不含密钥/用户名/路径明文。 |
| 发布姿态 | `Test-DesktopReleasePosture.ps1` | 未签名文案、校验边界、原生包元数据、静态脚本。 |
| 包健康 | `Test-DesktopPackageHealth.ps1` | 必需文件、WinAppSDK DLL、清单、旧 Python 资产拒绝。 |
| 便携安装 | `Test-DesktopInstallSmoke.ps1` | 安装/卸载、快捷方式、清单。 |
| 安装器 | `Build-DesktopInstaller.ps1` + `Test-DesktopInstallerSmoke.ps1` | 编译、安装、升级、拒绝不安全目录。 |
| UI 烟测 | `Test-DesktopSingleInstanceSmoke.ps1` 等 | 需交互桌面；CI 默认跳过。 |

## 排障

### `ISCC.exe` 找不到

`Build-DesktopInstaller.ps1` 会依次查找 `-IsccPath`、`$env:ISCC_PATH`、PATH、`%LOCALAPPDATA%\Programs\Inno Setup 7`、`C:\Program Files\Inno Setup 7`、`C:\Program Files (x86)\Inno Setup 7`，再尝试 Inno Setup 6。安装 Inno Setup 7.1.1，或显式传：

```powershell
pwsh -File .\desktop\scripts\Build-DesktopInstaller.ps1 -IsccPath "C:\Program Files\Inno Setup 7\ISCC.exe"
```

### `dotnet test` 找不到测试

`global.json` 必须保留：

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

`EDetection.Tests` 是 xUnit v3 可执行测试项目，不是传统 VSTest 项目。先 `dotnet build`，再 `dotnet test ... --no-build`。不要在项目里混用 VSTest 适配器。

### 原生烟测报“不能对 Null 值表达式调用方法”

用 Windows PowerShell 5.1 运行时，`ProcessStartInfo.ArgumentList` 不存在，脚本会在启动测试进程时失败。改用 `pwsh`：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\desktop\scripts\Test-DesktopNativeBackendSmoke.ps1 -NoBuild
```

### 发布包缺少 WinAppSDK DLL

确认 `EDetection.Desktop.csproj` 仍有：

```xml
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
```

然后重新执行 `Publish-Desktop.ps1`。不要用旧的 `publish` 目录直接构建安装器；包健康会拒绝缺少 `Microsoft.WindowsAppRuntime.dll` / `Microsoft.UI.Xaml.dll` 的目录。

### 安装器提示文件清单不一致

`install-files.txt` 由发布脚本生成，必须与 `publish` 目录一致。先重新发布，再构建安装器；不要手工编辑清单。`Test-DesktopPackageHealth.ps1` 会检查缺失、额外文件和旧 Python 资产。

### 升级后出现旧文件

安装器按清单删除旧产品文件，并显式清理 `core`、`e_detection`、`python-runtime`、`python-wheelhouse`。如果仍有残留，先确认安装目录是 EDetection 产品目录，再运行 `Uninstall-Desktop.ps1` 或重新安装；不要直接删除未知目录。

### SmartScreen / 未知发布者

`v2.0.2` 产物未签名，这是发布约定，不是构建失败。SHA-256 只验证下载字节完整性，不验证发布者身份。需要签名时配置 `WINDOWS_CODE_SIGNING_PFX_BASE64` 与 `WINDOWS_CODE_SIGNING_PFX_PASSWORD`，并按工作流签名；`v2.0.2` 标签会强制保持未签名。

## 相关文档

- [`../docs/architecture.md`](../docs/architecture.md)
- [`../docs/data-model.md`](../docs/data-model.md)
- [`../docs/状态.md`](../docs/状态.md)
- [`../docs/交接.md`](../docs/交接.md)
- [`NATIVE_BACKEND_MIGRATION.md`](NATIVE_BACKEND_MIGRATION.md)（历史迁移记录）
