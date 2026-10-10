; Inno Setup script for MarkdownBlaze (per-user install; registers the .md/.markdown/.mdx documents, folders and markdown:// links).
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
; So `MarkdownBlaze` can be run from any terminal, the install dir is added to the user PATH.
ChangesEnvironment=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Icons]
Name: "{group}\MarkdownBlaze"; Filename: "{app}\{#MyAppExe}"
Name: "{userdesktop}\MarkdownBlaze"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; Everything the app can register for itself (Settings -> Windows integration, or --register) is
; registered here, under the same keys, so the installer and the app never fight over them
; (ShellIntegration.cs is the other half of this list, and ShellIntegrationTests checks they agree).
; All per-user. Every key has uninsdeletekey / uninsdeletevalue, so that uninstalling leaves no
; "Open with" entry or markdown:// handler pointing at a program that is gone.

; The marker that tells the app the installer did this, so its Settings page says "Managed by the
; installer" and offers no button. The key goes too, when nothing else is in it.
Root: HKCU; Subkey: "Software\MarkdownBlaze"; ValueType: string; ValueName: "InstallSource"; ValueData: "installer"; Flags: uninsdeletevalue uninsdeletekeyifempty

; Documents: a ProgId, offered for .md / .markdown / .mdx under OpenWithProgids. That makes MarkdownBlaze
; a choice in "Open with" and in Settings -> Default apps (where the user, not the installer, decides).
Root: HKCU; Subkey: "Software\Classes\MarkdownBlaze.Document"; ValueType: string; ValueName: ""; ValueData: "Markdown Document"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\MarkdownBlaze.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"",0"
Root: HKCU; Subkey: "Software\Classes\MarkdownBlaze.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" ""%1"""
; (These three only make sure that an extension nobody else owns, like .mdx, is not left behind as an
; empty key; Inno removes them after the values below, which are listed after them.)
Root: HKCU; Subkey: "Software\Classes\.md"; ValueType: none; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.markdown"; ValueType: none; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.mdx"; ValueType: none; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "MarkdownBlaze.Document"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "MarkdownBlaze.Document"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.mdx\OpenWithProgids"; ValueType: string; ValueName: "MarkdownBlaze.Document"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty

; Folders have no default app, so they get a verb. %V is the folder clicked, or the one whose empty
; space was clicked.
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MarkdownBlaze"; ValueType: string; ValueName: ""; ValueData: "Open with MarkdownBlaze"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MarkdownBlaze"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExe}"",0"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MarkdownBlaze\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" ""%V"""
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MarkdownBlaze"; ValueType: string; ValueName: ""; ValueData: "Open with MarkdownBlaze"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MarkdownBlaze"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExe}"",0"
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MarkdownBlaze\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" ""%V"""

; The markdown:// protocol: links from other programs open the document here.
Root: HKCU; Subkey: "Software\Classes\markdown"; ValueType: string; ValueName: ""; ValueData: "URL:markdown document"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\markdown"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\markdown\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"",0"
Root: HKCU; Subkey: "Software\Classes\markdown\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" ""%1"""

; Older versions of the app registered "Open with" as a verb on each extension (a menu entry that is
; not a default-app candidate), on every launch, and uninstalling did not remove it. Clear those out.
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\MarkdownBlaze"; ValueType: none; Flags: deletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.markdown\shell\MarkdownBlaze"; ValueType: none; Flags: deletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mdx\shell\MarkdownBlaze"; ValueType: none; Flags: deletekey

; Global alias: add the install dir to the user PATH so `MarkdownBlaze` works from any terminal.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Check: NeedsAddPath(ExpandConstant('{app}'))

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch MarkdownBlaze"; Flags: nowait postinstall skipifsilent

[Code]
{ True when the given dir is not already present in the user PATH (avoids duplicate entries). }
function NeedsAddPath(Param: string): Boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;

{ Remove the install dir from the user PATH on uninstall. }
procedure RemovePath(Param: string);
var
  OrigPath: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath) then
    exit;
  OrigPath := ';' + OrigPath + ';';
  P := Pos(';' + Uppercase(Param) + ';', Uppercase(OrigPath));
  if P = 0 then exit;
  Delete(OrigPath, P, Length(Param) + 1);
  if (Length(OrigPath) > 0) and (OrigPath[1] = ';') then Delete(OrigPath, 1, 1);
  if (Length(OrigPath) > 0) and (OrigPath[Length(OrigPath)] = ';') then Delete(OrigPath, Length(OrigPath), 1);
  RegWriteExpandStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemovePath(ExpandConstant('{app}'));
end;
