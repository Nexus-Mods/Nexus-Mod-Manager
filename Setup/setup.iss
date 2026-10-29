// Define stuff we need to download/update/install

#define use_msi31
;#define use_msi45
#define use_dotnetfx45

// End

#define MyAppSetupName 'NMM'
#define MyExeName 'NexusClient.exe'
#define MyAppVersion '0.93.8'
#define SetupScriptVersion '0.7.1.1'
#define MyPublisher 'DuskDweller'

; Validate the existing Release output without downloading, building or running the helper.
; Missing/stale packages must stop installer compilation instead of silently disabling inspection.
#define LoadOrderVerifier AddBackslash(SourcePath) + "..\Support\LarianPackageInspector\Build.ps1"
#define LoadOrderPackage AddBackslash(SourcePath) + "..\Stage\Release\Tools\LoadOrder"
#if Exec(GetEnv("SystemRoot") + "\System32\WindowsPowerShell\v1.0\powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + AddQuotes(LoadOrderVerifier) + " -Configuration Release -VerifyOnly -OutputDirectory " + AddQuotes(LoadOrderPackage), SourcePath, 1, SW_HIDE) != 0
  #error Load-order helper package verification failed. Build NexusClient Release before compiling this installer.
#endif

[Setup]
AppName={#MyAppSetupName}
AppID=6af12c54-643b-4752-87d0-8335503010de
AppVersion={#MyAppVersion}
AppVerName={#MyAppSetupName} {#MyAppVersion}
AppCopyright=Copyright � {#MyPublisher} 2019-2026
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyPublisher}
AppPublisher={#MyPublisher}
;AppPublisherURL=http://...
;AppSupportURL=http://...
;AppUpdatesURL=http://...
OutputBaseFilename={#MyAppSetupName}-{#MyAppVersion}
DefaultGroupName={#MyAppSetupName}
DefaultDirName={pf}\{#MyAppSetupName}
UninstallDisplayName={#MyAppSetupName}
UninstallDisplayIcon={app}\{#MyExeName},0
Uninstallable=true
UninstallFilesDir={app}\uninstall
DirExistsWarning=no
DisableDirPage=no
CreateAppDir=true
OutputDir=..\Stage\Installer\
SourceDir=.
AllowNoIcons=true
SignedUninstaller=false
UsePreviousGroup=true
UsePreviousAppDir=true
LanguageDetectionMethod=uilanguage
InternalCompressLevel=Ultra64
SolidCompression=true
Compression=lzma2/Max
ChangesAssociations=true
LicenseFile=..\Stage\Release\data\License.txt
InfoBeforeFile=..\Stage\Release\data\releasenotes.rtf
MinVersion=0,6.1
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64 
ArchitecturesInstallIn64BitMode=x64 
AppMutex=Global\6af12c54-643b-4752-87d0-8335503010de

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: associateNxmFiles; Description: &Associate *.NXM files with {#MyAppSetupName}; GroupDescription: Other tasks:;
Name: associateNxmUrls; Description: Associate NXM: &URLs with {#MyAppSetupName}; GroupDescription: Other tasks:;
Name: associateFomodFiles; Description: &Associate *.FOMOD files with {#MyAppSetupName}; GroupDescription: Other tasks:;
Name: associateOmodFiles; Description: &Associate *.OMOD files with {#MyAppSetupName}; GroupDescription: Other tasks:;

[Dirs]
; Preserve shared ownership bindings across upgrades, uninstalls and independent NMM copies.
; Fine-grained read/create-only permissions are provisioned by the elevated installer below.
Name: "{commonappdata}\NMMCE\CollectionTargetAuthorities"; Flags: uninsneveruninstall

#define RetiredGameModeDlls "Fallout3.dll,Fallout4.dll,FalloutNV.dll,Oblivion.dll,OblivionRemastered.dll,Skyrim.dll,SkyrimGOG.dll,SkyrimSE.dll,Starfield.dll,Enderal.dll,EnderalSE.dll,Fallout4VR.dll,SkyrimVR.dll"
#define RetiredGameModeConfigs "Fallout3.dll.config,Fallout4.dll.config,FalloutNV.dll.config,Oblivion.dll.config,OblivionRemastered.dll.config,Skyrim.dll.config,SkyrimGOG.dll.config,SkyrimSE.dll.config,Starfield.dll.config,Enderal.dll.config,EnderalSE.dll.config,Fallout4VR.dll.config,SkyrimVR.dll.config"
; Filter stale development files without excluding game data XML or runtime dependencies.
#define NonRuntimeFiles "*.pdb,*.tmp,*.log,*.vshost.*,nunit.*,Microsoft.Testing.*,Microsoft.TestPlatform.*,Microsoft.VisualStudio.TestPlatform.*,Microsoft.ApplicationInsights.*,DevExpress.*.xml,Newtonsoft.Json.xml,SevenZipSharp.xml,System.Data.SQLite.xml,CSharpScript.xml"

[InstallDelete]
; This application-owned helper directory contains binaries only, never profiles or mod data.
Type: filesandordirs; Name: "{app}\Tools\LoadOrder"; Check: IsWin64
; Remove documentation replaced by plain-text licenses and generated release notes.
Type: files; Name: "{app}\data\License.rtf"
Type: files; Name: "{app}\data\NMM License.rtf"
Type: files; Name: "{app}\data\NewVersionDisclaimer.rtf"
; Remove NMM-owned Script Type assemblies left in legacy/non-canonical locations.
Type: files; Name: "{app}\CSharpScript.dll"
Type: files; Name: "{app}\ModScript.dll"
Type: files; Name: "{app}\XmlScript.dll"
Type: files; Name: "{app}\ScriptTypes\AntlrUtil.dll"
Type: files; Name: "{app}\GameModes\Antlr3.Runtime.dll"
Type: files; Name: "{app}\GameModes\AntlrUtil.dll"
Type: files; Name: "{app}\GameModes\CSharpScript.dll"
Type: files; Name: "{app}\GameModes\ModScript.dll"
Type: files; Name: "{app}\GameModes\XmlScript.dll"
; GamebryoBase is shared with the host and must load only from the application folder.
Type: files; Name: "{app}\GameModes\GamebryoBase.dll"
Type: files; Name: "{app}\GameModes\GamebryoBase.dll.config"
Type: files; Name: "{app}\GameModes\GamebryoBase.pdb"

; Remove only retired concrete Game Mode assemblies; game-specific Script Type assemblies remain installed.
Type: files; Name: "{app}\Fallout3.dll"
Type: files; Name: "{app}\GameModes\Fallout3.dll"
Type: files; Name: "{app}\Fallout4.dll"
Type: files; Name: "{app}\GameModes\Fallout4.dll"
Type: files; Name: "{app}\FalloutNV.dll"
Type: files; Name: "{app}\GameModes\FalloutNV.dll"
Type: files; Name: "{app}\Oblivion.dll"
Type: files; Name: "{app}\GameModes\Oblivion.dll"
Type: files; Name: "{app}\OblivionRemastered.dll"
Type: files; Name: "{app}\GameModes\OblivionRemastered.dll"
Type: files; Name: "{app}\Skyrim.dll"
Type: files; Name: "{app}\GameModes\Skyrim.dll"
Type: files; Name: "{app}\SkyrimGOG.dll"
Type: files; Name: "{app}\GameModes\SkyrimGOG.dll"
Type: files; Name: "{app}\SkyrimSE.dll"
Type: files; Name: "{app}\GameModes\SkyrimSE.dll"
Type: files; Name: "{app}\Starfield.dll"
Type: files; Name: "{app}\GameModes\Starfield.dll"
Type: files; Name: "{app}\Enderal.dll"
Type: files; Name: "{app}\GameModes\Enderal.dll"
Type: files; Name: "{app}\EnderalSE.dll"
Type: files; Name: "{app}\GameModes\EnderalSE.dll"
Type: files; Name: "{app}\Fallout4VR.dll"
Type: files; Name: "{app}\GameModes\Fallout4VR.dll"
Type: files; Name: "{app}\SkyrimVR.dll"
Type: files; Name: "{app}\GameModes\SkyrimVR.dll"

[Files]
Source: "..\Stage\Release\*.exe"; Excludes: "{#NonRuntimeFiles}"; DestDir: {app}; Flags: ignoreversion
Source: "..\Stage\Release\*.config"; Excludes: "{#NonRuntimeFiles},{#RetiredGameModeConfigs}"; DestDir: {app}; Flags: ignoreversion
Source: "..\Stage\Release\*.dll"; Excludes: "{#NonRuntimeFiles},{#RetiredGameModeDlls}"; DestDir: {app}; Flags: ignoreversion
Source: "..\Stage\Release\data\*"; Excludes: "{#NonRuntimeFiles}"; DestDir: {app}\data; Flags: ignoreversion recursesubdirs
Source: "..\Stage\Release\Languages\*"; Excludes: "{#NonRuntimeFiles}"; DestDir: {app}\Languages; Flags: ignoreversion recursesubdirs
Source: "..\Stage\Release\GameModes\*"; Excludes: "{#NonRuntimeFiles},Antlr3.Runtime.dll,AntlrUtil.dll,CSharpScript.dll,ModScript.dll,XmlScript.dll,GamebryoBase.dll,GamebryoBase.dll.config,{#RetiredGameModeDlls},{#RetiredGameModeConfigs}"; DestDir: {app}\GameModes; Flags: ignoreversion recursesubdirs
Source: "..\Stage\Release\ModFormats\*"; Excludes: "{#NonRuntimeFiles}"; DestDir: {app}\ModFormats; Flags: ignoreversion recursesubdirs
Source: "..\Stage\Release\ScriptTypes\*"; Excludes: "{#NonRuntimeFiles}"; DestDir: {app}\ScriptTypes; Flags: ignoreversion recursesubdirs
Source: "..\Stage\Release\Tools\LoadOrder\*"; Excludes: "*.pdb"; DestDir: {app}\Tools\LoadOrder; Flags: ignoreversion recursesubdirs; Check: IsWin64

[Icons]
Name: {group}\{#MyAppSetupName}; Filename: {app}\{#MyExeName}; WorkingDir: {app}
Name: {group}\{#MyAppSetupName} (Trace Mode); Filename: {app}\{#MyExeName}; Parameters: -trace; WorkingDir: {app}
Name: {group}\{cm:UninstallProgram,{#MyAppSetupName}}; Filename: {uninstallexe}; WorkingDir: {app}
Name: {commondesktop}\{#MyAppSetupName}; Filename: {app}\{#MyExeName}; Tasks: desktopicon; WorkingDir: {app}
Name: {userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppSetupName}; Filename: {app}\{#MyExeName}; Tasks: quicklaunchicon; WorkingDir: {app}

[Run]
Filename: {app}\{#MyExeName}; Description: {cm:LaunchProgram,{#MyAppSetupName}}; Flags: nowait postinstall skipifsilent

[Registry]
;.nxm
Root: HKCR; Subkey: .nxm; ValueType: string; ValueName: ; ValueData: NXM_File_Type; Flags: uninsdeletekey; Tasks: associateNxmFiles
Root: HKCR; Subkey: NXM_File_Type; ValueType: string; ValueName: ; ValueData: {#MyAppSetupName} Mod Archive; Flags: uninsdeletekey; Tasks: associateNxmFiles
Root: HKCR; Subkey: NXM_File_Type\DefaultIcon; ValueType: string; ValueName: ; ValueData: {app}\{#MyExeName},0; Tasks: associateNxmFiles
Root: HKCR; Subkey: NXM_File_Type\shell\open\command; ValueType: string; ValueName: ; ValueData: """{app}\{#MyExeName}"" ""%1"""; Tasks: associateNxmFiles
;URL support
Root: HKCR; Subkey: nxm; ValueType: string; ValueName: ; ValueData: "URL:Nexus Mod"; Flags: uninsdeletekey; Tasks: associateNxmUrls
Root: HKCR; Subkey: nxm; ValueType: string; ValueName: "URL Protocol"; ValueData: ; Tasks: associateNxmUrls
Root: HKCR; Subkey: nxm\DefaultIcon; ValueType: string; ValueName: ; ValueData: {#MyExeName}; Tasks: associateNxmUrls
Root: HKCR; Subkey: nxm\shell\open\command; ValueType: string; ValueName: ; ValueData: """{app}\{#MyExeName}"" ""%1"""; Tasks: associateNxmUrls
;.fomod
Root: HKCR; Subkey: .fomod; ValueType: string; ValueName: ; ValueData: FOMOD_File_Type; Flags: uninsdeletekey; Tasks: associateFomodFiles
Root: HKCR; Subkey: FOMOD_File_Type; ValueType: string; ValueName: ; ValueData: Fallout Mod Archive; Flags: uninsdeletekey; Tasks: associateFomodFiles
Root: HKCR; Subkey: FOMOD_File_Type\DefaultIcon; ValueType: string; ValueName: ; ValueData: {app}\{#MyExeName},0; Tasks: associateFomodFiles
Root: HKCR; Subkey: FOMOD_File_Type\shell\open\command; ValueType: string; ValueName: ; ValueData: """{app}\{#MyExeName}"" ""%1"""; Tasks: associateFomodFiles
;.omod
Root: HKCR; Subkey: .omod; ValueType: string; ValueName: ; ValueData: OMOD_File_Type; Flags: uninsdeletekey; Tasks: associateFomodFiles
Root: HKCR; Subkey: OMOD_File_Type; ValueType: string; ValueName: ; ValueData: Oblivion Mod Archive; Flags: uninsdeletekey; Tasks: associateFomodFiles
Root: HKCR; Subkey: OMOD_File_Type\DefaultIcon; ValueType: string; ValueName: ; ValueData: {app}\{#MyExeName},0; Tasks: associateFomodFiles
Root: HKCR; Subkey: OMOD_File_Type\shell\open\command; ValueType: string; ValueName: ; ValueData: """{app}\{#MyExeName}"" ""%1"""; Tasks: associateFomodFiles

#include "scripts\products.iss"

#include "scripts\products\stringversion.iss"
#include "scripts\products\winversion.iss"
#include "scripts\products\fileversion.iss"
#include "scripts\products\dotnetfxversion.iss"

#ifdef use_msi31
#include "scripts\products\msi31.iss"
#endif
#ifdef use_msi45
#include "scripts\products\msi45.iss"
#endif

#ifdef use_dotnetfx45
#include "scripts\products\dotnetfx45.iss"
#endif

[CustomMessages]
win2000sp3_title=Windows 2000 Service Pack 3
winxpsp2_title=Windows XP Service Pack 2
winxpsp3_title=Windows XP Service Pack 3

#expr SaveToFile(AddBackslash(SourcePath) + "Preprocessed"+MyAppSetupname+SetupScriptVersion+".iss")

[Code]
function SharedStateDirectoryAttributes(const Path: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
var
  StateDirectory, ParentDirectory, Parameters: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;

  StateDirectory := ExpandConstant('{commonappdata}\NMMCE\CollectionTargetAuthorities');
  ParentDirectory := ExpandConstant('{commonappdata}\NMMCE');
  if ((SharedStateDirectoryAttributes(ParentDirectory) and $400) <> 0) or
     ((SharedStateDirectoryAttributes(StateDirectory) and $400) <> 0) then
    RaiseException('NMM cannot provision shared installation state through a redirected or inaccessible directory: ' + StateDirectory);

  { Users may read bindings and add a new file in this exact directory. WD is not inherited,
    so it does not grant write access to existing records owned by another account.
    Only the creator receives modify access to its own files, including temporary files.
    Existing ACLs, binding contents and ownership are retained; no recursive reset is used. }
  Parameters := '"' + StateDirectory + '" /grant "*S-1-5-32-545:(OI)(CI)RX" ' +
    '"*S-1-5-32-545:(WD)" "*S-1-3-0:(OI)(IO)M"';
  if not Exec(ExpandConstant('{sys}\icacls.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('NMM could not provision access to its shared installation-state folder: ' + StateDirectory);
  if ResultCode <> 0 then
    RaiseException('NMM could not provision access to its shared installation-state folder: ' + StateDirectory + ' (code ' + IntToStr(ResultCode) + ').');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpInfoBefore then
    WizardForm.PageNameLabel.Caption := 'Release Notes';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
mRes : integer;
mPub : String;
begin
  case CurUninstallStep of
    usUninstall:
      begin
        //check if we should delete the user.config files
        mRes := MsgBox('Do you want to remove all of {#MyAppSetupName}''s configuration files? Doing so will reset all of {#MyAppSetupName}''s settings. Either option will keep your mods intact.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2)
        if mRes = IDYES then
          begin
            mPub := '{#MyPublisher}';
            StringChangeEx(mPub,' ', '_', True);
            DelTree(ExpandConstant('{localappdata}/') + mPub + '/{#MyExeName}*', False, True, True);
          End
      end;
  end;
end;

    function GetUninstallString: string;
var
  sUnInstPath: string;
  sUnInstallString: String;
begin
  Result := '';
  sUnInstPath := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\6af12c54-643b-4752-87d0-8335503010de_is1'); //Your App GUID/ID
  sUnInstallString := '';
  if not RegQueryStringValue(HKLM, sUnInstPath, 'UninstallString', sUnInstallString) then
    RegQueryStringValue(HKCU, sUnInstPath, 'UninstallString', sUnInstallString);
  Result := sUnInstallString;
end;

function IsUpgrade: Boolean;
begin
  Result := (GetUninstallString() <> '');
end;

function InitializeSetup: Boolean;
var
  V: Integer;
  Version: String;
  iResultCode: Integer;
  sUnInstallString: string;
begin
#ifdef use_msi31
	msi31('3.1');
#endif
#ifdef use_dotnetfx45
    dotnetfx45(528040); // min allowed version is .NET Framework 4.8
#endif

  Result := True; // in case when no previous version is found
  if RegValueExists(HKEY_LOCAL_MACHINE,'Software\Microsoft\Windows\CurrentVersion\Uninstall\6af12c54-643b-4752-87d0-8335503010de_is1', 'UninstallString') then  //Your App GUID/ID
  begin
    RegQueryStringValue(HKEY_LOCAL_MACHINE,'Software\Microsoft\Windows\CurrentVersion\Uninstall\6af12c54-643b-4752-87d0-8335503010de_is1', 'DisplayVersion', Version);
    if Version < '0.50.0' then
    begin
      V := MsgBox(ExpandConstant('You need to uninstall your current version of NMM before you apply this update. When asked if you would like to remove the config files, select no. This will not remove or change any of your installed mods. Simply install NMM to the same location as before.'), mbInformation, MB_YESNO); //Custom Message if App installed
      if V = IDYES then
      begin
        sUnInstallString := GetUninstallString();
        sUnInstallString :=  RemoveQuotes(sUnInstallString);
        Exec(ExpandConstant(sUnInstallString), '', '', SW_SHOW, ewWaitUntilTerminated, iResultCode);
        Result := True; //if you want to proceed after uninstall
            //Exit; //if you want to quit after uninstall
      end
      else
        Result := False; //when older version present and not uninstalled
    end;
  end;
end;