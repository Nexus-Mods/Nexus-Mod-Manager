namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Forms;
    using Nexus.Client.Mods;
    using Nexus.Client.ModManagement.UI;

    /// <inheritdoc />
	public class ModLinkInstaller : IModLinkInstaller
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

		private bool? PromptForUnresolvedTxtConflict(IMod mod, string baseFilePath, ModInstallRoot installRoot, string loweredPath, bool looseFileConflict)
		{
			string strMessage = $"Data file '{baseFilePath}' already exists, but NMM cannot currently identify its owner.";
			strMessage += Environment.NewLine + "Activate this mod's file instead?";

			switch (ShowOwnedOverwriteDialog(strMessage, true, false))
			{
				case OverwriteResult.Yes:
					return AcceptUnresolvedTxtConflict(mod, baseFilePath, installRoot, looseFileConflict);
				case OverwriteResult.No:
					return DeclineUnresolvedTxtConflict(looseFileConflict);
				case OverwriteResult.NoToAll:
					_doNotOverwriteAll = true;
					return DeclineUnresolvedTxtConflict(looseFileConflict);
				case OverwriteResult.YesToAll:
					_overwriteAll = true;
					return AcceptUnresolvedTxtConflict(mod, baseFilePath, installRoot, looseFileConflict);
				case OverwriteResult.NoToGroup:
					RememberFolderChoice(loweredPath, false);
					return DeclineUnresolvedTxtConflict(looseFileConflict);
				case OverwriteResult.YesToGroup:
					RememberFolderChoice(loweredPath, true);
					return AcceptUnresolvedTxtConflict(mod, baseFilePath, installRoot, looseFileConflict);
				default:
					throw new Exception("Sanity check failed: OverwriteDialog returned a value not present in the OverwriteResult enum");
			}
		}

		private bool AcceptUnresolvedTxtConflict(IMod mod, string baseFilePath, ModInstallRoot installRoot, bool looseFileConflict)
		{
			if (looseFileConflict)
				VirtualModActivator.OverwriteLooseFile(baseFilePath, Path.GetFileName(mod.Filename), installRoot);

			return true;
		}

		private static bool? DeclineUnresolvedTxtConflict(bool looseFileConflict)
		{
			// Indexed conflicts can safely receive an inactive link. A loose file cannot: there is no
			// managed owner beneath it, so adding an inactive link would leave a broken ownership stack.
			return looseFileConflict ? (bool?)null : false;
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
			if (ModInstallFileFilter.IsIgnored(baseFilePath))
				return string.Empty;

            var booLink = (TestOverwriteFileLink(mod, baseFilePath, installRoot, out var priority, out var fileLinks));

			if (booLink != null)
            {
                if (booLink == true)
				{
					if (priority >= 0 && fileLinks != null && fileLinks.Count > 0)
					{
						VirtualModActivator.UpdateLinkListPriority(fileLinks);
						isSwitching = false;
					}

					string linkedFilePath = VirtualModActivator.AddFileLink(mod, baseFilePath, sourceFile, isSwitching, false, handlePlugin, 0, installRoot);
					if (string.IsNullOrEmpty(linkedFilePath))
					{
						if (VirtualModActivator.DisableLinkCreation)
							throw new InvalidOperationException("Virtual mod link creation is currently disabled.");

						throw new IOException(string.Format("Failed to deploy mod file '{0}'.", baseFilePath));
					}

					return linkedFilePath;
				}

                VirtualModActivator.AddInactiveLink(mod, baseFilePath, ++priority, installRoot);
            }

			return string.Empty;
		}

		private bool? TestOverwriteFileLink(IMod mod, string baseFilePath, ModInstallRoot installRoot, out int priority, out List<IVirtualModLink> modLinks)
		{
            var fileLinkPriority = VirtualModActivator.CheckFileLink(baseFilePath, installRoot, out var modCheck, out modLinks);
			priority = fileLinkPriority;
			var loweredPath = baseFilePath.ToLowerInvariant();
			bool isTxtFile = Path.GetExtension(baseFilePath).Equals(".txt", StringComparison.InvariantCultureIgnoreCase);
			bool promptLooseTxtConflict = modCheck == VirtualModActivator.DummyMod && _promptForTxtFileConflicts && isTxtFile;

			// Loose files normally bypass the overwrite-choice cache because CheckFileLink returns priority -1.
			// When TXT prompting is enabled, however, they are real user-visible conflicts and must honor
			// Yes/No to All and Yes/No to Folder just like managed conflicts do.
			if (fileLinkPriority >= 0 || promptLooseTxtConflict)
			{
				if (_overwriteFolders.Contains(Path.GetDirectoryName(loweredPath)))
                {
					if (promptLooseTxtConflict)
						VirtualModActivator.OverwriteLooseFile(baseFilePath, Path.GetFileName(mod.Filename), installRoot);
                    return true;
                }

                if (_doNotOverwriteFolders.Contains(Path.GetDirectoryName(loweredPath)))
                {
					// A loose file has no managed owner to sit above the new mod in the virtual-link stack.
					// Returning null skips the incoming file entirely instead of creating a bogus inactive link.
                    return promptLooseTxtConflict ? (bool?)null : false;
                }

                if (_overwriteAll)
                {
					if (promptLooseTxtConflict)
						VirtualModActivator.OverwriteLooseFile(baseFilePath, Path.GetFileName(mod.Filename), installRoot);
                    return true;
                }

                if (_doNotOverwriteAll)
                {
                    return promptLooseTxtConflict ? (bool?)null : false;
                }
            }

			if (modCheck == mod)
            {
                return null;
            }

            if (modCheck == VirtualModActivator.DummyMod)
			{
				if (!promptLooseTxtConflict)
				{
					VirtualModActivator.OverwriteLooseFile(baseFilePath, Path.GetFileName(mod.Filename), installRoot);
					return true;
				}

				return PromptForUnresolvedTxtConflict(mod, baseFilePath, installRoot, loweredPath, true);
			}

            if (modCheck != null)
            {
                var modFile = modCheck.Filename;
                var modFileId = modCheck.Id;
                
                if (!string.IsNullOrEmpty(modFileId))
                {
                    if (_overwriteMods.Contains(modFileId))
                    {
                        return true;
                    }

                    if (_doNotOverwriteMods.Contains(modFileId))
                    {
                        return false;
                    }
                }
                else
                {
                    if (_overwriteMods.Contains(modFile))
                    {
                        return true;
                    }

                    if (_doNotOverwriteMods.Contains(modFile))
                    {
                        return false;
                    }
                }

                if (!_promptForTxtFileConflicts && isTxtFile)
                {
                    return false;
				}

				string strMessage = $"Data file '{baseFilePath}' has already been installed by '{modCheck.ModName}'";
                strMessage += Environment.NewLine + "Activate this mod's file instead?";

                switch (ShowOwnedOverwriteDialog(strMessage, true, true))
                {
                    case OverwriteResult.Yes:
                        return true;
                    case OverwriteResult.No:
                        return false;
                    case OverwriteResult.NoToAll:
                        _doNotOverwriteAll = true;
                        return false;
                    case OverwriteResult.YesToAll:
                        _overwriteAll = true;
                        return true;
                    case OverwriteResult.NoToGroup:
                        Queue<string> folders = new Queue<string>();
                        folders.Enqueue(Path.GetDirectoryName(loweredPath));
                        
                        while (folders.Count > 0)
                        {
                            loweredPath = folders.Dequeue();
                            
                            if (!_overwriteFolders.Contains(loweredPath))
                            {
                                _doNotOverwriteFolders.Add(loweredPath);
                                
                                if (Directory.Exists(loweredPath))
                                {
                                    foreach (var s in Directory.GetDirectories(loweredPath))
                                    {
                                        folders.Enqueue(s.ToLowerInvariant());
                                    }
                                }
                            }
                        }

                        return false;
                    case OverwriteResult.YesToGroup:
                        folders = new Queue<string>();
                        folders.Enqueue(Path.GetDirectoryName(loweredPath));
                        
                        while (folders.Count > 0)
                        {
                            loweredPath = folders.Dequeue();
                            
                            if (!_doNotOverwriteFolders.Contains(loweredPath))
                            {
                                _overwriteFolders.Add(loweredPath);
                                if (Directory.Exists(loweredPath))
                                {
                                    foreach (var s in Directory.GetDirectories(loweredPath))
                                    {
                                        folders.Enqueue(s.ToLowerInvariant());
                                    }
                                }
                            }
                        }
                        return true;
                    case OverwriteResult.NoToMod:
                        modFile = modCheck.Filename;
                        modFileId = modCheck.Id;
                        
                        if (!string.IsNullOrEmpty(modFileId))
                        {
                            if (!_overwriteMods.Contains(modFileId))
                            {
                                _doNotOverwriteMods.Add(modFileId);
                            }
                        }
                        else
                        {
                            if (!_overwriteMods.Contains(modFile))
                            {
                                _doNotOverwriteMods.Add(modFile);
                            }
                        }
                        return false;
                    case OverwriteResult.YesToMod:
                        modFile = modCheck.Filename;
                        modFileId = modCheck.Id;
                        
                        if (!string.IsNullOrEmpty(modFileId))
                        {
                            if (!_doNotOverwriteMods.Contains(modFileId))
                            {
                                _overwriteMods.Add(modFileId);
                            }
                        }
                        else
                        {
                            if (!_doNotOverwriteMods.Contains(modFile))
                            {
                                _overwriteMods.Add(modFile);
                            }
                        }
                        return true;
                    default:
                        throw new Exception("Sanity check failed: OverwriteDialog returned a value not present in the OverwriteResult enum");
                }
            }

			// A virtual link can survive while its owning IMod can no longer be resolved (for example,
			// around deactivate/reactivate transitions). Without this guard TXT files silently overwrite
			// because modCheck is null even though an indexed conflict exists.
			if (_promptForTxtFileConflicts && isTxtFile && fileLinkPriority >= 0)
				return PromptForUnresolvedTxtConflict(mod, baseFilePath, installRoot, loweredPath, false);

            return true;
        }
	}
}
