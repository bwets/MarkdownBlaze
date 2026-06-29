; Inno Setup script for MarkdownBlaze (per-user install, registers .md/.markdown association).
; Version + publish dir are passed on the command line by the release workflow:
;   ISCC /DMyAppVersion=1.0.5 /DPublishDir=<abs path to publish\win-x64> MarkdownBlaze.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\publish\win-x64"
#endif
#define MyAppName "MarkdownBlaze"
#define MyAppExe "MarkdownBlaze.exe"

[Setup]
AppId={{8B5E2A4C-6F1D-4E9A-9C3B-2D7A1F0E5B66}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Gregory Kieffer
AppPublisherURL=https://github.com/bwets/MarkdownBlaze
DefaultDirName={localappdata}\Programs\MarkdownBlaze
DefaultGroupName=MarkdownBlaze
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExe}
OutputBaseFilename=MarkdownBlaze-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ChangesAssociations=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Icons]
Name: "{group}\MarkdownBlaze"; Filename: "{app}\{#MyAppExe}"
Name: "{userdesktop}\MarkdownBlaze"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; ProgId + add MarkdownBlaze as a handler for .md / .markdown (per-user; user can set it as default).
Root: HKCU; Subkey: "Software\Classes\MarkdownBlaze.Document"; ValueType: string; ValueName: ""; ValueData: "Markdown Document"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\MarkdownBlaze.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExe},0"
Root: HKCU; Subkey: "Software\Classes\MarkdownBlaze.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "MarkdownBlaze.Document"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "MarkdownBlaze.Document"; ValueData: ""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch MarkdownBlaze"; Flags: nowait postinstall skipifsilent
