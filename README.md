# EverDefault

<img src="./src/EverDefault.App/Assets/logo.png" align="right" width="200" alt="EverDefault logo">

[![Release](https://img.shields.io/github/v/release/BUGJI/EverDefault?label=release&sort=semver)](https://github.com/BUGJI/EverDefault/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/BUGJI/EverDefault/total)](https://github.com/BUGJI/EverDefault/releases)
[![License](https://img.shields.io/github/license/BUGJI/EverDefault)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)

EverDefault 是一个 Windows 注册表守护工具：把你选定的东西「锁住」，一旦被其他软件改动，就自动改回来。

> A Windows registry guard that locks in your choices and reverts unwanted changes automatically.

它由两部分组成：

- **EverDefault.Service** —— 后台 Windows 服务，负责监控引擎，支持 HKLM 与各用户的 HKCU。
- **EverDefault.App** —— 托盘 + WPF 图形界面，通过命名管道（Named Pipe）与服务通信。

## 目录

- [功能](#功能)
- [快速开始](#快速开始)
- [系统要求](#系统要求)
- [设置](#设置)
- [杀毒软件提示](#杀毒软件提示)
- [正式安装为开机自启服务](#正式安装为开机自启服务)
- [规则导入 / 导出](#规则导入--导出)
- [数据目录](#数据目录)
- [常见问题](#常见问题)
- [已知限制](#已知限制)
- [更新日志](#更新日志)
- [技术说明](#技术说明)
  - [项目结构](#项目结构)
  - [技术栈](#技术栈)
  - [构建](#构建)
  - [服务命令行参数](#服务命令行参数)
- [许可证](#许可证)

## 功能

| 模块 | 说明 |
| --- | --- |
| 默认应用守护（DefaultApp） | 为指定文件类型（`.pdf`、`.ext` 等）锁定默认程序。会重算并回写 Win10/11 的 `UserChoice` 哈希，同时维护 `OpenWithProgids` 与旧式文件关联。 |
| This PC 命名空间（NameSpace） | 检测并删除「此电脑」下的自定义命名空间项，可按 Guid / 显示名 / 目标路径 / 正则匹配。 |
| 自定义注册表（CustomRegistry） | 按基线或期望值监控任意注册表键/值，支持 还原 / 删除 / 仅记录。键模式用 `*` 匹配子键层。 |

其他特性：

- **运行模式**：主页显示当前是「用户模式」还是「服务模式」。安装 / 重启 / 卸载服务的入口都在「设置 → 运行模式」中，默认不引导安装；其中「重启服务」在两种模式下都可用。
- **启动即连接**：打开界面会自动连接后台服务——已安装服务则进入服务模式，未安装则以用户模式运行，无需管理员权限。用户模式下引擎在后台静默运行，并随托盘退出而结束。
- **主页统计**：显示本次服务启动以来的「已记录 / 已拦截 / 拦截失败」数量。
- **主题**：支持亮色 / 暗色，默认跟随系统。
- **开机自启**：可随系统启动，并可选择「开机后隐藏到托盘」。
- **版本更新**：可开启检查更新，按天 / 周 / 月检查 GitHub 发行版。
- 规则支持 `Monitor`（事件实时触发）、`Schedule`（轮询）和 `Manual` 三种模式，可限定生效的操作系统范围。
- 规则冲突检测（同一目标被多条规则命中时给出提示）。
- 规则导入 / 导出为可读 JSON。
- 变更日志，可设置保留天数。
- 控制台模式，便于免管理员快速体验。

![EverDefault 主界面](https://github.com/user-attachments/assets/b4e769cc-e278-49c4-9781-6edf1da86534)

> 主界面：左侧为规则分类，右侧为列表与统计。

## 快速开始

1. 到 [Releases](https://github.com/BUGJI/EverDefault/releases/latest) 下载 `EverDefault-Setup-<版本>.exe` 并运行：安装程序会自动创建并启动后台服务（需要管理员权限）。
   已经自行构建过的话（见下方「构建」），也可以直接双击 `dist\EverDefault.App.exe`——未安装服务时会以用户模式运行，无需管理员权限，也无需安装。
2. 主页显示当前运行模式，以及本次启动以来的「已记录 / 已拦截 / 拦截失败」统计。
3. 到左侧「规则」分类点『新建』创建规则，或用『测试规则』先跑通流程。
4. 用 `regedit` 改动被保护的键，几秒后到「日志」页即可看到记录与处理结果。

> 也可在 `dist\` 中双击 `run-console.cmd` 手动以控制台模式启动服务。

## 系统要求

- **操作系统**：Windows 10 / 11（Win7 / Win8 仅支持部分功能，见[已知限制](#已知限制)）。
- **运行时**：.NET Framework 4.8。Win10 1903+ / Win11 通常已自带；更早的系统需先手动安装。
- **权限**：用户模式无需管理员；安装系统服务（守护 HKLM / 所有用户）需要管理员权限。
- **磁盘**：程序数据位于 `%ProgramData%\EverDefault`。

## 设置

设置页可调整：

- **运行模式**：默认「用户模式」（随登录启动、仅守护当前用户）。如需守护系统级(HKLM)或所有用户，可在此安装「服务模式」，并支持重启 / 卸载服务。
- **启用监控**：总开关。
- **随系统启动（开机自启）**：登录后自动启动托盘程序。
- **开机后隐藏到托盘**：自启动时不弹出窗口，直接最小化到托盘。
- **主题**：亮色 / 暗色 / 跟随系统。
- **系统版本覆盖**、**日志保留天数**、**目标用户范围**。
- **检查版本更新**：按天 / 周 / 月检查新版本。

## 杀毒软件提示

本工具会「监控并回写注册表」，这类行为容易被杀软误报为可疑。请将以下文件 / 目录加入白名单：

- `EverDefault.Service.exe`
- `EverDefault.App.exe`
- `%ProgramData%\EverDefault`

## 正式安装为开机自启服务

**方式一：安装包**

到 [Releases](https://github.com/BUGJI/EverDefault/releases/latest) 下载 `EverDefault-Setup-<版本>.exe`；自行构建则运行 `build-installer.cmd` 产出的 `installer\Output\EverDefault-Setup-*.exe`。安装程序会自动创建并启动服务。

**方式二：脚本**

1. 在发行包（或仓库内 `packaging\` 目录）中找到 `install-service.cmd`，右键 → 以管理员身份运行。
2. 运行 `EverDefault.App.exe`，它会自动连接服务；需要开机自启可在「设置」里开启。

卸载：以管理员身份运行发行包中的 `uninstall-service.cmd`，或使用安装包自带卸载程序。

## 规则导入 / 导出

- 工具栏「导出」：把当前所有规则保存为 JSON，格式为 `{Version, ExportedUtc, Rules:[...]}`，用 `Kind` 字段区分模块（`DefaultApp` / `NameSpace` / `CustomRegistry`），便于手工编辑。
- 工具栏「导入」：从 JSON 读取规则并**追加**到现有规则（自动分配新 ID，不覆盖已有规则），可放心多次导入。
- 参考样例：`packaging/sample-rules.json`。

新建规则时的「选择…」按钮可直接从注册表浏览选择目标（文件类型 / ProgId、NameSpace 键、任意注册表值）。

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

**改了注册表，日志里却没有记录？**
确认「设置 → 启用监控」已开启；规则模式为 `Schedule` 时需等一个轮询周期，`Manual` 模式不会自动触发。

**默认应用设置后又被系统改回去了？**
本工具会持续回写，但某些系统更新会重置 `UserChoice`。若长期无效，通常是 Win11 新版本更换了哈希算法，请到 [Issues](https://github.com/BUGJI/EverDefault/issues) 附上系统版本反馈。

**用户模式下能守护 HKLM 吗？**
不能。守护系统级(HKLM)或所有用户，需在「设置 → 运行模式」中安装服务模式。

**数据存在哪里？如何备份？**
见[数据目录](#数据目录)，直接复制 `%ProgramData%\EverDefault` 即可完成备份。

**杀软报警或文件被删除？**
见[杀毒软件提示](#杀毒软件提示)，将相关文件 / 目录加入白名单。

## 已知限制

- `UserChoice` 哈希算法在 Win10 / Win11 验证通过；Win7 / Win8 走传统关联路径。Win11 某些新版本可能改用另一套算法，若无效请反馈。
- 仅处理已加载的用户配置单元（已注销用户不处理）。
- 服务在需要接管受保护键时，会把键的属主改为 `SYSTEM`（已为用户补回完全控制）。
- `OpenWithProgids` 项以 `REG_SZ` 空值写入，功能可用，但类型与系统不完全一致。

## 更新日志

各版本的新增与修复见 [CHANGELOG.md](CHANGELOG.md)，每条发布说明的完整原文见 [Releases](https://github.com/BUGJI/EverDefault/releases)。

---

# 技术说明

## 项目结构

```
src/
  EverDefault.Core/         模型、枚举、规则/设置定义、存储接口、规则冲突检测
  EverDefault.Registry/     注册表访问、快照、UserChoice 哈希、命名空间扫描、注册表监控
  EverDefault.Persistence/  JSON 存储实现（规则 / 基线 / 日志 / 设置）
  EverDefault.Serialization/ 共享 JSON 序列化配置（字典类型名、白名单绑定器）
  EverDefault.Ipc/          命名管道协议、分帧、序列化、规则编解码、管道安全
  EverDefault.Service/      监控引擎宿主、调度器、各规则模块处理器
  EverDefault.App/          WPF 托盘界面（主页 / 规则 / 日志 / 设置 / 关于）
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
