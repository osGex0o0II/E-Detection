# 桌面开发指南

C# / .NET 11 / WinUI 3 桌面应用。检测核心 `EDetection.NativeCore` 不依赖 WinUI。

## 环境

- Windows 10（1809 及以上）或 Windows 11（x64）
- .NET SDK `11.0.100`
- PowerShell 7（`pwsh`）
- Inno Setup 7.1.1（仅构建安装器时需要，兼容 Inno Setup 6）

Windows App SDK 由 NuGet 还原。发布使用 `WindowsPackageType=None` 与 `WindowsAppSDKSelfContained=true`。

## 运行

从仓库根目录执行：

```powershell
dotnet restore .\desktop\EDetection.Desktop.slnx
dotnet build .\desktop\EDetection.Desktop.slnx -c Debug --no-restore
dotnet run --project .\desktop\EDetection.Desktop\EDetection.Desktop.csproj -c Debug
```

首次启动在 `%LOCALAPPDATA%\E-Detection\Desktop` 创建 `settings.json` 与 `detection-config.json`；默认值取自仓库根目录的 `config.json`。

## 测试

```powershell
dotnet format .\desktop\EDetection.Desktop.slnx --verify-no-changes --no-restore
dotnet test .\desktop\EDetection.Tests\EDetection.Tests.csproj -c Debug --no-build
pwsh -File .\desktop\scripts\Test-DesktopArchitecture.ps1 -NoBuild
pwsh -File .\desktop\scripts\Test-DesktopNativeBackendSmoke.ps1 -NoBuild
```

检查见 [Windows CI 工作流](../.github/workflows/desktop.yml)；UI 检查需 Windows 桌面会话。

## 发布

```powershell
pwsh -File .\desktop\scripts\Publish-Desktop.ps1 -RuntimeIdentifier win-x64
pwsh -File .\desktop\scripts\Test-DesktopPackageHealth.ps1 -PackagePath .\artifacts\desktop\win-x64\publish
pwsh -File .\desktop\scripts\Build-DesktopInstaller.ps1 -RuntimeIdentifier win-x64
pwsh -File .\desktop\scripts\Test-DesktopInstallerSmoke.ps1 -InstallerPath .\artifacts\desktop\win-x64\installer\EDetection-Setup-win-x64.exe
```

产物位于 `artifacts/desktop/win-x64/`：`publish/`、`EDetection-win-x64.zip` 与 `installer/EDetection-Setup-win-x64.exe`。

安装器按用户安装，默认路径 `%LOCALAPPDATA%\Programs\EDetection`；卸载默认保留设置，`-RemoveSettings` 才清理。

签名状态以 Release 为准；未签名安装包可能触发“未知发布者”或 SmartScreen。SHA-256 仅验证文件完整性，不能验证发布者身份。

## 常见问题

| 问题 | 处理 |
| --- | --- |
| 找不到 `ISCC.exe` | 安装 Inno Setup，或向 `Build-DesktopInstaller.ps1` 传入 `-IsccPath` |
| `dotnet test` 失败 | 保留 `global.json` 的 `Microsoft.Testing.Platform` 配置，先构建 |
| PowerShell API 不兼容 | 用 PowerShell 7 的 `pwsh` |
| 发布包缺少 WinAppSDK DLL | 同时检查 `WindowsAppSDKSelfContained=true` 与 `WindowsPackageType=None`，重新发布 |
| 安装文件清单不一致 | 重新发布后构建安装器，不要手工修改 `install-files.txt` |

技术参考：[架构](../docs/architecture.md) · [数据模型](../docs/data-model.md) · [维护说明](../docs/交接.md)。
