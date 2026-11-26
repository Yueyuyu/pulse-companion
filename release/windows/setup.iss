; Run through build-installer.ps1. There is no wildcard source or private renderer fallback.
[Setup]
AppId=PulseCompanion.PublicBrand
AppName=Pulse Companion
AppVersion={#ProductVersion}
AppPublisher=Pulse Companion contributors
AppPublisherURL=https://github.com/Yueyuyu/pulse-companion
AppSupportURL=https://github.com/Yueyuyu/pulse-companion/issues
DefaultDirName={localappdata}\CodexDesktopCompanion\pulse\{#DeploymentId}
DefaultGroupName=Pulse Companion
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
Compression=lzma2/normal
SolidCompression=yes
CloseApplications=no
RestartApplications=no
WizardStyle=modern
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\PulseWebPreview.exe
UninstallDisplayName=Pulse Companion (Windows x64)
OutputBaseFilename=Pulse-Companion-{#ProductVersion}-Windows-x64-Setup
VersionInfoVersion={#ProductVersion}.0
VersionInfoDescription=Pulse Companion Windows x64 Setup
InfoBeforeFile=installation-info.txt

[Files]
#include PayloadInclude

[Icons]
Name: "{group}\Pulse Companion"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File ""{app}\install-tools\public-lifecycle.ps1"" -Action Start"; WorkingDir: "{app}"; IconFilename: "{app}\PulseWebPreview.exe"; Flags: runminimized
Name: "{group}\卸载 Pulse Companion"; Filename: "{uninstallexe}"

[Messages]
WizardInfoBefore=安装之前：数据与授权
InfoBeforeLabel=请阅读公开品牌版的读取范围和停止方式，再继续安装。
ButtonNext=下一步(&N) >
ButtonBack=< 上一步(&B)
ButtonCancel=取消
ButtonInstall=安装(&I)
ButtonFinish=完成(&F)

[Code]
var
  Quiesced, Activated, ActivationFailed: Boolean;

procedure LogRepairProof(const S: String; const Error, FirstLine: Boolean);
var
  Stage: String;
begin
  { 日志只接收固定检查名，不写子进程原始输出、用户路径或账户信息。 }
  if Pos('pulse-repair-proof:', S) <> 1 then exit;
  Stage := Copy(S, Length('pulse-repair-proof:') + 1, Length(S));
  if (Stage <> '') and (Pos('|', Stage) = 0) and
      (Pos('|' + Stage + '|', '|registration|registration-location|uninstaller-command|uninstaller-path|uninstaller-files|payload|tool-size|tool-hash|matched|') <> 0) then
    Log('Pulse repair proof: ' + Stage);
end;

function RunHelper(Script, Action: String): Integer;
var
  Args: String;
  Code: Integer;
begin
  Args := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Script + '" -Action ' + Action;
  if Action = 'Activate' then Args := Args + ' -ProductVersion {#ProductVersion}';
  if ExecAndLogOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code, @LogRepairProof)
  then Result := Code else Result := -1;
  Log('Pulse lifecycle: ' + Action + ' -> ' + IntToStr(Result));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Script: String;
begin
  Result := '';
  if CompareText(RemoveBackslashUnlessRoot(ExpandConstant('{app}')),
      ExpandConstant('{localappdata}\CodexDesktopCompanion\pulse\{#DeploymentId}')) <> 0 then begin
    Result := '公开版仅支持固定的当前用户安装路径。'; exit;
  end;
  ExtractTemporaryFile('public-package.json');
  ExtractTemporaryFile('common.ps1');
  ExtractTemporaryFile('pulse-background-task.ps1');
  ExtractTemporaryFile('public-lifecycle.ps1');
  Script := ExpandConstant('{tmp}\public-lifecycle.ps1');
  Code := RunHelper(Script, 'Check');
  case Code of
    0: ;
    11: Log('Repairing an incomplete, positively identified installation of this public package.');
    21: Result := '已发现活动的本机专用版或旧版，公开版不会覆盖。可以继续使用原版；如要换版，请先另存原部署、启动备份和个人设置，再使用原版卸载入口。';
    23: Result := '需要 Microsoft Edge WebView2 Evergreen Runtime。请从 https://developer.microsoft.com/microsoft-edge/webview2/ 安装后重试。';
    else Result := '安装目录、已有安装记录或同名计划任务无法安全验证；未覆盖。';
  end;
  if Result <> '' then exit;
  Quiesced := True;
  if RunHelper(Script, 'Quiesce') <> 0 then Result := '无法安全停止本公开版的活动副本，安装已停止。';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    { Inno 已在 PerformInstall 中提交卸载日志，并会捕获 ssPostInstall 异常。
      不用 RaiseException 声称回滚；保留真实卸载登记，让同一包能安全重试。 }
    ActivationFailed := True;
    Code := RunHelper(ExpandConstant('{app}\install-tools\public-lifecycle.ps1'), 'Activate');
    if (Code <> 0) and (Code <> 10) then begin
      Log('Pulse files installed but activation failed; retry the same setup or use its registered uninstaller. Exit code 30.');
      exit;
    end;
    ActivationFailed := False;
    Activated := True;
    if Code = 10 then begin
      Log('Pulse installed; immediate launch deferred. Use the Start menu or log on again.');
      if not WizardSilent then MsgBox('安装已完成，但当前会话没有立即启动。可以在开始菜单打开 Pulse Companion，或等下次登录。', mbInformation, MB_OK);
    end;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if ActivationFailed then Result := 30 else Result := 0;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and ActivationFailed then begin
    WizardForm.FinishedHeadingLabel.Caption := 'Pulse Companion 未能完成激活';
    WizardForm.FinishedLabel.Caption := '程序文件已安装，但后台设置未完成。请检查当前用户的任务计划和安装目录权限，再运行同一安装包重试；也可在 Windows「已安装的应用」中卸载。个人设置和备份不会被删除。';
  end;
end;

procedure DeinitializeSetup;
begin
  if Quiesced and not Activated then
    RunHelper(ExpandConstant('{tmp}\public-lifecycle.ps1'), 'Recover');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { usUninstall runs after the user has confirmed, before owned files are removed. }
  if CurUninstallStep = usUninstall then begin
    if RunHelper(ExpandConstant('{app}\install-tools\public-lifecycle.ps1'), 'Uninstall') <> 0 then begin
      SuppressibleMsgBox('未能安全验证或移除本版后台入口，卸载已停止。', mbError, MB_OK, IDOK);
      Abort;
    end;
  end;
end;
