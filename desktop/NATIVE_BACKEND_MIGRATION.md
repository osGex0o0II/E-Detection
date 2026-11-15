# 原生后端迁移记录（历史）

> 归档：2026-11-16
> 本文记录从 Python JSON Lines 进程桥接到进程内原生后端的迁移结果，仅作历史说明，不是当前执行策略。
> 当前架构、运行生命周期和发布模型以 [`../docs/architecture.md`](../docs/architecture.md) 为准；字段与报告模型见 [`../docs/data-model.md`](../docs/data-model.md)。

## 迁移结论

E-Detection 已完成从 Python 进程桥接到进程内 **C# + .NET 11** 检测后端的迁移。`native` 是唯一支持的运行和发布配置；Python 后端、运行时、依赖锁定、兼容入口与对等测试基线已从仓库移除。

原生检测核心位于 [`EDetection.NativeCore/`](EDetection.NativeCore/)，不依赖 WinUI 或 Windows App SDK。`EDetection.Desktop/` 只作为 WinUI 壳引用该核心；[`EDetection.Desktop.Tests/`](EDetection.Desktop.Tests/) 是独立的 `net11.0` 控制台测试项目，仅引用 `Desktop.Core` 与 `NativeCore`，可在无交互 GitHub Actions Runner 中验证检测逻辑。

## 迁移后的落点

| 位置 | 职责 |
| --- | --- |
| `EDetection.NativeCore/Services/NativeDetectionBackendService.*.cs` | CSV 解析、规则、明细、汇总、OpenXML 报告。 |
| `EDetection.NativeCore/Models/` | `DetectionRequest`、`DetectionBackendEvent`、报告 DTO、对等追踪。 |
| `EDetection.Infrastructure/DependencyInjection.cs` | `IDetectionBackendService` → `NativeDetectionBackendService`。 |
| `EDetection.Desktop.Core/` | WinUI-free 设置、诊断、事件映射、运行状态、报告摘要。 |
| `EDetection.Desktop/` | WinUI 3 壳、托盘、通知、窗口与设置页面。 |
| `EDetection.Desktop.Tests/` | 无界面冒烟与 20 项命名回归。 |

## 已验证能力

- 递归 CSV 发现、UTF-8 BOM/UTF-8/GB18030/GBK 编码选择、列名归一化和高压日报跳过。
- 电压、电流、功率、功率因数、温度、冻结、离线、传感器与 CT/PT 相关检测规则。
- 结构化运行事件、文件和目录统计、异常预览、设备与问题类型汇总。
- 原生 `.xlsx` 报告：检测概览、异常明细、设备汇总、异常分类统计、传感器状态和检测配置。
- 设置迁移、托盘/通知/启动集成、包健康检查、便携安装和 Inno Setup 升级安装。

## 发布约束

- 所有发布和安装命令固定使用 `native`；包健康检查拒绝 `python-runtime`、`python-wheelhouse`、`core`、`e_detection` 等旧资产。
- `v2.0.2` GitHub Release 有意保持未签名，并须通过安装器烟测及发布姿态门禁；SHA-256 仅验证文件完整性，不能验证发布者身份。
- 真实数据目录只读审计是发布验证的一部分；审计工件不得含输入路径、设备名或遥测明文。
- 原生核心烟测会在启动时断言未加载 `Microsoft.WindowsAppRuntime`、`Microsoft.UI.Xaml` 或 `WinUIEx`；CI 中不得以延长超时替代该隔离验证。

## 维护方向

后续规则、报告和 UI 改动只维护原生实现与 .NET 测试。任何重新引入 Python 运行时、进程桥接、wheelhouse 或旧 PyInstaller 产物的变更都属于架构回退，必须经过单独设计审查。

## 相关文档

- [`../docs/architecture.md`](../docs/architecture.md)
- [`../docs/data-model.md`](../docs/data-model.md)
- [`../docs/状态.md`](../docs/状态.md)
- [`../docs/交接.md`](../docs/交接.md)
- [`README.md`](README.md)
