# EverDefault

EverDefault 是一个 Windows 注册表守护工具：把你选定的东西「锁住」，一旦被其他软件改动，就自动改回来。

它由两部分组成：

- **EverDefault.Service** —— 后台 Windows 服务，负责监控引擎，支持 HKLM 与各用户的 HKCU。
- **EverDefault.App** —— 托盘 + WPF 图形界面，通过命名管道（Named Pipe）与服务通信。

## 功能

| 模块 | 说明 |
| --- | --- |
| 默认应用守护（DefaultApp） | 为指定文件类型（`.pdf`、`.ext` 等）锁定默认程序。会重算并回写 Win10/11 的 `UserChoice` 哈希，同时维护 `OpenWithProgids` 与旧式文件关联。 |
| This PC 命名空间（NameSpace） | 检测并删除「此电脑」下的自定义命名空间项，可按 Guid / 显示名 / 目标路径 / 正则匹配。 |
| 自定义注册表（CustomRegistry） | 按基线或期望值监控任意注册表键/值，支持 还原 / 删除 / 仅记录。键模式用 `*` 匹配子键层。 |

其他特性：

- 规则支持 `Monitor`（事件实时触发）、`Schedule`（轮询）和 `Manual` 三种模式，可限定生效的操作系统范围。
- 规则冲突检测（同一目标被多条规则命中时给出提示）。
- 规则导入 / 导出为可读 JSON。
- 变更日志，可设置保留天数。
- 控制台模式，便于免管理员快速体验。

<img width="750" height="500" alt="image" src="https://github.com/user-attachments/assets/b4e769cc-e278-49c4-9781-6edf1da86534" />

## 快速开始（功能验证）

1. 在 `dist\`（或 `packaging\`）中双击 `run-console.cmd`，以控制台模式启动服务，窗口保持打开。
2. 双击 `EverDefault.App.exe` 打开界面，右上角应显示服务状态。
3. 点「测试规则」创建一条「仅记录」的自定义注册表规则。
4. 用 `regedit` 在 `HKEY_CURRENT_USER\Software\EverDefaultTest` 下新建或修改任意值，几秒后界面「变更日志」会出现记录。

也可以直接在界面主页点「临时启动」，无需管理员权限。

## 杀毒软件提示

本工具会「监控并回写注册表」，这类行为容易被杀软误报为可疑。请将以下文件 / 目录加入白名单：

- `EverDefault.Service.exe`
- `EverDefault.App.exe`
- `%ProgramData%\EverDefault`

## 正式安装为开机自启服务

**方式一：安装包**

运行 `build-installer.cmd` 产出的 `installer\Output\EverDefault-Setup-*.exe`，安装程序会自动创建并启动服务。

**方式二：脚本**

1. 右键 `install-service.cmd` → 以管理员身份运行。
2. 运行 `EverDefault.App.exe`，可把它的快捷方式放入启动项。

卸载：以管理员身份运行 `uninstall-service.cmd`，或使用安装包自带卸载程序。

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

## 已知限制

- `UserChoice` 哈希算法在 Win10 / Win11 验证通过；Win7 / Win8 走传统关联路径。Win11 某些新版本可能改用另一套算法，若无效请反馈。
- 仅处理已加载的用户配置单元（已注销用户不处理）。
- 服务在需要接管受保护键时，会把键的属主改为 `SYSTEM`（已为用户补回完全控制）。
- `OpenWithProgids` 项以 `REG_SZ` 空值写入，功能可用，但类型与系统不完全一致。

---

# 技术说明

## 项目结构

```
src/
  EverDefault.Core/         模型、枚举、规则/设置定义、存储接口、规则冲突检测
  EverDefault.Registry/     注册表访问、快照、UserChoice 哈希、命名空间扫描、注册表监控
  EverDefault.Persistence/  JSON 存储实现（规则 / 基线 / 日志 / 设置）
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
