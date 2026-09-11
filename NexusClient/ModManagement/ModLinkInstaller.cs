namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Forms;
    using Nexus.Client.Mods;
    using Nexus.Client.ModManagement.UI;

    /// <inheritdoc />
	public class ModLinkInstaller : IModLinkInstaller, IModLinkInstallDecisionSupport
	{
		private readonly List<string> _overwriteFolders = new List<string>();
		private readonly List<string> _doNotOverwriteFolders = new List<string>();
		private readonly List<string> _overwriteMods = new List<string>();
		private readonly List<string> _doNotOverwriteMods = new List<string>();
		private bool _doNotOverwriteAll;
		private bool _overwriteAll;
		private readonly bool _promptForTxtFileConflicts;

        #region Properties

		/// <summary>
		/// Gets or sets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected VirtualModActivator VirtualModActivator { get; set; }

		#endregion

		#region Constructors

		public ModLinkInstaller(IVirtualModActivator virtualModActivator)
			: this(virtualModActivator, false)
		{
		}

		public ModLinkInstaller(IVirtualModActivator virtualModActivator, bool promptForTxtFileConflicts)
		{
			VirtualModActivator = (VirtualModActivator)virtualModActivator;
			_promptForTxtFileConflicts = promptForTxtFileConflicts;
		}

		private static OverwriteResult ShowOwnedOverwriteDialog(string message, bool allowPerGroup, bool allowPerMod)
		{
			Form owner = FindMainForm();
			if (owner == null || owner.IsDisposed || !owner.IsHandleCreated)
				return OverwriteForm.ShowDialog(message, allowPerGroup, allowPerMod);

			if (owner.InvokeRequired)
			{
				OverwriteResult result = OverwriteResult.No;
				owner.Invoke((MethodInvoker)(() =>
					result = OverwriteForm.ShowDialog(owner, message, allowPerGroup, allowPerMod)));
				return result;
			}

			return OverwriteForm.ShowDialog(owner, message, allowPerGroup, allowPerMod);
		}

		private static Form FindMainForm()
		{
			Form fallback = null;
			for (int index = 0; index < Application.OpenForms.Count; index++)
			{
				Form form = Application.OpenForms[index];
				if (form == null || form.IsDisposed)
					continue;

				if (fallback == null)
					fallback = form;

				if (String.Equals(form.GetType().Name, "MainForm", StringComparison.Ordinal))
					return form;
			}

			return fallback;
		}

		private void RememberFolderChoice(string loweredPath, bool overwrite)
		{
			List<string> selectedFolders = overwrite ? _overwriteFolders : _doNotOverwriteFolders;
			List<string> oppositeFolders = overwrite ? _doNotOverwriteFolders : _overwriteFolders;
			Queue<string> folders = new Queue<string>();
			folders.Enqueue(Path.GetDirectoryName(loweredPath));

			while (folders.Count > 0)
			{
				string folder = folders.Dequeue();
				if (oppositeFolders.Contains(folder) || selectedFolders.Contains(folder))
					continue;

				selectedFolders.Add(folder);
				if (Directory.Exists(folder))
				{
					foreach (var subFolder in Directory.GetDirectories(folder))
						folders.Enqueue(subFolder.ToLowerInvariant());
				}
			}
		}

		#endregion

		/// <inheritdoc />
		public string AddFileLink(IMod mod, string baseFilePath, string sourceFile, bool isSwitching)
		{
			return AddFileLink(mod, baseFilePath, sourceFile, isSwitching, false);
		}

        /// <inheritdoc />
		public string AddFileLink(IMod mod, string baseFilePath, string sourceFile, bool isSwitching, bool handlePlugin)
		{
			return AddFileLink(mod, baseFilePath, sourceFile, isSwitching, handlePlugin, ModInstallRoot.Default);
		}

		public string AddFileLink(IMod mod, string baseFilePath, string sourceFile, bool isSwitching, bool handlePlugin, ModInstallRoot installRoot)
		{
			return AddFileLinkInternal(mod, baseFilePath, sourceFile, isSwitching, handlePlugin, installRoot, null);
		}

		/// <summary>
		/// Resolves any user-visible overwrite choice required for a virtual link without deploying the link.
		/// </summary>
		/// <param name="p_modMod">The mod whose file will be linked.</param>
		/// <param name="p_strBaseFilePath">The logical destination path of the file.</param>
		/// <param name="p_mirInstallRoot">The installation root containing the destination.</param>
		/// <returns>The resolved overwrite choice, or an unresolved decision when no prompt is required.</returns>
		public ModLinkInstallDecision ResolveFileLinkDecision(IMod p_modMod, string p_strBaseFilePath, ModInstallRoot p_mirInstallRoot)
		{
			int intPriority;
			List<IVirtualModLink> lstFileLinks;
			ModLinkInstallDecision midDecision;
			bool? booLink = TestOverwriteFileLink(p_modMod, p_strBaseFilePath, p_mirInstallRoot, out intPriority, out lstFileLinks, true, null, out midDecision);
			bool booCreatesActiveLink = booLink == true;

			// A null result can mean that the file is already active for the same mod.
			// Capture that effective outcome so projected state can expose the staged content.
			if (!booLink.HasValue)
			{
				IMod modCurrentOwner;
				List<IVirtualModLink> lstCurrentLinks;
				VirtualModActivator.CheckFileLink(p_strBaseFilePath, p_mirInstallRoot, out modCurrentOwner, out lstCurrentLinks);
				if (modCurrentOwner == p_modMod)
					booLink = true;
			}

			return midDecision.WithLinkOutcome(booLink, booCreatesActiveLink);
		}

		/// <summary>
		/// Deploys a virtual link using an overwrite choice that was resolved before deployment.
		/// </summary>
		/// <param name="p_modMod">The mod whose file will be linked.</param>
		/// <param name="p_strBaseFilePath">The logical destination path of the file.</param>
		/// <param name="p_strSourceFile">The staged source path.</param>
		/// <param name="p_booIsSwitching">Whether the operation is part of a mod switch.</param>
		/// <param name="p_booHandlePlugin">Whether plugin handling should be performed.</param>
		/// <param name="p_mirInstallRoot">The installation root containing the destination.</param>
		/// <param name="p_midDecision">The overwrite choice resolved before deployment.</param>
		/// <returns>The linked file path, or an empty string when the incoming file is not activated.</returns>
		public string AddFileLinkWithResolvedDecision(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching, bool p_booHandlePlugin, ModInstallRoot p_mirInstallRoot, ModLinkInstallDecision p_midDecision)
		{
			return AddFileLinkInternal(p_modMod, p_strBaseFilePath, p_strSourceFile, p_booIsSwitching, p_booHandlePlugin, p_mirInstallRoot, p_midDecision);
		}

		/// <summary>
		/// Evaluates the current virtual-link state and deploys the incoming file when the resulting decision permits it.
		/// </summary>
		/// <param name="p_modMod">The mod whose file will be linked.</param>
		/// <param name="p_strBaseFilePath">The logical destination path of the file.</param>
		/// <param name="p_strSourceFile">The staged source path.</param>
		/// <param name="p_booIsSwitching">Whether the operation is part of a mod switch.</param>
		/// <param name="p_booHandlePlugin">Whether plugin handling should be performed.</param>
		/// <param name="p_mirInstallRoot">The installation root containing the destination.</param>
		/// <param name="p_midDecision">An optional overwrite choice resolved before deployment.</param>
		/// <returns>The linked file path, or an empty string when the incoming file is not activated.</returns>
		private string AddFileLinkInternal(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching, bool p_booHandlePlugin, ModInstallRoot p_mirInstallRoot, ModLinkInstallDecision p_midDecision)
		{
			if (ModInstallFileFilter.IsIgnored(p_strBaseFilePath))
				return string.Empty;

			int intPriority;
			List<IVirtualModLink> lstFileLinks;
			ModLinkInstallDecision midResolvedDecision;
			bool? booLink = TestOverwriteFileLink(p_modMod, p_strBaseFilePath, p_mirInstallRoot, out intPriority, out lstFileLinks, false, p_midDecision, out midResolvedDecision);
			if (booLink != null)
			{
				if (booLink == true)
				{
					if (intPriority >= 0 && lstFileLinks != null && lstFileLinks.Count > 0)
					{
						VirtualModActivator.UpdateLinkListPriority(lstFileLinks);
						p_booIsSwitching = false;
					}

					string strLinkedFilePath = VirtualModActivator.AddFileLink(p_modMod, p_strBaseFilePath, p_strSourceFile, p_booIsSwitching, false, p_booHandlePlugin, 0, p_mirInstallRoot);
					if (string.IsNullOrEmpty(strLinkedFilePath))
					{
						if (VirtualModActivator.DisableLinkCreation)
							throw new InvalidOperationException("Virtual mod link creation is currently disabled.");

						throw new IOException(string.Format("Failed to deploy mod file '{0}'.", p_strBaseFilePath));
					}

					return strLinkedFilePath;
				}

				VirtualModActivator.AddInactiveLink(p_modMod, p_strBaseFilePath, ++intPriority, p_mirInstallRoot);
			}

			return string.Empty;
		}

		/// <summary>
		/// Evaluates whether an incoming virtual link may replace the current owner and optionally resolves the user choice without mutating deployment state.
		/// </summary>
		/// <param name="p_modMod">The mod whose file will be linked.</param>
		/// <param name="p_strBaseFilePath">The logical destination path of the file.</param>
		/// <param name="p_mirInstallRoot">The installation root containing the destination.</param>
		/// <param name="p_intPriority">Receives the current virtual-link priority.</param>
		/// <param name="p_lstModLinks">Receives the existing virtual-link stack.</param>
		/// <param name="p_booDecisionOnly">Whether deployment-side effects must be suppressed.</param>
		/// <param name="p_midPreResolvedDecision">An optional overwrite choice resolved during planning.</param>
		/// <param name="p_midResolvedDecision">Receives any overwrite choice resolved by this evaluation.</param>
		/// <returns><c>true</c> to activate the incoming link, <c>false</c> to keep it inactive, or <c>null</c> to skip it.</returns>
		private bool? TestOverwriteFileLink(IMod p_modMod, string p_strBaseFilePath, ModInstallRoot p_mirInstallRoot, out int p_intPriority, out List<IVirtualModLink> p_lstModLinks, bool p_booDecisionOnly, ModLinkInstallDecision p_midPreResolvedDecision, out ModLinkInstallDecision p_midResolvedDecision)
		{
			int intFileLinkPriority = VirtualModActivator.CheckFileLink(p_strBaseFilePath, p_mirInstallRoot, out IMod modCheck, out p_lstModLinks);
			p_intPriority = intFileLinkPriority;
			p_midResolvedDecision = new ModLinkInstallDecision();
			string strLoweredPath = p_strBaseFilePath.ToLowerInvariant();
			bool booIsTxtFile = Path.GetExtension(p_strBaseFilePath).Equals(".txt", StringComparison.InvariantCultureIgnoreCase);
			bool booPromptLooseTxtConflict = modCheck == VirtualModActivator.DummyMod && _promptForTxtFileConflicts && booIsTxtFile;

			if (intFileLinkPriority >= 0 || booPromptLooseTxtConflict)
			{
				if (_overwriteFolders.Contains(Path.GetDirectoryName(strLoweredPath)))
				{
					if (booPromptLooseTxtConflict && !p_booDecisionOnly)
						VirtualModActivator.OverwriteLooseFile(p_strBaseFilePath, Path.GetFileName(p_modMod.Filename), p_mirInstallRoot);
					return true;
				}

				if (_doNotOverwriteFolders.Contains(Path.GetDirectoryName(strLoweredPath)))
					return booPromptLooseTxtConflict ? (bool?)null : false;

				if (_overwriteAll)
				{
					if (booPromptLooseTxtConflict && !p_booDecisionOnly)
						VirtualModActivator.OverwriteLooseFile(p_strBaseFilePath, Path.GetFileName(p_modMod.Filename), p_mirInstallRoot);
					return true;
				}

				if (_doNotOverwriteAll)
					return booPromptLooseTxtConflict ? (bool?)null : false;
			}

			if (modCheck == p_modMod)
				return null;

			if (modCheck == VirtualModActivator.DummyMod)
			{
				if (!booPromptLooseTxtConflict)
				{
					if (!p_booDecisionOnly)
						VirtualModActivator.OverwriteLooseFile(p_strBaseFilePath, Path.GetFileName(p_modMod.Filename), p_mirInstallRoot);
					return true;
				}

				bool booOverwrite = ResolveOverwriteChoice(
					$"Data file '{p_strBaseFilePath}' already exists, but NMM cannot currently identify its owner.{Environment.NewLine}Activate this mod's file instead?",
					true,
					false,
					strLoweredPath,
					null,
					p_midPreResolvedDecision,
					out p_midResolvedDecision);
				if (booOverwrite && !p_booDecisionOnly)
					VirtualModActivator.OverwriteLooseFile(p_strBaseFilePath, Path.GetFileName(p_modMod.Filename), p_mirInstallRoot);
				return booOverwrite ? (bool?)true : null;
			}

			if (modCheck != null)
			{
				string strModFile = modCheck.Filename;
				string strModFileId = modCheck.Id;
				if (!string.IsNullOrEmpty(strModFileId))
				{
					if (_overwriteMods.Contains(strModFileId))
						return true;
					if (_doNotOverwriteMods.Contains(strModFileId))
						return false;
				}
				else
				{
					if (_overwriteMods.Contains(strModFile))
						return true;
					if (_doNotOverwriteMods.Contains(strModFile))
						return false;
				}

				if (!_promptForTxtFileConflicts && booIsTxtFile)
					return false;

				string strMessage = $"Data file '{p_strBaseFilePath}' has already been installed by '{modCheck.ModName}'";
				strMessage += Environment.NewLine + "Activate this mod's file instead?";
				return ResolveOverwriteChoice(strMessage, true, true, strLoweredPath, modCheck, p_midPreResolvedDecision, out p_midResolvedDecision);
			}

			// Indexed TXT conflicts can outlive their owning IMod around deactivate/reactivate transitions.
			if (_promptForTxtFileConflicts && booIsTxtFile && intFileLinkPriority >= 0)
			{
				string strMessage = $"Data file '{p_strBaseFilePath}' already exists, but NMM cannot currently identify its owner.";
				strMessage += Environment.NewLine + "Activate this mod's file instead?";
				return ResolveOverwriteChoice(strMessage, true, false, strLoweredPath, null, p_midPreResolvedDecision, out p_midResolvedDecision);
			}

			return true;
		}

		/// <summary>
		/// Resolves an overwrite prompt and updates the persistent choice state used by subsequent conflicts.
		/// </summary>
		/// <param name="p_strMessage">The overwrite message shown to the user.</param>
		/// <param name="p_booAllowPerGroup">Whether folder-scoped choices are available.</param>
		/// <param name="p_booAllowPerMod">Whether mod-scoped choices are available.</param>
		/// <param name="p_strLoweredPath">The normalized destination path used for folder-scoped choices.</param>
		/// <param name="p_modCurrentOwner">The current managed owner, when one exists.</param>
		/// <param name="p_midPreResolvedDecision">An optional overwrite choice resolved during planning.</param>
		/// <param name="p_midResolvedDecision">Receives the choice resolved by this call.</param>
		/// <returns><c>true</c> when the incoming file should overwrite the conflict; otherwise, <c>false</c>.</returns>
		private bool ResolveOverwriteChoice(string p_strMessage, bool p_booAllowPerGroup, bool p_booAllowPerMod, string p_strLoweredPath, IMod p_modCurrentOwner, ModLinkInstallDecision p_midPreResolvedDecision, out ModLinkInstallDecision p_midResolvedDecision)
		{
			if (p_midPreResolvedDecision != null && p_midPreResolvedDecision.IsResolved)
			{
				p_midResolvedDecision = p_midPreResolvedDecision;
				return p_midPreResolvedDecision.Overwrite;
			}

			OverwriteResult owrResult = ShowOwnedOverwriteDialog(p_strMessage, p_booAllowPerGroup, p_booAllowPerMod);
			bool booOverwrite;
			switch (owrResult)
			{
				case OverwriteResult.Yes:
					booOverwrite = true;
					break;
				case OverwriteResult.No:
					booOverwrite = false;
					break;
				case OverwriteResult.NoToAll:
					_doNotOverwriteAll = true;
					booOverwrite = false;
					break;
				case OverwriteResult.YesToAll:
					_overwriteAll = true;
					booOverwrite = true;
					break;
				case OverwriteResult.NoToGroup:
					RememberFolderChoice(p_strLoweredPath, false);
					booOverwrite = false;
					break;
				case OverwriteResult.YesToGroup:
					RememberFolderChoice(p_strLoweredPath, true);
					booOverwrite = true;
					break;
				case OverwriteResult.NoToMod:
					RememberModChoice(p_modCurrentOwner, false);
					booOverwrite = false;
					break;
				case OverwriteResult.YesToMod:
					RememberModChoice(p_modCurrentOwner, true);
					booOverwrite = true;
					break;
				default:
					throw new Exception("Sanity check failed: OverwriteDialog returned a value not present in the OverwriteResult enum");
			}

			p_midResolvedDecision = new ModLinkInstallDecision(booOverwrite);
			return booOverwrite;
		}

		/// <summary>
		/// Records a mod-scoped overwrite choice for subsequent conflicts owned by the same mod.
		/// </summary>
		/// <param name="p_modCurrentOwner">The mod that currently owns the conflicting file.</param>
		/// <param name="p_booOverwrite">Whether conflicts owned by the mod should be overwritten.</param>
		private void RememberModChoice(IMod p_modCurrentOwner, bool p_booOverwrite)
		{
			if (p_modCurrentOwner == null)
				return;

			string strModFile = p_modCurrentOwner.Filename;
			string strModFileId = p_modCurrentOwner.Id;
			List<string> lstSelectedMods = p_booOverwrite ? _overwriteMods : _doNotOverwriteMods;
			List<string> lstOppositeMods = p_booOverwrite ? _doNotOverwriteMods : _overwriteMods;
			string strKey = !string.IsNullOrEmpty(strModFileId) ? strModFileId : strModFile;
			if (!lstOppositeMods.Contains(strKey) && !lstSelectedMods.Contains(strKey))
				lstSelectedMods.Add(strKey);
		}

	}
}
