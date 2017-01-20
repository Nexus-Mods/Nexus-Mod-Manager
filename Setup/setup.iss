// Define stuff we need to download/update/install

#define use_msi31
;#define use_msi45
#define use_dotnetfx45

// End

#define MyAppSetupName 'Nexus Mod Manager'
#define MyExeName 'NexusClient.exe'
#define MyAppVersion '0.63.13'
#define SetupScriptVersion '0.7.1.0'
#define MyPublisher 'Black Tree Gaming'
[Setup]
AppName={#MyAppSetupName}
AppID=6af12c54-643b-4752-87d0-8335503010de
AppVersion={#MyAppVersion}
AppVerName={#MyAppSetupName} {#MyAppVersion}
AppCopyright=Copyright © {#MyPublisher} 2011-2016
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
OutputDir=bin
SourceDir=.
AllowNoIcons=true
SignedUninstaller=true
UsePreviousGroup=true
UsePreviousAppDir=true
LanguageDetectionMethod=uilanguage
InternalCompressLevel=Ultra64
SolidCompression=true
Compression=lzma2/Max
ChangesAssociations=true
LicenseFile=..\bin\Release\data\Licence.rtf
InfoBeforeFile=..\bin\Release\data\NewVersionDisclaimer.rtf
InfoAfterFile=..\bin\Release\data\releasenotes.rtf
MinVersion=0,6.0
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64 ia64
ArchitecturesInstallIn64BitMode=x64 ia64
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

[Files]
Source: "..\bin\Release\*.exe"; Excludes: "*.vshost.exe"; DestDir: {app}; Flags: ignoreversion
Source: "..\bin\Release\*.config"; Excludes: "*.vshost.exe.config"; DestDir: {app}; Flags: ignoreversion
Source: "..\bin\Release\*.dll"; DestDir: {app}; Flags: ignoreversion
Source: "..\bin\Release\data\*"; DestDir: {app}\data; Flags: ignoreversion recursesubdirs
Source: "..\bin\Release\GameModes\*"; DestDir: {app}\GameModes; Flags: ignoreversion recursesubdirs
Source: "..\bin\Release\ModFormats\*"; DestDir: {app}\ModFormats; Flags: ignoreversion recursesubdirs
Source: "..\bin\Release\ScriptTypes\*"; DestDir: {app}\ScriptTypes; Flags: ignoreversion recursesubdirs

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
    //dotnetfx45(2); // min allowed version is .netfx 4.5.2
    //dotnetfx45(0); // min allowed version is .netfx 4.5.0
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