#ifndef Publicacao
  #error Defina Publicacao com o caminho de artifacts/desktop.
#endif
#ifndef Destino
  #error Defina Destino com o caminho de saida em .tools.
#endif
#ifndef Versao
  #error Defina Versao conforme Directory.Build.props.
#endif
#ifndef IdentificadorApp
  #define IdentificadorApp "sonarFrame.Desktop"
#endif
#ifndef NomeApp
  #define NomeApp "sonarFrame"
#endif
#ifndef ExtensaoProjeto
  #define ExtensaoProjeto ".sonarframe"
#endif
#ifndef TipoProjeto
  #define TipoProjeto "sonarFrame.Projeto"
#endif
#ifndef MutexAplicativo
  #define MutexAplicativo "sonarFrame.Desktop.Active"
#endif
#define Executavel "SonarFrame.Desktop.exe"

[Setup]
AppId={#IdentificadorApp}
AppName={#NomeApp}
AppVersion={#Versao}
AppPublisher=SonarFrame
DefaultDirName={localappdata}\Programs\{#NomeApp}
DefaultGroupName={#NomeApp}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=classic
DisableWelcomePage=no
AllowNoIcons=yes
AppMutex={#MutexAplicativo}
CloseApplications=no
RestartApplications=no
ChangesAssociations=yes
UninstallDisplayIcon={app}\{#Executavel}
OutputDir={#Destino}
OutputBaseFilename=sonarFrame-{#Versao}-win-x64-setup
Compression=lzma2/normal
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "atalhodesktop"; Description: "Criar atalho na Área de &Trabalho"; GroupDescription: "Atalhos:"
Name: "associarprojetos"; Description: "Registrar sonarFrame para abrir projetos {#ExtensaoProjeto}"; GroupDescription: "Associação de arquivos:"; Flags: unchecked

[Files]
Source: "{#Publicacao}\*"; DestDir: "{app}"; Excludes: "*.sonarframe,*.recovery"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#NomeApp}"; Filename: "{app}\{#Executavel}"; WorkingDir: "{app}"
Name: "{group}\Desinstalar {#NomeApp}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#NomeApp}"; Filename: "{app}\{#Executavel}"; WorkingDir: "{app}"; Tasks: atalhodesktop

[Registry]
Root: HKCU; Subkey: "Software\Classes\{#ExtensaoProjeto}\OpenWithProgids"; ValueType: string; ValueName: "{#TipoProjeto}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: associarprojetos
Root: HKCU; Subkey: "Software\Classes\{#TipoProjeto}"; ValueType: string; ValueData: "Projeto sonarFrame"; Flags: uninsdeletekey; Tasks: associarprojetos
Root: HKCU; Subkey: "Software\Classes\{#TipoProjeto}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#Executavel}"",0"; Tasks: associarprojetos
Root: HKCU; Subkey: "Software\Classes\{#TipoProjeto}\shell\open\command"; ValueType: string; ValueData: """{app}\{#Executavel}"" ""%1"""; Tasks: associarprojetos

[Run]
Filename: "{app}\{#Executavel}"; Description: "Abrir sonarFrame"; Flags: nowait postinstall skipifsilent unchecked
