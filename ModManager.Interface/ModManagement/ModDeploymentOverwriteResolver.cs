using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Resolves overwrite prompts consistently for Direct and promoted Virtual deployments.
	/// </summary>
	public sealed class ModDeploymentOverwriteResolver
	{
		private readonly IMod m_modMod;
		private readonly IInstallLog m_ilgInstallLog;
		private readonly IModDeploymentManager m_mdmDeploymentManager;
		private readonly ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmationDelegate;
		private readonly List<string> m_lstOverwriteFolders = new List<string>();
		private readonly List<string> m_lstDontOverwriteFolders = new List<string>();
		private readonly HashSet<string> m_hstOverwriteOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> m_hstDontOverwriteOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private bool m_booOverwriteAll;
		private bool m_booDontOverwriteAll;

		/// <summary>
		/// Initializes an overwrite resolver for one mod installation.
		/// </summary>
		public ModDeploymentOverwriteResolver(IMod p_modMod, IInstallLog p_ilgInstallLog,
			IModDeploymentManager p_mdmDeploymentManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			m_modMod = p_modMod ?? throw new ArgumentNullException(nameof(p_modMod));
			m_ilgInstallLog = p_ilgInstallLog ?? throw new ArgumentNullException(nameof(p_ilgInstallLog));
			m_mdmDeploymentManager = p_mdmDeploymentManager ?? throw new ArgumentNullException(nameof(p_mdmDeploymentManager));
			m_dlgOverwriteConfirmationDelegate = p_dlgOverwriteConfirmationDelegate ??
				((message, allowGroup, hasOwner) => OverwriteResult.No);
		}

		/// <summary>
		/// Determines whether the installing mod should become the physical winner for the target.
		/// </summary>
		public bool ShouldActivate(ModDeploymentTarget p_mdtTarget)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));

			string deploymentPath = m_mdmDeploymentManager.GetDeploymentPath(p_mdtTarget);
			if (!File.Exists(deploymentPath))
				return true;

			string currentOwnerKey = m_mdmDeploymentManager.GetCurrentOwnerKey(p_mdtTarget);
			string installingModKey = m_ilgInstallLog.GetModKey(m_modMod);
			if (!string.IsNullOrEmpty(currentOwnerKey) &&
				currentOwnerKey.Equals(installingModKey, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			string directory = (Path.GetDirectoryName(deploymentPath) ?? string.Empty).ToLowerInvariant();
			if (IsFolderCovered(m_lstOverwriteFolders, directory))
				return true;
			if (IsFolderCovered(m_lstDontOverwriteFolders, directory))
				return false;
			if (m_booOverwriteAll)
				return true;
			if (m_booDontOverwriteAll)
				return false;
			if (!string.IsNullOrEmpty(currentOwnerKey) && m_hstOverwriteOwners.Contains(currentOwnerKey))
				return true;
			if (!string.IsNullOrEmpty(currentOwnerKey) && m_hstDontOverwriteOwners.Contains(currentOwnerKey))
				return false;

			bool readOnly = new FileInfo(deploymentPath).IsReadOnly;
			bool hasManagedOwner = !string.IsNullOrEmpty(currentOwnerKey) &&
				!currentOwnerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase);
			string message = hasManagedOwner
				? "Game file '{0}' is already owned by another managed mod"
				: "Game file '{0}' already exists";
			message += readOnly ? " and is read-only." : ".";
			message += Environment.NewLine + string.Format("Overwrite with {0} mod's file?", m_modMod.ModName);

			OverwriteResult result = m_dlgOverwriteConfirmationDelegate(
				string.Format(message, p_mdtTarget.RelativePath),
				true,
				hasManagedOwner);
			switch (result)
			{
				case OverwriteResult.Yes:
					return true;
				case OverwriteResult.No:
					return false;
				case OverwriteResult.YesToAll:
					m_booOverwriteAll = true;
					return true;
				case OverwriteResult.NoToAll:
					m_booDontOverwriteAll = true;
					return false;
				case OverwriteResult.YesToGroup:
					m_lstOverwriteFolders.Add(directory);
					return true;
				case OverwriteResult.NoToGroup:
					m_lstDontOverwriteFolders.Add(directory);
					return false;
				case OverwriteResult.YesToMod:
					if (!string.IsNullOrEmpty(currentOwnerKey))
						m_hstOverwriteOwners.Add(currentOwnerKey);
					return true;
				case OverwriteResult.NoToMod:
					if (!string.IsNullOrEmpty(currentOwnerKey))
						m_hstDontOverwriteOwners.Add(currentOwnerKey);
					return false;
				default:
					throw new InvalidOperationException("The overwrite dialog returned an unsupported result.");
			}
		}

		private static bool IsFolderCovered(IEnumerable<string> p_enmFolders, string p_strDirectory)
		{
			foreach (string folder in p_enmFolders)
			{
				if (p_strDirectory.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
					p_strDirectory.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}
	}
}
