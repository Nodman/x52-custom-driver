#define MyAppName "Ærakon x52 driver"
; Version comes from the VERSION file in the repository root (single source of truth)
#define VersionFile FileOpen(AddBackslash(SourcePath) + "VERSION")
#define MyAppVersion Trim(FileRead(VersionFile))
#expr FileClose(VersionFile)
#define MyAppPublisher "Ærakon"
#define MyAppURL "https://github.com/Nodman/x52-custom-driver"
#define MyAppExeName "X52.CustomDriver.App.exe"
#define BuildPath "X52.CustomDriver.App\bin\Release\net9.0-windows\win-x64\publish"
#define ConsoleBuildPath "X52.CustomDriver.Console\bin\Release\net9.0-windows\win-x64\publish"

[Setup]
; Same name as the driver's single-instance mutex: setup asks to close a running driver
; instead of failing with "file in use" when it sits in the tray
AppMutex=AerakonX52Driver
AppId={{D3F7D5C2-4E8F-4B9A-9C7D-2E5F8A1B3C4D}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=InstallerOutput
OutputBaseFilename=AerakonX52Driver_Setup_v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=X52.CustomDriver.App\app.ico
UninstallDisplayIcon={app}\app.ico

[Languages]
; Instructions follow the language picked at the start of setup
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "installer\INSTRUCTIONS_en.txt"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"; InfoBeforeFile: "installer\INSTRUCTIONS_uk.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoBeforeFile: "installer\INSTRUCTIONS_es.txt"

[CustomMessages]
english.VJoyMissing=vJoy is not installed.%n%nThis driver sends your X52 to games through the vJoy virtual joystick, so vJoy is required.%n%nOpen the vJoy download page now?%nInstall vJoy, then start the driver – it will offer to set vJoy up for the X52.
ukrainian.VJoyMissing=vJoy не встановлено.%n%nЦей драйвер передає X52 у гри через віртуальний джойстик vJoy, тому vJoy обов'язковий.%n%nВідкрити сторінку завантаження vJoy зараз?%nВстановіть vJoy, потім запустіть драйвер – він запропонує налаштувати vJoy для X52.
english.UnhideX52=The driver hid your X52 from other programs (HidHide).%n%nTo make it visible to games again, Windows will now ask for admin permission once.
ukrainian.UnhideX52=Драйвер приховав ваш X52 від інших програм (HidHide).%n%nЩоб ігри знову бачили X52, Windows зараз один раз попросить дозвіл адміністратора.
spanish.UnhideX52=El driver ocultó tu X52 a otros programas (HidHide).%n%nPara que los juegos vuelvan a verlo, Windows pedirá ahora permiso de administrador una vez.
english.UnhideX52Failed=The X52 could not be made visible again automatically.%n%nOpen HidHide Configuration Client and remove the X52 from the "Applications" / "Devices" lists, or reinstall the driver and use SETTINGS → SHOW REAL X52 before uninstalling.
ukrainian.UnhideX52Failed=Не вдалося автоматично знову зробити X52 видимим.%n%nВідкрийте HidHide Configuration Client і приберіть X52 зі списку пристроїв, або перевстановіть драйвер і натисніть SETTINGS → SHOW REAL X52 перед видаленням.
spanish.UnhideX52Failed=No se pudo volver a mostrar el X52 automáticamente.%n%nAbre HidHide Configuration Client y quita el X52 de la lista de dispositivos, o reinstala el driver y usa SETTINGS → SHOW REAL X52 antes de desinstalar.
spanish.VJoyMissing=vJoy no está instalado.%n%nEste driver envía el X52 a los juegos a través del joystick virtual vJoy, así que vJoy es imprescindible.%n%n¿Abrir ahora la página de descarga de vJoy?%nInstala vJoy y luego abre el driver: te ofrecerá configurarlo para el X52.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BuildPath}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildPath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; MANUAL DLL PATCH FIX: Force copy vJoyInterface from x64 folder to ROOT so Wrapper finds it
Source: "{#BuildPath}\x64\vJoyInterface.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ConsoleBuildPath}\*"; DestDir: "{app}\Diagnostics"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files
Source: "X52.CustomDriver.App\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_1234&PID_BEAD"; ValueType: string; ValueName: "OEMName"; ValueData: "Ærakon X52 Virtual Joystick"; Flags: uninsdeletekey

[Code]
const
  VJoyDownloadUrl = 'https://github.com/jshafer817/vJoy/releases/latest';

function VJoyInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\vjoy') or
            FileExists(ExpandConstant('{commonpf}\vJoy\x64\vJoyInterface.dll'));
  if not Result and IsWin64 then
    Result := FileExists(ExpandConstant('{commonpf64}\vJoy\x64\vJoyInterface.dll'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  // After installing: make sure vJoy is there, otherwise offer the download page
  if (CurStep = ssPostInstall) and (not WizardSilent) and (not VJoyInstalled) then
  begin
    if MsgBox(CustomMessage('VJoyMissing'), mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', VJoyDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

function DriverHidTheX52(): Boolean;
var
  Json: AnsiString;
begin
  Result := False;
  if LoadStringFromFile(ExpandConstant('{localappdata}\AerakonX52Driver\settings.json'), Json) then
    Result := Pos('"HideRealX52": true', Json) > 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  // Before the files are removed: if the driver hid the X52 with HidHide, un-hide it, otherwise
  // the stick would stay invisible to every game after the driver is gone.
  if (CurUninstallStep = usUninstall) and DriverHidTheX52() then
  begin
    if not UninstallSilent then
      MsgBox(CustomMessage('UnhideX52'), mbInformation, MB_OK);
    if (not Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--hidhide uninstall-request', '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
    begin
      if not UninstallSilent then
        MsgBox(CustomMessage('UnhideX52Failed'), mbError, MB_OK);
    end;
  end;
end;
