; Inno Setup script for PrinceWM - builds PrinceWMSetup.exe
[Setup]
AppId={{8E0F7A12-DR1F-4FE8-B9A5-PrinceWM0000001}
AppName=PrinceWM
AppVersion=1.2.0
AppPublisher=prince
DefaultDirName={autopf}\PrinceWM
DefaultGroupName=PrinceWM
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\PrinceWM.exe
OutputBaseFilename=PrinceWMSetup
OutputDir=C:\Users\bakki\OneDrive\Desktop
SetupIconFile=icon.ico
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "C:\dev\PrinceWM\bin\Release\net9.0-windows10.0.26100.0\win-x64\publish\PrinceWM.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PrinceWM"; Filename: "{app}\PrinceWM.exe"
Name: "{autodesktop}\PrinceWM"; Filename: "{app}\PrinceWM.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PrinceWM.exe"; Description: "Launch PrinceWM now"; Flags: nowait postinstall skipifsilent shellexec
