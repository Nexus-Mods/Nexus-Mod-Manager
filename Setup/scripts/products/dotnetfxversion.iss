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
				// >= .NET Framework 4.6.2
				Result := (regVersion >= 394802);
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
			//not supported
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
			if (not RegQueryDWordValue(HKLM, netfx11plus_reg + 'v4\Full' + lcid, 'Release', regVersion)) then
				regVersion := -1;
	end;
	Result := regVersion;
end;
