; Assistant d'installation du launcher COTK.
; Compile par le workflow GitHub Actions :
;   ISCC.exe /DAppVersion=x.y.z installer\cotk.iss

#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

[Setup]
AppId={{7E2A9F41-6C3D-4B58-9A0E-52C4F10D81A2}
AppName=COTK Launcher
AppVersion={#AppVersion}
AppPublisher=COTK - Crown of the King
DefaultDirName={localappdata}\COTK
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=InstallerOut
OutputBaseFilename=cotk-launcher-setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
UninstallFilesDir={localappdata}\COTK\unins
UninstallDisplayName=COTK Launcher

[Messages]
WelcomeLabel2=Ceci va installer le launcher COTK - Crown of the King.%n%nLe launcher telecharge et met a jour le client du jeu, puis vous connecte aux serveurs.%n%nInstallation recommandee pour votre utilisateur : aucune droits administrateur requis.

[Files]
Source: "..\stage\COTK.Launcher.*"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\stage\fonts\*"; DestDir: "{app}\fonts"; Flags: ignoreversion recursesubdirs
Source: "..\stage\ClientConfig.example.ini"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\stage\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\stage\JOUE.bat"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\COTK"; Filename: "{app}\COTK.Launcher.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\COTK"; Filename: "{app}\COTK.Launcher.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\COTK.Launcher.exe"; Description: "Lancer COTK maintenant"; Flags: nowait postinstall skipifsilent

; Note : la desinstallation ne supprime que les fichiers du launcher.
; Le client telecharge (dossier client\) et les reglages (%APPDATA%\COTK)
; sont conserves comme donnees joueur.
