# 贡献指南

感谢参与。提交前请先搜索现有 Issue，避免重复。

## 前置条件

- Windows 10（1809 及以上）或 Windows 11（x64）。
- .NET SDK 11.0.100，版本由 `global.json` 约束。
- PowerShell 7（`pwsh`，脚本要求 7.4 及以上）。
- Inno Setup 7.1.1（仅构建安装器时需要，兼容 Inno Setup 6）。

## 构建与验证

从仓库根目录执行：

```powershell
dotnet restore .\desktop\EDetection.Desktop.slnx
dotnet build .\desktop\EDetection.Desktop.slnx -c Debug --no-restore
dotnet format .\desktop\EDetection.Desktop.slnx --verify-no-changes --no-restore
dotnet test .\desktop\EDetection.Tests\EDetection.Tests.csproj -c Debug --no-build
pwsh -File .\desktop\scripts\Test-DesktopArchitecture.ps1 -NoBuild
pwsh -File .\desktop\scripts\Test-DesktopNativeBackendSmoke.ps1 -NoBuild
```

本地运行：

```powershell
dotnet run --project .\desktop\EDetection.Desktop\EDetection.Desktop.csproj -c Debug
```

交互式 UI 烟测需要 Windows 桌面会话；CI 默认不执行。完整检查见 [Windows CI 工作流](.github/workflows/desktop.yml)。

## 架构与测试约束

- 依赖方向与模块职责见 [架构](docs/architecture.md)。
- CSV、规则、事件与报告契约见 [数据模型](docs/data-model.md)。
- `EDetection.NativeCore` 不引用 WinUI 或 Windows App SDK；`EDetection.Domain` 与 `EDetection.Desktop.Core` 保持 UI-free。
- 行级规则顺序固定为电压 → 电流 → 有功功率 → 功率因数 → 温度；冻结/恒定检测随后执行。
- xUnit v3 使用 `Microsoft.Testing.Platform`；不要移除 `global.json` 中的测试运行器配置。
- 规则、报告格式或设置迁移变更必须补充测试，并先更新对应文档。

## 分支与 PR

- 从 `main` 创建短生命周期分支，例如 `fix/...`、`docs/...`。
- 每个 PR 聚焦一个问题，说明变更目的、用户可见影响和实际执行的验证命令。
- 提交信息保持简洁，使用 `fix:`、`docs:` 等前缀说明意图。
- 提交 PR 前确认工作区不包含构建产物、日志或本地设置。
- 涉及版本号、发布流程、安装器或更新源的改动，须先获得维护者同意；不要在普通 PR 中调整发布版本。
- 安全问题不要通过公开 PR 或 Issue 报告，请按 [安全策略](SECURITY.md) 处理。

## 代码风格

- 遵循 `.editorconfig` 与现有代码风格；提交前运行 `dotnet format`。
- 保持 nullable 与 implicit usings 开启，依赖版本统一由 `Directory.Packages.props` 管理。
- PowerShell 脚本使用 `pwsh` 7，不使用 Windows PowerShell 5.1 专有行为。
- 不修改无关代码或进行大范围格式化，避免掩盖行为变更。

## 文档

- 中文文档保持简洁、专业，使用全角标点，不使用 emoji。
- Markdown 使用相对链接，新增或移动文件后检查链接。
- 不添加营销文案、合成截图或未经验证的性能与兼容性声明。
- 仓库未提供开源许可证；再分发或用于其他项目前请先确认授权。
