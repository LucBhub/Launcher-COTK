; Assistant d'installation du launcher COTK (Launcher Next).
; Contenu = publication self-contained identique au zip e2e-teste
; (COTK-Launcher-Next-<version>-win-x64.zip), racine COTK.Launcher.exe.
; Meme AppId que le setup historique 0.10.x : mise a niveau in place
; pour les joueurs qui ont l'ancien launcher installe.
;
;   ISCC.exe /DAppVersion=x.y.z /DStageDir=publish /DOutputPath=release installer\cotk-next.iss
;
; Empaquetage recommande : tools\package-setup.ps1 (garde-fous + empreinte).

#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
#ifndef StageDir
#define StageDir "..\stage"
#endif
#ifndef OutputPath
#define OutputPath "InstallerOut"
#endif

[Setup]
AppId={{7E2A9F41-6C3D-4B58-9A0E-52C4F10D81A2}
AppName=COTK Launcher
AppVersion={#AppVersion}
AppPublisher=COTK - Crown of the King
DefaultDirName={localappdata}\COTK
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputPath}
OutputBaseFilename=COTK-Launcher-Next-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
; Chemin litteral volontaire (comme le setup historique) : le desinstalleur
; reste a l'emplacement canonique meme si le joueur choisit un autre dossier.
UninstallFilesDir={localappdata}\COTK\unins
UninstallDisplayName=COTK Launcher

[Messages]
WelcomeLabel2=Ceci va installer le launcher COTK - Crown of the King.%n%nLe launcher telecharge et met a jour le client du jeu, puis vous connecte aux serveurs.%n%nInstallation recommandee pour votre utilisateur : aucune droits administrateur requis.

[Files]
; Toute la sortie publish self-contained (runtime .NET embarque inclus),
; avec les sous-dossiers : sans eux le launcher croit etre framework-dependent
; et Windows propose de telecharger le .NET Desktop Runtime.
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\COTK"; Filename: "{app}\COTK.Launcher.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\COTK"; Filename: "{app}\COTK.Launcher.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\COTK.Launcher.exe"; Description: "Lancer COTK maintenant"; Flags: nowait postinstall skipifsilent

; Note : la desinstallation ne supprime que les fichiers du launcher.
; Le client telecharge (dossier client\) et les reglages (%APPDATA%\COTK)
; sont conserves comme donnees joueur.
