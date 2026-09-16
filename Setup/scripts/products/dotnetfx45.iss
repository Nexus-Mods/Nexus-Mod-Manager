// requires Windows 7 Service Pack 1, Windows 8.1, Windows 10, or a corresponding supported Windows Server version
// Uses the Microsoft .NET Framework 4.8 web runtime installer.
// https://dotnet.microsoft.com/download/dotnet-framework/net48

[CustomMessages]
dotnetfx45_title=.NET Framework 4.8

dotnetfx45_size=1.4 MB + required components

;http://www.microsoft.com/globaldev/reference/lcid-all.mspx
en.dotnetfx45_lcid=''
de.dotnetfx45_lcid='/lcid 1031 '


[Code]
const
	dotnetfx45_url = 'https://go.microsoft.com/fwlink/?LinkId=2085155';

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