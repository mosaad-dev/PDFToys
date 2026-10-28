#define MyAppName "PDFToys"
#define VersionFile FileOpen(AddBackslash(SourcePath) + "..\VERSION")
#define MyAppVersion Trim(FileRead(VersionFile))
#expr FileClose(VersionFile)
#define MyAppPublisher "PDFToys"
#define MyAppExeName "PDFToys.App.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
UninstallDisplayIcon={app}\{#MyAppExeName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=PDFToys-{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"

[Files]
Source: "..\artifacts\package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\scripts\register-shell-extension.ps1"" -DllPath ""{app}\PDFToys.ShellExtension.dll"""; StatusMsg: "Registering shell extension..."; Flags: runhidden 64bit; AfterInstall: CheckShellRegistration

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\scripts\register-shell-extension.ps1"" -Action unregister -DllPath ""{app}\PDFToys.ShellExtension.dll"""; Flags: runhidden 64bit; RunOnceId: "UnregisterPDFToysShellExtension"

[Code]
procedure CheckShellRegistration;
var
  RegisteredPath: String;
begin
  if (not RegQueryStringValue(HKLM64, 'SOFTWARE\Classes\CLSID\{2a97e713-bd5f-45c4-a410-e57a3c3d86a4}\InprocServer32', '', RegisteredPath)) or
     (CompareText(RegisteredPath, ExpandConstant('{app}\PDFToys.ShellExtension.dll')) <> 0) then
    RaiseException('PDFToys files were installed, but Explorer registration failed. Run the installer again as administrator.');
end;
