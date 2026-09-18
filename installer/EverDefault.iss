; EverDefault installer script (Inno Setup 6)
; Compile with build-installer.cmd (which builds dist\ first).

#define MyAppName "EverDefault"
#define MyAppVersion "1.1.1"
#define MyAppExeName "EverDefault.App.exe"
#define MyServiceExeName "EverDefault.Service.exe"
#define MyServiceName "EverDefault"

[Setup]
AppId={{6B2D5F3A-9C41-4E7B-A8D2-1F0C3E5A7B90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=EverDefault
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=EverDefault-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\EverDefault.App\Assets\logo.ico

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{#MyAppName}\使用说明"; Filename: "{app}\README.txt"
Name: "{autoprograms}\{#MyAppName}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure StopServiceAndApp;
var
  ResultCode: Integer;
begin
  { 关闭托盘界面，再停止并删除旧服务，避免升级时文件被占用 }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im {#MyAppExeName} /f', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#MyServiceName}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#MyServiceName}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure InstallService;
var
  ResultCode: Integer;
  BinPath: String;
begin
  BinPath := ExpandConstant('{app}\{#MyServiceExeName}');
  Exec(ExpandConstant('{sys}\sc.exe'),
       'create {#MyServiceName} binPath= "' + BinPath + '" start= auto DisplayName= "EverDefault Registry Guard"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'),
       'description {#MyServiceName} "EverDefault 注册表守护：监控并还原默认应用、名称空间与自定义注册表项。"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'),
       'failure {#MyServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#MyServiceName}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure AskRemoveData;
var
  DataDir: String;
begin
  DataDir := ExpandConstant('{commonappdata}\EverDefault');
  if DirExists(DataDir) then
  begin
    if MsgBox('是否一并删除用户数据？' + #13#10 + DataDir + #13#10 + #13#10 +
              '选择“否”会保留规则、基线、日志和设置，方便日后重装。',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataDir, True, True, True);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopServiceAndApp;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallService;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopServiceAndApp;
    AskRemoveData;
  end;
end;
