## 变更说明

说明目的、用户可见影响和相关 Issue（如有）。

## 验证

- [ ] `dotnet restore .\desktop\EDetection.Desktop.slnx`
- [ ] `dotnet build .\desktop\EDetection.Desktop.slnx -c Debug --no-restore`
- [ ] `dotnet format .\desktop\EDetection.Desktop.slnx --verify-no-changes --no-restore`
- [ ] `dotnet test .\desktop\EDetection.Tests\EDetection.Tests.csproj -c Debug --no-build`
- [ ] `pwsh -File .\desktop\scripts\Test-DesktopArchitecture.ps1 -NoBuild`
- [ ] `pwsh -File .\desktop\scripts\Test-DesktopNativeBackendSmoke.ps1 -NoBuild`
- [ ] 文档链接已检查（如适用）

实际执行的命令与结果：

```text

```

## 检查

- [ ] 未在未经维护者同意的情况下修改版本号、发布流程或安装器。
- [ ] 已补充或更新测试与文档。
- [ ] 提交内容不包含密钥、真实路径、设备名或日志中的敏感信息。
- [ ] 变更聚焦单一问题，未包含无关格式化。
