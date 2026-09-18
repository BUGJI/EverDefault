EverDefault - 注册表守护工具
================================

功能
----
1. 默认应用守护：为选定的文件类型锁定默认程序，被其他软件改动后自动还原。
2. This PC 命名空间：按规则检测并删除自定义命名空间项。
3. 自定义注册表：按基线或期望值监控注册表，支持还原/删除/仅记录。

组件
----
- EverDefault.Service.exe   后台服务（监控引擎，支持 HKLM 与各用户 HKCU）
- EverDefault.App.exe       托盘 + 图形界面（通过命名管道连接服务）

快速开始（免管理员，体验用）
----------------------------
1. 双击 run-console.cmd 启动服务（保持窗口打开）。
2. 双击 EverDefault.App.exe 打开界面，右上角应显示服务状态。
3. 点“测试规则”创建一条“仅记录”的自定义注册表规则。
4. 用 regedit 在 HKEY_CURRENT_USER\Software\EverDefaultTest 下新建或修改任意“值”，
   几秒后界面下方“变更日志”会出现记录。

规则导入 / 导出
---------------
- 工具栏「导出」：把当前所有规则保存为 JSON。格式为可读的 {Version, ExportedUtc, Rules:[...]}，
  用 "Kind" 字段区分模块（DefaultApp / NameSpace / CustomRegistry），便于手工编辑。
- 工具栏「导入」：从 JSON 读取规则并“追加”到现有规则（自动分配新 ID，不覆盖已有规则，
  因此可放心多次导入，重复项可手动删除）。
- 参考样例：同目录下 sample-rules.json。

新建规则时的“选择...”按钮
--------------------------
- 默认应用：可直接从注册表浏览选择“文件类型(.ext)”和“ProgId”。
- This PC 命名空间：可直接浏览到 NameSpace 键。
- 自定义注册表：点“从注册表选择...”，在树里定位键；选中右上角的某个“值”后
  点“使用此值”，会自动填入值名/类型/期望数据，并切到“精确匹配”。
- 键模式中 “*” 匹配子键层：例如 HKCU\Software\Foo\* 指 Foo 下的子键；
  要监控 Foo 自身的值，写 HKCU\Software\Foo（不带 \*）。


正式安装为开机自启服务（需要管理员）
------------------------------------
1. 右键 install-service.cmd -> 以管理员身份运行。
2. 运行 EverDefault.App.exe。可把它的快捷方式放入启动项。

卸载
----
- 右键 uninstall-service.cmd -> 以管理员身份运行。
- 用户数据位于 %ProgramData%\EverDefault（rules.json / baselines.json /
  changelog.json / settings.json），可手动删除。

杀毒软件提示
------------
本工具会“监控并回写注册表”，这类行为容易被杀软误报为可疑。
请将以下文件加入杀软/防火墙白名单：
- EverDefault.Service.exe
- EverDefault.App.exe
- 数据目录 %ProgramData%\EverDefault

已知限制
--------
- 默认应用（UserChoice）哈希算法在 Win10/11 验证通过；Win7/8 走传统关联路径。
  Win11 某些新版本可能使用另一套算法，若无效请反馈。
- 仅处理已加载的用户配置单元（已注销用户不处理）。
- 服务在需要接管受保护键时会把键的属主改为 SYSTEM（已为用户补回完全控制）。
- “打开方式(OpenWithProgids)”项以 REG_SZ 空值写入，功能可用但类型与系统不完全一致。
