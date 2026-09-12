using System;
using System.IO;
using System.Xml.Linq;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.InstallationLog.Upgraders
{
	/// <summary>
	/// Upgrades the Install Log from 0.5.0.0 to the sparse 0.6.0.0 schema without migrating Virtual state.
	/// </summary>
	public class Upgrade0500Task : UpgradeTask
	{
		private static readonly Version SupportedVersion = new Version("0.5.0.0");

		/// <summary>
		/// Updates only the InstallLog schema version; existing mod and data-file records remain implicit Virtual state.
		/// </summary>
		protected override void UpgradeInstallLog(string logPath, string modInstallDirectory, ModRegistry modRegistry)
		{
			if (!File.Exists(logPath))
				return;

			XDocument log = XDocument.Load(logPath);
			XElement root = log.Element("installLog");
			string version = root?.Attribute("fileVersion")?.Value;
			if (!SupportedVersion.ToString().Equals(version, StringComparison.Ordinal))
				throw new UpgradeException(string.Format("Cannot upgrade Install Log version: {0} Expecting {1}", version, SupportedVersion));

			root.Attribute("fileVersion").Value = InstallLog.CurrentVersion.ToString();
			log.Save(logPath);
		}
	}
}
