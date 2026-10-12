## 变更说明

<!-- 简要说明这个 PR 做了什么、为什么这么做 -->

## 验证方式

<!-- 描述如何验证，例如构建命令、手动测试步骤、受影响的注册表键等 -->
- [ ] `dotnet build EverDefault.slnx -c Release` 通过
- [ ] 已在 Windows 10 / 11 实机验证（如有涉及运行时行为）

## 关联 Issue

<!-- 如 Closes #123；没有可留空 -->

## 检查项

- [ ] 已更新 `CHANGELOG.md`（如有用户可见的功能 / 修复）
- [ ] 如涉及发布，版本变更仅改动 `Directory.Build.props` 的 `<Version>`
- [ ] 未提交构建产物（`dist/`、`installer/Output/` 等）
