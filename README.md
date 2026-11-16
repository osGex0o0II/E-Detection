<p align="center">
  <img src="docs/images/e-detection-logo.svg" alt="E-Detection 标志" width="104" height="104" />
</p>

<h1 align="center">E-Detection</h1>
<p align="center">Windows 电力数据异常检测工具</p>
<p align="center">
  <a href="https://github.com/osGex0o0II/E-Detection/releases/latest">下载</a> ·
  <a href="#使用">使用</a> ·
  <a href="docs/README.md">开发文档</a>
</p>

E-Detection 批量分析电力监控与 SCADA 导出的 CSV 日报，识别电气及设备状态异常，在本机生成 Excel 报告。

## 功能

- **异常检测**：电压、电流、功率、功率因数、温度、CT/PT、设备离线、数据冻结及传感器状态。
- **CSV 处理**：递归扫描目录，支持 UTF-8、GB18030、GBK 等编码，识别常见测点列名。
- **结果与报告**：查看异常明细、设备汇总和报告历史；导出含 6 张工作表的 `.xlsx` 报告。

基于 C#、.NET 11 和 WinUI 3，支持 Windows 10/11 x64。核心检测在本机运行。

## 界面

以下为改版前的待命界面截图，尚未展示检测结果；标题栏保留旧版图标。

![E-Detection 工作台](docs/images/e-detection-workbench.png)

<details>
<summary>设置页面</summary>

![E-Detection 设置页面](docs/images/e-detection-settings.png)

</details>

## 使用

1. 从 [Releases](https://github.com/osGex0o0II/E-Detection/releases/latest) 下载 Windows x64 安装包或便携包。
2. 选择 CSV 输入目录及报告输出目录，按需调整检测规则和阈值。
3. 启用“生成 Excel 报告”并点击“开始检测”，查看检测结果。

输入 CSV 仅被读取，不会覆盖原始文件。

## 开发

需要 .NET SDK 11.0.100 和 PowerShell 7。完整构建、测试与发布命令见 [桌面开发指南](desktop/README.md)；[架构、数据模型等文档](docs/README.md) 单独维护。

## 发布与许可

`v2.0.2` 发布物未签名，Windows 可能显示“未知发布者”或 SmartScreen 提示。SHA-256 仅验证文件完整性，不能验证发布者身份。

仓库目前未包含 `LICENSE` 文件；代码使用与再分发须遵守适用版权规定，不能仅因仓库公开便视为已获授权。
