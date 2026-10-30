# EverDefault

<img src="./src/EverDefault.App/Assets/logo.png" align="right" width="200" alt="EverDefault logo">

[![Release](https://img.shields.io/github/v/release/BUGJI/EverDefault?label=release&sort=semver)](https://github.com/BUGJI/EverDefault/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/BUGJI/EverDefault/total)](https://github.com/BUGJI/EverDefault/releases)
[![License](https://img.shields.io/github/license/BUGJI/EverDefault)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)

EverDefault 会守住你选择的默认程序和系统设置。一旦被其他软件改动，它会自动改回来。

> A Windows registry guard that locks in your choices and reverts unwanted changes automatically.

安装后跟着向导勾几下就能用，不需要了解注册表、ProgId 或哈希算法。

## 目录

- [怎么用](#怎么用)
- [功能](#功能)
- [界面说明](#界面说明)
- [系统要求](#系统要求)
- [设置](#设置)
- [通知与暂停](#通知与暂停)
- [杀毒软件提示](#杀毒软件提示)
- [规则导入 / 导出](#规则导入--导出)
- [数据目录](#数据目录)
- [常见问题](#常见问题)
- [已知限制](#已知限制)
- [更新日志](#更新日志)
- [技术说明](#技术说明)
- [许可证](#许可证)

## 怎么用

1. 到 [Releases](https://github.com/BUGJI/EverDefault/releases/latest) 下载 `EverDefault-Setup-<版本>.exe` 并运行。
2. 首次启动会出现向导：
   - 勾选想保护的项目（默认浏览器、邮件、PDF、图片、视频……）。
   - 点击「开始保护」即可。
3. 以后其他程序想改掉这些默认设置时，EverDefault 会自动改回，并在托盘弹出提示。

> 只想临时试一下：双击 `dist\EverDefault.App.exe`，未安装服务时会以用户模式运行，无需管理员权限。

## 功能

| 模块 | 说明 |
| --- | --- |
| 默认应用守护 | 锁定文件类型（`.pdf`、`.png` 等）和链接类型（`http`、`https`、`mailto`）的默认程序。会自动重算并回写 Win10/11 的 `UserChoice` 哈希。 |
| 整理此电脑 | 扫描「此电脑」和桌面上多出来的图标，勾选后一键移除。 |
| 系统设置保护 | 锁定显示文件扩展名、隐藏文件、最近使用记录、自动播放等常用设置。 |
| 高级规则 | 需要精确控制时，可编辑任意注册表键/值，支持还原、删除、仅记录、写策略。 |

其他特性：

- **基础配置 / 高级配置双 Tab**：基础配置只提供搜索、勾选和图形化预览；高级配置才出现原始字段。
- **一键接管当前默认**：读取你现在使用的默认程序，按应用聚合，勾选即可保护，不会改变现状。
- **托盘通知**：拦截时显示「检测到修改，已改回」，点击可直接调整对应规则；通知可完全关闭。
- **暂停保护**：支持全局暂停 1 小时和单项暂停 1 小时，到点自动恢复。
- **活动记录**：用普通语言记录每一次拦截，可展开查看原始注册表详情。
- **亮色 / 暗色主题**、开机自启、开机后隐藏到托盘、版本更新检查。
- 导入 / 导出可读 JSON 备份（高级模式）。

## 界面说明

- **主页**：当前保护状态、暂停/恢复、统计、推荐保护和最近活动。
- **我的保护**：按应用聚合的保护项，每项一个开关，可调整、暂停或停止；支持搜索、状态筛选和全部暂停/恢复。
- **活动**：按「全部 / 已拦截 / 需要处理」筛选，支持搜索和清空记录，可展开原始详情。
- **设置**：常规设置；服务模式、用户范围、日志保留等收在「高级选项」里。
- **高级规则**：在 设置 → 高级选项 中打开「显示高级选项」后出现。

## 系统要求

- **操作系统**：Windows 10 / 11（Win7 / Win8 仅支持部分功能）。
- **运行时**：.NET Framework 4.8。Win10 1903+ / Win11 通常已自带。
- **权限**：用户模式无需管理员；安装系统服务（守护系统级设置与所有用户）需要管理员权限。
- **磁盘**：程序数据位于 `%ProgramData%\EverDefault`。

## 设置

基础设置：

- **启用保护**：总开关。
- **随系统启动**：登录后自动启动托盘程序。
- **开机后隐藏到托盘**。
- **拦截时用气泡通知我**：关闭后完全静默，但仍会在后台保护并记录。
- **主题**：亮色 / 暗色 / 跟随系统。
- **检查版本更新**：按天 / 周 / 月，也可以点「立即检查更新」。
- **备份与数据**：备份 / 恢复保护规则、打开数据目录、清空活动记录。

高级选项：

- **显示高级选项**：打开后出现原始规则页。
- **运行模式**：用户模式 / 服务模式，支持安装、重启、卸载服务。
- **系统版本覆盖**、**日志保留天数**、**目标用户范围**。

## 通知与暂停

- 拦截时托盘会弹出气泡，例如：`检测到「.pdf 默认程序」被修改，已改回 Acrobat。`
- 点击气泡会打开对应规则，可直接换应用、暂停或停止保护。
- 托盘右键可以「暂停保护 1 小时」；再次点击即可恢复。
- 在 Windows 专注助手开启或系统通知被关闭时，气泡可能不显示；活动页仍会记录。

## 杀毒软件提示

本工具会「监控并回写注册表」，这类行为容易被杀软误报为可疑。请将以下文件 / 目录加入白名单：

- `EverDefault.Service.exe`
- `EverDefault.App.exe`
- `%ProgramData%\EverDefault`

## 规则导入 / 导出

在 设置 → 备份与数据 中，或高级模式的工具栏，提供：

- **备份**：把当前全部规则保存为 JSON，格式为 `{Version, ExportedUtc, Rules:[...]}`。
- **恢复**：从 JSON 追加导入（自动分配新 ID，不覆盖已有规则）。

参考样例：`packaging/sample-rules.json`。

## 数据目录

所有数据位于 `%ProgramData%\EverDefault`（服务与托盘共享）：

```
rules.json      规则
baselines.json  基线
changelog.json  变更日志
settings.json   设置
```

卸载时安装程序会询问是否一并删除该目录。

## 常见问题

**想换默认程序，但被改回去了？**
这是保护在生效。先到托盘右键「暂停保护 1 小时」，或在「我的保护」里暂停/调整对应项目，再改默认程序。改完后恢复保护即可。

**改了注册表，活动里却没有记录？**
确认「设置 → 启用保护」已开启；高级规则如果使用 `Manual` 模式不会自动触发。

**默认应用设置后又被系统改回去了？**
EverDefault 会持续回写，但某些系统更新会重置 `UserChoice`。若长期无效，通常是 Win11 新版本更换了哈希算法，请附系统版本反馈。

**用户模式下能守护系统级设置吗？**
不能。守护系统级设置或所有用户，需要在 设置 → 高级选项 → 运行模式 中安装服务模式。

**通知太吵 / 不想看到气泡？**
设置里关闭「拦截时用气泡通知我」即可，后台仍会正常保护并记录。

**数据存在哪里？如何备份？**
见[数据目录](#数据目录)，直接复制 `%ProgramData%\EverDefault` 即可。

**杀软报警或文件被删除？**
见[杀毒软件提示](#杀毒软件提示)，将相关文件 / 目录加入白名单。

## 已知限制

- `UserChoice` 哈希算法在 Win10 / Win11 验证通过；Win7 / Win8 走传统关联路径。Win11 某些新版本可能改用另一套算法，若无效请反馈。
- 仅处理已加载的用户配置单元（已注销用户不处理）。
- 服务在需要接管受保护键时，会把键的属主改为 `SYSTEM`（已为用户补回完全控制）。
- 通知只提示「检测到修改」以及被改成哪个应用，不显示具体是哪个进程写入的：注册表变更通知本身不携带进程信息。
- `OpenWithProgids` 项以 `REG_SZ` 空值写入，功能可用，但类型与系统不完全一致。

## 更新日志

各版本的新增与修复见 [CHANGELOG.md](CHANGELOG.md)，每条发布说明的完整原文见 [Releases](https://github.com/BUGJI/EverDefault/releases)。

---

# 技术说明

## 项目结构

```
src/
  EverDefault.Core/         模型、枚举、规则/设置定义、存储接口、规则冲突检测
  EverDefault.Registry/     注册表访问、快照、UserChoice 哈希、当前关联读取、命名空间扫描
  EverDefault.Persistence/  JSON 存储实现（规则 / 基线 / 日志 / 设置）
  EverDefault.Serialization/ 共享 JSON 序列化配置（字典类型名、白名单绑定器）
  EverDefault.Ipc/          命名管道协议、分帧、序列化、规则编解码、管道安全
  EverDefault.Service/      监控引擎宿主、调度器、各规则模块处理器
  EverDefault.App/          WPF 托盘界面（主页 / 我的保护 / 活动 / 设置 / 高级规则）
poc/
  EverDefault.Poc.UserChoice/  UserChoice 哈希算法验证程序
installer/                     Inno Setup 安装脚本
packaging/                     随发行包一起分发的脚本、说明与样例规则
```

## 技术栈

- .NET Framework 4.8（`net48`），C# 7.3
- WPF + Windows Forms（托盘）
- Newtonsoft.Json（随发行包提供）
- 命名管道 IPC，Windows 注册表更改通知（`RegNotifyChangeKeyValue`）

## 构建

需要 .NET SDK（用于 `net48` 目标的 `Microsoft.NETFramework.ReferenceAssemblies`）。

```cmd
REM 构建整个解决方案
dotnet build EverDefault.slnx -c Release

REM 构建并刷新 dist\ 目录（生成发布包）
build-dist.cmd

REM 构建并编译 Inno Setup 安装包（需要 Inno Setup 6）
build-installer.cmd
```

> `build-dist.cmd` 会把文件复制到 `dist\`。若提示文件被占用，请先退出托盘程序并停止服务。

## 服务命令行参数

`EverDefault.Service.exe` 支持以下调试参数：

| 参数 | 说明 |
| --- | --- |
| `--console` | 以控制台模式运行（前台，Ctrl+C 停止）。 |
| `--ping` | 通过命名管道查询服务状态。 |
| `--users` | 列出当前用户范围解析到的 SID 与展开路径。 |
| `--saverule` | 通过 IPC 创建一条测试规则并读回。 |
| `--getlog` | 读取最近 50 条变更日志。 |
| `--import <rules.json>` | 通过 IPC 批量导入规则文件。 |

## 许可证

[MIT](LICENSE)
