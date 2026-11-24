[Setup]
AppId=LigPrint.Offline.x64
AppName=LigPrint
AppVersion=1.3.0
DefaultDirName={localappdata}\Programs\LigPrint
DefaultGroupName=LigPrint
PrivilegesRequired=lowest
ChangesAssociations=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
SetupIconFile=src\sunflower.ico
UninstallDisplayIcon={app}\LigPrint.exe
OutputDir=dist
OutputBaseFilename=LigPrint-Setup-x64
Compression=lzma2
WizardStyle=modern
DisableProgramGroupPage=yes
[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
[Files]
Source: "dist\LigPrint.exe"; DestDir: "{app}"; Flags: ignoreversion
[Registry]
Root: HKCU; Subkey: "Software\Classes\LigPrint.Template"; ValueType: string; ValueData: "LigPrint 标签模板"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\LigPrint.Template\DefaultIcon"; ValueType: string; ValueData: """{app}\LigPrint.exe"",0"
Root: HKCU; Subkey: "Software\Classes\LigPrint.Template\shell\open\command"; ValueType: string; ValueData: """{app}\LigPrint.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\.lig\OpenWithProgids"; ValueType: string; ValueName: "LigPrint.Template"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.lig"; ValueType: string; ValueData: "LigPrint.Template"; Flags: createvalueifdoesntexist uninsdeletekeyifempty
[Icons]
Name: "{group}\LigPrint"; Filename: "{app}\LigPrint.exe"
Name: "{userdesktop}\LigPrint"; Filename: "{app}\LigPrint.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "创建桌面快捷方式"
[Code]
function InitializeSetup(): Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 394802);
  if not Result then MsgBox('请先离线安装 .NET Framework 4.6.2 或更新版本。', mbError, MB_OK);
end;




procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Current: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Classes\.lig', '', Current) then
      if Current = 'LigPrint.Template' then
        RegDeleteValue(HKCU, 'Software\Classes\.lig', '');
end;



