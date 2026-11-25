; Altyazı+ kurulum dosyası (Inno Setup 6). CI'da derlenir: iscc /DMyVersion=x.y.z installer\AltyaziPlus.iss
; Yönetici izni istemez: kullanıcının kendi klasörüne (%LOCALAPPDATA%\Programs\AltyaziPlus) kurulur.
; Program içindeki "Şimdi güncelle" bu kurulumu sessiz çalıştırır (/VERYSILENT); ayarlar ve eşlemeler korunur
; (onlar %LOCALAPPDATA%\PS5TurkceAltyazi'de durur).

#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif

[Setup]
AppId={{9522C619-FBFA-456A-B9C0-1E09F2D7E667}
AppName=Altyazı+
AppVersion={#MyVersion}
AppVerName=Altyazı+ {#MyVersion}
AppPublisher=Burak Topcu
AppPublisherURL=https://github.com/topcu53/PS5TurkceAltyazi
DefaultDirName={localappdata}\Programs\AltyaziPlus
DefaultGroupName=Altyazı+
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=..\publish
OutputBaseFilename=AltyaziPlus-Setup
SetupIconFile=..\src\Ps5Altyazi\Resources\app.ico
UninstallDisplayIcon={app}\AltyaziPlus.exe
UninstallDisplayName=Altyazı+
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Güncellemede çalışan Altyazı+ kapatılır (Windows Yeniden Başlatma Yöneticisi)
CloseApplications=force
RestartApplications=no

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol koy"; GroupDescription: "Kısayollar:"

[Files]
Source: "..\publish\AltyaziPlus.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Altyazı+"; Filename: "{app}\AltyaziPlus.exe"
Name: "{autodesktop}\Altyazı+"; Filename: "{app}\AltyaziPlus.exe"; Tasks: desktopicon

[Run]
; Kurulum (ya da sessiz güncelleme) bitince program açılır.
Filename: "{app}\AltyaziPlus.exe"; Description: "Altyazı+'ı şimdi aç"; Flags: nowait postinstall
