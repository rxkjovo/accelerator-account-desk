#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDirectory
  #error PublishDirectory is required
#endif
#ifndef RepositoryDirectory
  #error RepositoryDirectory is required
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory is required
#endif

[Setup]
AppId={{69BFC873-8DCB-4A68-9D7F-FB7E67A70EF9}
AppName=加速账户助手
AppVersion={#AppVersion}
AppVerName=加速账户助手 {#AppVersion}
AppPublisher=rxkjovo
AppPublisherURL=https://github.com/rxkjovo/accelerator-account-desk
AppSupportURL=https://github.com/rxkjovo/accelerator-account-desk/issues
AppUpdatesURL=https://github.com/rxkjovo/accelerator-account-desk/releases/latest
DefaultDirName={localappdata}\Programs\AcceleratorAccountDesk
DefaultGroupName=加速账户助手
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile={#RepositoryDirectory}\src\AcceleratorDesk.App\Assets\app.ico
UninstallDisplayIcon={app}\加速账户助手-{#AppVersion}\加速账户助手.exe
InfoBeforeFile={#RepositoryDirectory}\installer\安装提示.txt
OutputDir={#ReleaseDirectory}
OutputBaseFilename=AcceleratorAccountDesk-{#AppVersion}-Setup-win-x64
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#RepositoryDirectory}\installer\Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: checkedonce

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}\加速账户助手-{#AppVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\加速账户助手"; Filename: "{app}\加速账户助手-{#AppVersion}\加速账户助手.exe"; WorkingDir: "{app}\加速账户助手-{#AppVersion}"
Name: "{autodesktop}\加速账户助手"; Filename: "{app}\加速账户助手-{#AppVersion}\加速账户助手.exe"; WorkingDir: "{app}\加速账户助手-{#AppVersion}"; Tasks: desktopicon

[Run]
Filename: "{app}\加速账户助手-{#AppVersion}\加速账户助手.exe"; Description: "完成后启动加速账户助手"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; User data is stored separately and is deliberately absent from uninstall actions.
