using System;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	public interface IModLinkInstaller
	{
		#region Mod Link Installer
		string AddFileLink(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching);
		string AddFileLink(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching, bool p_booHandlePlugin);
		#endregion
	}
}
