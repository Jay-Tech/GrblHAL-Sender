; GrblHAL Sender Inno Setup Script
; Usage: iscc /DAppVersion=1.0.0 /DArch=x64 /DPublishDir=..\..\publish\win-x64 GrblHALSender.iss

#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif

#ifndef Arch
  #define Arch "x64"
#endif

#ifndef PublishDir
  #define PublishDir "..\..\publish\win-" + Arch
#endif

[Setup]
AppId={{B8E3F2A1-4D5C-4E6F-8A9B-1C2D3E4F5A6B}
AppName=GrblHAL Sender
AppVersion={#AppVersion}
AppVerName=GrblHAL Sender {#AppVersion}
AppPublisher=Jay-Tech
AppPublisherURL=https://github.com/Jay-Tech/GrblHAL-Sender
DefaultDirName={autopf}\GrblHAL Sender
DefaultGroupName=GrblHAL Sender
OutputDir=..\..\artifacts
OutputBaseFilename=GrblHALSender-{#AppVersion}-win-{#Arch}-setup
Compression=lzma2/ultra64
SolidCompression=yes
SetupIconFile=..\..\icons\GHalSender.ico
UninstallDisplayIcon={app}\GrbLHALSender.Desktop.exe
WizardStyle=modern
PrivilegesRequired=admin
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GrblHAL Sender"; Filename: "{app}\GrbLHALSender.Desktop.exe"
Name: "{group}\{cm:UninstallProgram,GrblHAL Sender}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\GrblHAL Sender"; Filename: "{app}\GrbLHALSender.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GrbLHALSender.Desktop.exe"; Description: "{cm:LaunchProgram,GrblHAL Sender}"; Flags: nowait postinstall skipifsilent
