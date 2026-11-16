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

批量检测电力监控与 SCADA CSV 日报，生成 Excel 异常报告。

## 功能

- **电气异常**：电压、电流、功率、功率因数、温度，CT 极性及 PT 接线异常。
- **设备状态**：离线、传感器故障/未配置、数据冻结/恒定。
- **CSV 处理**：递归扫描，支持 UTF-8、GB18030、GBK 等编码与常见测点列名。
- **结果查看**：异常明细、设备汇总、报告历史、Excel 导出。

Windows 10（1809+）/ 11 · x64 · 本地运行。

## 快速开始

1. 从 [Releases](https://github.com/osGex0o0II/E-Detection/releases/latest) 下载安装器或便携 ZIP；便携版解压后运行 `Install-Desktop.ps1`。
2. 选择 CSV 输入、报告输出目录，按需调整阈值。
3. 导出时开启“生成 Excel 报告”，点击“开始检测”。

CSV 只读，不覆盖原文件。

## 开发

构建需 .NET SDK 11.0.100、PowerShell 7；命令见 [桌面开发指南](desktop/README.md)，设计与数据见 [技术文档](docs/README.md)。参与开发前请阅读 [贡献指南](CONTRIBUTING.md)。

## 发布与许可

签名状态见 [Releases](https://github.com/osGex0o0II/E-Detection/releases/latest)；未签名安装包可能触发安全提示。SHA-256 仅验证文件完整性，不能验证发布者身份。

本项目使用 [MIT 许可证](LICENSE)，版权归 E-Detection contributors 所有。安全漏洞请按 [安全策略](SECURITY.md) 私下报告，不要公开 Issue。
