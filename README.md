<p align="center">
  <img src="docs/images/e-detection-logo.svg" alt="E-Detection 标志" width="104" height="104" />
</p>

<h1 align="center">E-Detection</h1>
<p align="center">Windows 电力数据异常检测工具</p>
<p align="center">
  <img src="https://img.shields.io/badge/.NET-11-555555?style=flat-square&amp;labelColor=333333" alt=".NET 11" />
  <img src="https://img.shields.io/badge/WinUI-3-555555?style=flat-square&amp;labelColor=333333" alt="WinUI 3" />
  <img src="https://img.shields.io/badge/Windows-x64-555555?style=flat-square&amp;labelColor=333333" alt="Windows x64" />
</p>
<p align="center">
  <a href="https://github.com/osGex0o0II/E-Detection/releases/latest">下载</a> ·
  <a href="#快速开始">快速开始</a> ·
  <a href="docs/README.md">开发文档</a>
</p>

批量检测电力监控与 SCADA CSV 日报异常，生成 Excel 报告。

## 功能

- **电气异常**：检测电压、电流、功率、功率因数、温度异常，以及 CT 极性和 PT 接线异常。
- **设备与数据状态**：识别设备离线、传感器故障或未配置、数据冻结或恒定。
- **CSV 处理**：递归扫描目录，支持 UTF-8、GB18030、GBK 等编码及常见测点列名。
- **检测结果**：查看异常明细、设备汇总和报告历史，按需生成 Excel 工作簿。

Windows 10（1809+）/ 11 · x64 · 本地运行。

## 快速开始

1. 从 [Releases](https://github.com/osGex0o0II/E-Detection/releases/latest) 下载安装器（推荐）或便携 ZIP；便携版解压后运行 `Install-Desktop.ps1`。
2. 启动程序，选择 CSV 输入目录与报告输出目录，按需调整检测阈值。
3. 如需导出报告，开启“生成 Excel 报告”，然后点击“开始检测”。

输入 CSV 仅被读取，不会覆盖原始文件。

## 开发

构建需要 .NET SDK 11.0.100 和 PowerShell 7。命令见 [桌面开发指南](desktop/README.md)，系统设计与数据定义见 [技术文档](docs/README.md)。

## 发布与许可

`v2.0.2` 发布物未签名，Windows 可能显示“未知发布者”或 SmartScreen 提示。SHA-256 仅验证文件完整性，不能验证发布者身份。

仓库尚未提供开源许可证。修改或再分发代码前，请先确认授权。
