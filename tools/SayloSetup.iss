[Setup]
AppId=Saylo.Desktop
AppName=Saylo
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Saylo
DefaultGroupName=Saylo
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
OutputBaseFilename=SayloSetup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\Saylo.Desktop.exe

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Saylo"; Filename: "{app}\Saylo.Desktop.exe"

[Run]
Filename: "{app}\Saylo.Desktop.exe"; Description: "Iniciar Saylo"; Flags: nowait postinstall skipifsilent
