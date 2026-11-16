# 原生后端迁移记录（历史）

> 历史记录：Python 进程桥接迁移至 C# 原生后端。现行设计见 [架构](../docs/architecture.md) 与 [数据模型](../docs/data-model.md)。

## 迁移结论

E-Detection 已从 Python 进程桥接迁移至进程内 C# + .NET 11 后端。`native` 是唯一运行与发布配置；Python 后端、运行时、依赖锁定、兼容入口与对等测试基线均已从仓库移除。

检测核心位于 [`EDetection.NativeCore/`](EDetection.NativeCore/)，不依赖 WinUI 或 Windows App SDK。`EDetection.Desktop/` 仅作为 WinUI 壳引用该核心；[`EDetection.Desktop.Tests/`](EDetection.Desktop.Tests/) 是独立的 `net11.0` 控制台测试项目，只引用 `Desktop.Core` 与 `NativeCore`，可在无交互 GitHub Actions Runner 中验证检测逻辑。

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

- 递归 CSV 发现、UTF-8 BOM / UTF-8 / GB18030 / GBK 编码选择、列名归一化与高压日报跳过。
- 电压、电流、功率、功率因数、温度、冻结、离线、传感器与 CT/PT 检测规则。
- 结构化运行事件、文件与目录统计、异常预览、设备与问题类型汇总。
- 原生 `.xlsx` 报告：检测概览、异常明细、设备汇总、异常分类统计、传感器状态与检测配置。
- 设置迁移、托盘/通知/启动集成、包健康检查、便携安装与 Inno Setup 升级安装。

## 发布约束

- 发布与安装命令固定使用 `native`；包健康检查拒绝 `python-runtime`、`python-wheelhouse`、`core`、`e_detection` 等旧资产。
- `v2.0.2` GitHub Release 有意保持未签名，并须通过安装器烟测与发布姿态门禁；SHA-256 仅验证文件完整性，不能验证发布者身份。
- 真实数据目录只读审计属于发布验证；审计工件不得包含输入路径、设备名或遥测明文。
- 原生核心烟测在启动时断言未加载 `Microsoft.WindowsAppRuntime`、`Microsoft.UI.Xaml` 或 `WinUIEx`；CI 不得以延长超时替代该隔离验证。

## 维护方向

后续规则、报告与 UI 改动只维护原生实现与 .NET 测试。重新引入 Python 运行时、进程桥接、wheelhouse 或旧 PyInstaller 产物属于架构回退，须经单独设计审查。

## 相关文档

- [架构](../docs/architecture.md)
- [数据模型](../docs/data-model.md)
- [项目状态](../docs/状态.md)
- [维护说明](../docs/交接.md)
- [桌面开发指南](README.md)
