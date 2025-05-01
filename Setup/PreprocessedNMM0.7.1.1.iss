
;#define use_msi45



[Setup]
AppName=NMM
AppID=6af12c54-643b-4752-87d0-8335503010de
AppVersion=0.88.2
AppVerName=NMM 0.88.2
AppCopyright=Copyright ï¿½ DuskDweller 2019-2022
VersionInfoVersion=0.88.2
VersionInfoCompany=DuskDweller
AppPublisher=DuskDweller
;AppPublisherURL=http://...
;AppSupportURL=http://...
;AppUpdatesURL=http://...
OutputBaseFilename=NMM-0.88.2
DefaultGroupName=NMM
DefaultDirName={pf}\NMM
UninstallDisplayName=NMM
UninstallDisplayIcon={app}\NexusClient.exe,0
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
LicenseFile=..\Stage\Release\data\License.rtf
InfoBeforeFile=..\Stage\Release\data\NewVersionDisclaimer.rtf
InfoAfterFile=..\Stage\Release\data\releasenotes.rtf
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
Name: associateNxmFiles; Description: &Associate *.NXM files with NMM; GroupDescription: Other tasks:;
Name: associateNxmUrls; Description: Associate NXM: &URLs with NMM; GroupDescription: Other tasks:;
Name: associateFomodFiles; Description: &Associate *.FOMOD files with NMM; GroupDescription: Other tasks:;
Name: associateOmodFiles; Description: &Associate *.OMOD files with NMM; GroupDescription: Other tasks:;

[Files]
Source: "..\Stage\Release\*.exe"; Excludes: "*.vshost.exe"; DestDir: {app}; Flags: ignoreversion
Source: "..\Stage\Release\*.config"; Excludes: "*.vshost.exe.config"; DestDir: {app}; Flags: ignoreversion
Source: "..\Stage\Release\*.dll"; DestDir: {app}; Flags: ignoreversion
Source: "..\Stage\Release\data\*"; Excludes: "*.pdb"; DestDir: {app}\data; Flags: ignoreversion recursesubdirs
Source: "..\Stage\Release\GameModes\*"; Excludes: "*.pdb"; DestDir: {app}\GameModes; Flags: ignoreversion recursesubdirs
Source: "..\Stage\Release\ModFormats\*"; Excludes: "*.pdb"; DestDir: {app}\ModFormats; Flags: ignoreversion recursesubdirs
Source: "..\Stage\Release\ScriptTypes\*"; Excludes: "*.pdb"; DestDir: {app}\ScriptTypes; Flags: ignoreversion recursesubdirs

[Icons]
Name: {group}\NMM; Filename: {app}\NexusClient.exe; WorkingDir: {app}
Name: {group}\NMM (Trace Mode); Filename: {app}\NexusClient.exe; Parameters: -trace; WorkingDir: {app}
Name: {group}\{cm:UninstallProgram,NMM}; Filename: {uninstallexe}; WorkingDir: {app}
Name: {commondesktop}\NMM; Filename: {app}\NexusClient.exe; Tasks: desktopicon; WorkingDir: {app}
Name: {userappdata}\Microsoft\Internet Explorer\Quick Launch\NMM; Filename: {app}\NexusClient.exe; Tasks: quicklaunchicon; WorkingDir: {app}

[Run]
Filename: {app}\NexusClient.exe; Description: {cm:LaunchProgram,NMM}; Flags: nowait postinstall skipifsilent

[Registry]
;.nxm
Root: HKCR; Subkey: .nxm; ValueType: string; ValueName: ; ValueData: NXM_File_Type; Flags: uninsdeletekey; Tasks: associateNxmFiles
Root: HKCR; Subkey: NXM_File_Type; ValueType: string; ValueName: ; ValueData: NMM Mod Archive; Flags: uninsdeletekey; Tasks: associateNxmFiles
Root: HKCR; Subkey: NXM_File_Type\DefaultIcon; ValueType: string; ValueName: ; ValueData: {app}\NexusClient.exe,0; Tasks: associateNxmFiles
Root: HKCR; Subkey: NXM_File_Type\shell\open\command; ValueType: string; ValueName: ; ValueData: """{app}\NexusClient.exe"" ""%1"""; Tasks: associateNxmFiles
;URL support
Root: HKCR; Subkey: nxm; ValueType: string; ValueName: ; ValueData: "URL:Nexus Mod"; Flags: uninsdeletekey; Tasks: associateNxmUrls
Root: HKCR; Subkey: nxm; ValueType: string; ValueName: "URL Protocol"; ValueData: ; Tasks: associateNxmUrls
Root: HKCR; Subkey: nxm\DefaultIcon; ValueType: string; ValueName: ; ValueData: NexusClient.exe; Tasks: associateNxmUrls
Root: HKCR; Subkey: nxm\shell\open\command; ValueType: string; ValueName: ; ValueData: """{app}\NexusClient.exe"" ""%1"""; Tasks: associateNxmUrls
;.fomod
Root: HKCR; Subkey: .fomod; ValueType: string; ValueName: ; ValueData: FOMOD_File_Type; Flags: uninsdeletekey; Tasks: associateFomodFiles
Root: HKCR; Subkey: FOMOD_File_Type; ValueType: string; ValueName: ; ValueData: Fallout Mod Archive; Flags: uninsdeletekey; Tasks: associateFomodFiles
Root: HKCR; Subkey: FOMOD_File_Type\DefaultIcon; ValueType: string; ValueName: ; ValueData: {app}\NexusClient.exe,0; Tasks: associateFomodFiles
Root: HKCR; Subkey: FOMOD_File_Type\shell\open\command; ValueType: string; ValueName: ; ValueData: """{app}\NexusClient.exe"" ""%1"""; Tasks: associateFomodFiles
;.omod
Root: HKCR; Subkey: .omod; ValueType: string; ValueName: ; ValueData: OMOD_File_Type; Flags: uninsdeletekey; Tasks: associateFomodFiles
Root: HKCR; Subkey: OMOD_File_Type; ValueType: string; ValueName: ; ValueData: Oblivion Mod Archive; Flags: uninsdeletekey; Tasks: associateFomodFiles
Root: HKCR; Subkey: OMOD_File_Type\DefaultIcon; ValueType: string; ValueName: ; ValueData: {app}\NexusClient.exe,0; Tasks: associateFomodFiles
Root: HKCR; Subkey: OMOD_File_Type\shell\open\command; ValueType: string; ValueName: ; ValueData: """{app}\NexusClient.exe"" ""%1"""; Tasks: associateFomodFiles

[Files]
Source: "scripts\isxdl\isxdl.dll"; Flags: dontcopy

[Code]
procedure isxdl_AddFile(URL, Filename: PAnsiChar);
external 'isxdl_AddFile@files:isxdl.dll stdcall';

function isxdl_DownloadFiles(hWnd: Integer): Integer;
external 'isxdl_DownloadFiles@files:isxdl.dll stdcall';

function isxdl_SetOption(Option, Value: PAnsiChar): Integer;
external 'isxdl_SetOption@files:isxdl.dll stdcall';

[CustomMessages]
DependenciesDir=MyProgramDependencies

en.depdownload_msg=The following applications are required before setup can continue:%n%n%1%nDownload and install now?
de.depdownload_msg=Die folgenden Programme werden benötigt bevor das Setup fortfahren kann:%n%n%1%nJetzt downloaden und installieren?

en.depdownload_memo_title=Download dependencies
de.depdownload_memo_title=Abhängigkeiten downloaden

en.depinstall_memo_title=Install dependencies
de.depinstall_memo_title=Abhängigkeiten installieren

en.depinstall_title=Installing dependencies
de.depinstall_title=Installiere Abhängigkeiten

en.depinstall_description=Please wait while Setup installs dependencies on your computer.
de.depinstall_description=Warten Sie bitte während Abhängigkeiten auf Ihrem Computer installiert wird.

en.depinstall_status=Installing %1...
de.depinstall_status=Installiere %1...

en.depinstall_missing=%1 must be installed before setup can continue. Please install %1 and run Setup again.
de.depinstall_missing=%1 muss installiert werden bevor das Setup fortfahren kann. Bitte installieren Sie %1 und starten Sie das Setup erneut.

en.depinstall_error=An error occured while installing the dependencies. Please restart the computer and run the setup again or install the following dependencies manually:%n
de.depinstall_error=Ein Fehler ist während der Installation der Abghängigkeiten aufgetreten. Bitte starten Sie den Computer neu und führen Sie das Setup erneut aus oder installieren Sie die folgenden Abhängigkeiten per Hand:%n

en.isxdl_langfile=
de.isxdl_langfile=german2.ini


[Files]
Source: "scripts\isxdl\german2.ini"; Flags: dontcopy

[Code]
type
	TProduct = record
		File: String;
		Title: String;
		Parameters: String;
		InstallClean : boolean;
		MustRebootAfter : boolean;
	end;

	InstallResult = (InstallSuccessful, InstallRebootRequired, InstallError);

var
	installMemo, downloadMemo, downloadMessage: string;
	products: array of TProduct;
	delayedReboot: boolean;
	DependencyPage: TOutputProgressWizardPage;


procedure AddProduct(FileName, Parameters, Title, Size, URL: string; InstallClean : boolean; MustRebootAfter : boolean);
var
	path: string;
	i: Integer;
begin
	installMemo := installMemo + '%1' + Title + #13;

	path := ExpandConstant('{src}{\}') + CustomMessage('DependenciesDir') + '\' + FileName;
	if not FileExists(path) then begin
		path := ExpandConstant('{tmp}{\}') + FileName;

		isxdl_AddFile(URL, path);

		downloadMemo := downloadMemo + '%1' + Title + #13;
		downloadMessage := downloadMessage + '	' + Title + ' (' + Size + ')' + #13;
	end;

	i := GetArrayLength(products);
	SetArrayLength(products, i + 1);
	products[i].File := path;
	products[i].Title := Title;
	products[i].Parameters := Parameters;
	products[i].InstallClean := InstallClean;
	products[i].MustRebootAfter := MustRebootAfter;
end;

function SmartExec(prod : TProduct; var ResultCode : Integer) : boolean;
begin
	if (LowerCase(Copy(prod.File,Length(prod.File)-2,3)) = 'exe') then begin
		Result := Exec(prod.File, prod.Parameters, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
	end else begin
		Result := ShellExec('', prod.File, prod.Parameters, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
	end;
end;

function PendingReboot : boolean;
var	names: String;
begin
	if (RegQueryMultiStringValue(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Control\Session Manager', 'PendingFileRenameOperations', names)) then begin
		Result := true;
	end else if ((RegQueryMultiStringValue(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Control\Session Manager', 'SetupExecute', names)) and (names <> ''))  then begin
		Result := true;
	end else begin
		Result := false;
	end;
end;

function InstallProducts: InstallResult;
var
	ResultCode, i, productCount, finishCount: Integer;
begin
	Result := InstallSuccessful;
	productCount := GetArrayLength(products);

	if productCount > 0 then begin
		DependencyPage := CreateOutputProgressPage(CustomMessage('depinstall_title'), CustomMessage('depinstall_description'));
		DependencyPage.Show;

		for i := 0 to productCount - 1 do begin
			if (products[i].InstallClean and (delayedReboot or PendingReboot())) then begin
				Result := InstallRebootRequired;
				break;
			end;

			DependencyPage.SetText(FmtMessage(CustomMessage('depinstall_status'), [products[i].Title]), '');
			DependencyPage.SetProgress(i, productCount);

			if SmartExec(products[i], ResultCode) then begin
				if (products[i].MustRebootAfter) then begin
					if (i = productCount - 1) then begin
						delayedReboot := true;
					end else begin
						Result := InstallRebootRequired;
					end;
					break;
				end else if (ResultCode = 0) then begin
					finishCount := finishCount + 1;
				end else if (ResultCode = 3010) then begin
					delayedReboot := true;
					finishCount := finishCount + 1;
				end else begin
					Result := InstallSuccessful;
					break;
				end;
			end else begin
				Result := InstallSuccessful;
				break;
			end;
		end;

		for i := 0 to productCount - finishCount - 1 do begin
			products[i] := products[i+finishCount];
		end;
		SetArrayLength(products, productCount - finishCount);

		DependencyPage.Hide;
	end;
end;

function PrepareToInstall(var NeedsRestart: boolean): String;
var
	i: Integer;
	s: string;
begin
	delayedReboot := false;

	case InstallProducts() of
		InstallError: begin
			s := CustomMessage('depinstall_error');

			for i := 0 to GetArrayLength(products) - 1 do begin
				s := s + #13 + '	' + products[i].Title;
			end;

			Result := s;
			end;
		InstallRebootRequired: begin
			Result := products[0].Title;
			NeedsRestart := true;

			RegWriteStringValue(HKEY_CURRENT_USER, 'SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce', 'InstallBootstrap', ExpandConstant('{srcexe}'));
			end;
	end;
end;

function NeedRestart : boolean;
begin
	if (delayedReboot) then
		Result := true;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
	s: string;
begin
	if downloadMemo <> '' then
		s := s + CustomMessage('depdownload_memo_title') + ':' + NewLine + FmtMessage(downloadMemo, [Space]) + NewLine;
	if installMemo <> '' then
		s := s + CustomMessage('depinstall_memo_title') + ':' + NewLine + FmtMessage(installMemo, [Space]) + NewLine;

	s := s + MemoDirInfo + NewLine + NewLine + MemoGroupInfo

	if MemoTasksInfo <> '' then
		s := s + NewLine + NewLine + MemoTasksInfo;

	Result := s
end;

function NextButtonClick(CurPageID: Integer): boolean;
begin
	Result := true;

	if CurPageID = wpReady then begin
		if downloadMemo <> '' then begin
			if (ActiveLanguage() <> 'en') then begin
				ExtractTemporaryFile(CustomMessage('isxdl_langfile'));
				isxdl_SetOption('language', ExpandConstant('{tmp}{\}') + CustomMessage('isxdl_langfile'));
			end;

			if SuppressibleMsgBox(FmtMessage(CustomMessage('depdownload_msg'), [downloadMessage]), mbConfirmation, MB_YESNO, IDYES) = IDNO then
				Result := false
			else if isxdl_DownloadFiles(StrToInt(ExpandConstant('{wizardhwnd}'))) = 0 then
				Result := false;
		end;
	end;
end;

function IsX86: boolean;
begin
	Result := (ProcessorArchitecture = paX86) or (ProcessorArchitecture = paUnknown);
end;

function IsX64: boolean;
begin
	Result := Is64BitInstallMode and (ProcessorArchitecture = paX64);
end;

function IsIA64: boolean;
begin
	Result := Is64BitInstallMode and (ProcessorArchitecture = paIA64);
end;

function GetString(x86, x64, ia64: String): String;
begin
	if IsX64() and (x64 <> '') then begin
		Result := x64;
	end else if IsIA64() and (ia64 <> '') then begin
		Result := ia64;
	end else begin
		Result := x86;
	end;
end;

function GetArchitectureString(): String;
begin
	if IsX64() then begin
		Result := '_x64';
	end else if IsIA64() then begin
		Result := '_ia64';
	end else begin
		Result := '';
	end;
end;

function stringtoversion(var temp: String): Integer;
var
	part: String;
	pos1: Integer;

begin
	if (Length(temp) = 0) then begin
		Result := -1;
		Exit;
	end;

	pos1 := Pos('.', temp);
	if (pos1 = 0) then begin
		Result := StrToInt(temp);
		temp := '';
	end else begin
		part := Copy(temp, 1, pos1 - 1);
		temp := Copy(temp, pos1 + 1, Length(temp));
		Result := StrToInt(part);
	end;
end;

function compareinnerversion(var x, y: String): Integer;
var
	num1, num2: Integer;

begin
	num1 := stringtoversion(x);
	num2 := stringtoversion(y);
	if (num1 = -1) or (num2 = -1) then begin
		Result := 0;
		Exit;
	end;

	if (num1 < num2) then begin
		Result := -1;
	end else if (num1 > num2) then begin
		Result := 1;
	end else begin
		Result := compareinnerversion(x, y);
	end;
end;

function compareversion(versionA, versionB: String): Integer;
var
  temp1, temp2: String;

begin
    temp1 := versionA;
    temp2 := versionB;
    Result := compareinnerversion(temp1, temp2);
end;
[Code]
var
	WindowsVersion: TWindowsVersion;

procedure initwinversion();
begin
	GetWindowsVersionEx(WindowsVersion);
end;

function exactwinversion(MajorVersion, MinorVersion: integer): boolean;
begin
	Result := (WindowsVersion.Major = MajorVersion) and (WindowsVersion.Minor = MinorVersion);
end;

function minwinversion(MajorVersion, MinorVersion: integer): boolean;
begin
	Result := (WindowsVersion.Major > MajorVersion) or ((WindowsVersion.Major = MajorVersion) and (WindowsVersion.Minor >= MinorVersion));
end;

function maxwinversion(MajorVersion, MinorVersion: integer): boolean;
begin
	Result := (WindowsVersion.Major < MajorVersion) or ((WindowsVersion.Major = MajorVersion) and (WindowsVersion.Minor <= MinorVersion));
end;

function exactwinspversion(MajorVersion, MinorVersion, SpVersion: integer): boolean;
begin
	if exactwinversion(MajorVersion, MinorVersion) then
		Result := WindowsVersion.ServicePackMajor = SpVersion
	else
		Result := true;
end;

function minwinspversion(MajorVersion, MinorVersion, SpVersion: integer): boolean;
begin
	if exactwinversion(MajorVersion, MinorVersion) then
		Result := WindowsVersion.ServicePackMajor >= SpVersion
	else
		Result := true;
end;

function maxwinspversion(MajorVersion, MinorVersion, SpVersion: integer): boolean;
begin
	if exactwinversion(MajorVersion, MinorVersion) then
		Result := WindowsVersion.ServicePackMajor <= SpVersion
	else
		Result := true;
end;
[Code]
function GetFullVersion(VersionMS, VersionLS: cardinal): string;
var
	version: string;
begin
	version := IntToStr(word(VersionMS shr 16));
	version := version + '.' + IntToStr(word(VersionMS and not $ffff0000));

	version := version + '.' + IntToStr(word(VersionLS shr 16));
	version := version + '.' + IntToStr(word(VersionLS and not $ffff0000));

	Result := version;
end;

function fileversion(file: string): string;
var
	versionMS, versionLS: cardinal;
begin
	if GetVersionNumbers(file, versionMS, versionLS) then
		Result := GetFullVersion(versionMS, versionLS)
	else
		Result := '0';
end;
[Code]
type
	NetFXType = (NetFx10, NetFx11, NetFx20, NetFx30, NetFx35, NetFx40Client, NetFx40Full, NetFx45);

const
	netfx11plus_reg = 'Software\Microsoft\NET Framework Setup\NDP\';

function netfxinstalled(version: NetFXType; lcid: string): boolean;
var
	regVersion: cardinal;
	regVersionString: string;
begin
	if (lcid <> '') then
		lcid := '\' + lcid;

	if (version = NetFx10) then begin
		RegQueryStringValue(HKLM, 'Software\Microsoft\.NETFramework\Policy\v1.0\3705', 'Install', regVersionString);
		Result := regVersionString <> '';
	end else begin
		case version of
			NetFx35:
				RegQueryDWordValue(HKLM, netfx11plus_reg + 'v3.5' + lcid, 'Install', regVersion);
			NetFx40Client:
				RegQueryDWordValue(HKLM, netfx11plus_reg + 'v4\Client' + lcid, 'Install', regVersion);
			NetFx40Full:
				RegQueryDWordValue(HKLM, netfx11plus_reg + 'v4\Full' + lcid, 'Install', regVersion);
			NetFx45:
			begin
				RegQueryDWordValue(HKLM, netfx11plus_reg + 'v4\Full' + lcid, 'Release', regVersion);
				Result := (regVersion >= 378389) and (regVersion <= 393295);
				Exit;
			end;
		end;
		Result := (regVersion <> 0);
	end;
end;

function netfxspversion(version: NetFXType; lcid: string): integer;
var
	regVersion: cardinal;
begin
	if (lcid <> '') then
		lcid := '\' + lcid;

	case version of
		NetFx10:
			regVersion := -1;
		NetFx35:
			if (not RegQueryDWordValue(HKLM, netfx11plus_reg + 'v3.5' + lcid, 'SP', regVersion)) then
				regVersion := -1;
		NetFx40Client:
			if (not RegQueryDWordValue(HKLM, netfx11plus_reg + 'v4\Client' + lcid, 'Servicing', regVersion)) then
				regVersion := -1;
		NetFx40Full:
			if (not RegQueryDWordValue(HKLM, netfx11plus_reg + 'v4\Full' + lcid, 'Servicing', regVersion)) then
				regVersion := -1;
		NetFx45:
			if (RegQueryDWordValue(HKLM, netfx11plus_reg + 'v4\Full' + lcid, 'Release', regVersion)) then begin
				if (regVersion = 379893) or (regVersion = 393295) then
					regVersion := 2 // 4.5.2
				else if (regVersion = 378675) or (regVersion = 378758) then
					regVersion := 1 // 4.5.1
				else if (regVersion = 378389) then
					regVersion := 0 // 4.5.0
				else
					regVersion := -1;
			end;
	end;
	Result := regVersion;
end;

[CustomMessages]
msi31_title=Windows Installer 3.1

en.msi31_size=2.5 MB
de.msi31_size=2,5 MB


[Code]
const
	msi31_url = 'http://download.microsoft.com/download/1/4/7/147ded26-931c-4daf-9095-ec7baf996f46/WindowsInstaller-KB893803-v2-x86.exe';

procedure msi31(MinVersion: string);
begin
	if (IsX86() and minwinversion(5, 0) and (compareversion(fileversion(ExpandConstant('{sys}{\}msi.dll')), MinVersion) < 0)) then
		AddProduct('msi31.exe',
			'/passive /norestart',
			CustomMessage('msi31_title'),
			CustomMessage('msi31_size'),
			msi31_url,
			false, false);
end;


[CustomMessages]
dotnetfx45_title=.NET Framework 4.5.2

dotnetfx45_size=1 MB - 68 MB

;http://www.microsoft.com/globaldev/reference/lcid-all.mspx
en.dotnetfx45_lcid=''
de.dotnetfx45_lcid='/lcid 1031 '


[Code]
const
	dotnetfx45_url = 'http://download.microsoft.com/download/B/4/1/B4119C11-0423-477B-80EE-7A474314B347/NDP452-KB2901954-Web.exe';

procedure dotnetfx45(MinVersion: integer);
begin
	if (not netfxinstalled(NetFx45, '') or (netfxspversion(NetFx45, '') < MinVersion)) then
		AddProduct('dotnetfx45.exe',
			CustomMessage('dotnetfx45_lcid') + '/q /passive /norestart',
			CustomMessage('dotnetfx45_title'),
			CustomMessage('dotnetfx45_size'),
			dotnetfx45_url,
			false, false);
end;

[CustomMessages]
win2000sp3_title=Windows 2000 Service Pack 3
winxpsp2_title=Windows XP Service Pack 2
winxpsp3_title=Windows XP Service Pack 3

