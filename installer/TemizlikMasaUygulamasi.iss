#define MyAppName "Temizlik ve Bakım Merkezi Professional"
#define MyAppVersion "3.1.3"
#define MyAppPublisher "Vedat Güldü"
#define MyAppExeName "TemizlikMasaUygulamasi.exe"
#define MyAppURL "https://github.com/vedatguldu/temizlik-bakim-merkezi-professional"
#define MyAppUpdatesURL "https://github.com/vedatguldu/temizlik-bakim-merkezi-professional/releases/latest"
#define SourcePublishDir "..\\publish\\win-x64"

[Setup]
AppId={{1D50C0B8-8DB3-44DA-B65D-77A03C2B11B0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppUpdatesURL}
DefaultDirName={autopf}\Temizlik Bakım Merkezi Professional
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=assets\wizard-side.bmp
WizardSmallImageFile=assets\wizard-small.bmp
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=no
DisableDirPage=auto
DisableReadyMemo=no
DisableProgramGroupPage=yes
OutputDir=..\artifacts\setup
OutputBaseFilename=TemizlikBakimMerkezi-Professional-v3_1_3-Setup
SetupIconFile=
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousAppDir=yes
UsePreviousLanguage=yes
UsePreviousTasks=yes
SetupLogging=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol oluştur"; GroupDescription: "Ek seçenekler:"; Flags: unchecked

[Files]
Source: "{#SourcePublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
