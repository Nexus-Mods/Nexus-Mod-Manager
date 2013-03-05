// Define stuff we need to download/update/install

#define use_msi31

#define use_dotnetfx35
// German languagepack?
//#define use_dotnetfx35lp

// Enable the required define(s) below if a local event function (prepended with Local) is used
//#define haveLocalPrepareToInstall
//#define haveLocalNeedRestart
//#define haveLocalNextButtonClick

// End

#define MyAppSetupName 'Nexus Mod Manager'
#define MyExeName 'NexusClient.exe'
#define MyAppVersion '0.44.0'
#define SetupScriptVersion '0.7.1.0'
#define MyPublisher 'Black Tree Gaming'
[Setup]
AppName={#MyAppSetupName}
AppID=6af12c54-643b-4752-87d0-8335503010de
AppVersion={#MyAppVersion}
AppVerName={#MyAppSetupName} {#MyAppVersion}
AppCopyright=Copyright © {#MyPublisher} 2011-2012
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
CreateAppDir=true
OutputDir=bin
SourceDir=.
AllowNoIcons=true
UsePreviousGroup=true
UsePreviousAppDir=true
LanguageDetectionMethod=uilanguage
InternalCompressLevel=Ultra64
SolidCompression=true
Compression=lzma2/Max
ChangesAssociations=true
LicenseFile=..\bin\Release\data\Licence.rtf
InfoBeforeFile=..\bin\Release\data\releasenotes.rtf
MinVersion=0,5.01
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

#include "scripts\products\winversion.iss"
#include "scripts\products\fileversion.iss"

#ifdef use_msi31
#include "scripts\products\msi31.iss"
#endif

#ifdef use_dotnetfx35
#include "scripts\products\dotnetfx35sp1.iss"
#ifdef use_dotnetfx35lp
#include "scripts\products\dotnetfx35sp1lp.iss"
#endif
#endif

[CustomMessages]
win2000sp3_title=Windows 2000 Service Pack 3
winxpsp2_title=Windows XP Service Pack 2
winxpsp3_title=Windows XP Service Pack 3

#expr SaveToFile(AddBackslash(SourcePath) + "Preprocessed"+MyAppSetupname+SetupScriptVersion+".iss")

[Code]
function InitializeSetup(): Boolean;
begin
	//init windows version
	initwinversion();
	
	//check if dotnetfx20 can be installed on this OS
	//if not minwinspversion(5, 0, 3) then begin
	//	MsgBox(FmtMessage(CustomMessage('depinstall_missing'), [CustomMessage('win2000sp3_title')]), mbError, MB_OK);
	//	exit;
	//end;
	if not minwinspversion(5, 1, 3) then begin
		MsgBox(FmtMessage(CustomMessage('depinstall_missing'), [CustomMessage('winxpsp3_title')]), mbError, MB_OK);
		exit;
	end;
	
#ifdef use_dotnetfx35
	dotnetfx35sp1();
#ifdef use_dotnetfx35lp
	dotnetfx35sp1lp();
#endif
#endif
	
	Result := true;
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
