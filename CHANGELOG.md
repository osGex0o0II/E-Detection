# 更新日志

本文件记录项目变更；格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循语义化版本。

## [Unreleased]

### 修复

- 修复 960 至 1000 DIP 紧凑布局下工作台不可达的问题；设置区与工作台可独立滚动。
- 为设置分类补充 UI Automation 名称，返回按钮移至左上角，支持 Esc 关闭设置并恢复焦点。
- 对齐开关状态文本、紧凑报告筛选和标题栏设置按钮，补充路径字段的完整值提示。
- 调整更新说明与版本哈希显示，补充版本格式单元测试。

## [v2.0.3] - 2026-11-16

### 变更

- 重新组织设置导航、Windows 标题栏和工作台操作。
- 检测阈值改为显式加载，增加未保存状态检查，并使用带滚动备份的原子写入。
- 扩展配置校验、设置页与桌面 UI 自动化回归检查。

### 修复

- 修正异常明细排序，补充可访问的导航标识和焦点恢复。

更早版本见 [GitHub Releases](https://github.com/osGex0o0II/E-Detection/releases)。

[Unreleased]: https://github.com/osGex0o0II/E-Detection/compare/v2.0.3...main
[v2.0.3]: https://github.com/osGex0o0II/E-Detection/releases/tag/v2.0.3
