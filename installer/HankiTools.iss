#define AppName "Hanki Tools"
#define AppPublisher "Hanki Tools"
#define AppURL "https://github.com/hankitools/hankitools-windows"

[Setup]
AppId={{C465139A-D8B4-4555-BE74-AB83F5C0CB94}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={code:GetDefaultDirName}
DefaultGroupName=Hanki Tools
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
SetupIconFile={#ProjectRoot}\src\IgezziGuard\Brand\hanki.ico
UninstallDisplayName=Hanki Tools {#AppVersion}
UninstallDisplayIcon={app}\HankiTools.exe
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#FileVersion}
VersionInfoProductVersion={#FileVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Hanki Tools Windows x64 setup
VersionInfoProductName=Hanki Tools

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Hanki Tools"; Filename: "{app}\HankiTools.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Hanki Tools"; Filename: "{app}\HankiTools.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Code]
function GetDefaultDirName(Param: string): string;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{autopf}\Hanki Tools')
  else
    Result := ExpandConstant('{localappdata}\Programs\Hanki Tools');
end;
