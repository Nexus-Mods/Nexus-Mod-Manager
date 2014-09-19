using System;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	public interface IModLinkInstaller
	{
		#region Mod Link Installer
		string AddFileLink(IMod p_modMod, string p_strBaseFilePath, bool p_booIsSwitching);
		#endregion
	}
}
