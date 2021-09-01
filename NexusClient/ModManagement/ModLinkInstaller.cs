namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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

        #region Properties

		/// <summary>
		/// Gets or sets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected VirtualModActivator VirtualModActivator { get; set; }

		#endregion

		#region Constructors

		public ModLinkInstaller(IVirtualModActivator virtualModActivator)
		{
			VirtualModActivator = (VirtualModActivator)virtualModActivator;
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
            var booLink = (TestOverwriteFileLink(mod, string.IsNullOrEmpty(sourceFile) ? baseFilePath : Path.GetFileName(sourceFile), out var priority, out var fileLinks));

			if (booLink != null)
            {
                if (booLink == true)
				{
					if (priority >= 0 && fileLinks != null && fileLinks.Count > 0)
					{
						VirtualModActivator.UpdateLinkListPriority(fileLinks);
						isSwitching = false;
					}

					return VirtualModActivator.AddFileLink(mod, baseFilePath, sourceFile, isSwitching, false, handlePlugin, 0);
				}

                VirtualModActivator.AddInactiveLink(mod, baseFilePath, ++priority);
            }

			return string.Empty;
		}

		private bool? TestOverwriteFileLink(IMod mod, string baseFilePath, out int priority, out List<IVirtualModLink> modLinks)
		{
            var fileLinkPriority = VirtualModActivator.CheckFileLink(baseFilePath, out var modCheck, out modLinks);
			priority = fileLinkPriority;
			var loweredPath = baseFilePath.ToLowerInvariant();

			if (fileLinkPriority >= 0)
			{
				if (_overwriteFolders.Contains(Path.GetDirectoryName(loweredPath)))
                {
                    return true;
                }

                if (_doNotOverwriteFolders.Contains(Path.GetDirectoryName(loweredPath)))
                {
                    return false;
                }

                if (_overwriteAll)
                {
                    return true;
                }

                if (_doNotOverwriteAll)
                {
                    return false;
                }
            }

			if (modCheck == mod)
            {
                return null;
            }

            if (modCheck == VirtualModActivator.DummyMod)
			{
				VirtualModActivator.OverwriteLooseFile(baseFilePath, Path.GetFileName(mod.Filename));
				return true;
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

                string strMessage = $"Data file '{baseFilePath}' has already been installed by '{modCheck.ModName}'";
                strMessage += Environment.NewLine + "Activate this mod's file instead?";

                switch (OverwriteForm.ShowDialog(strMessage, true, true))
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

            return true;
        }
	}
}
