namespace Nexus.Client.ModManagement.InstallationLog
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Nexus.Client.Mods;
    using Nexus.Client.Util.Collections;
    using Nexus.Transactions;
    using Nexus.Client.Util;

	public partial class InstallLog
	{
		/// <summary>
		/// Tracks the changes made to an <see cref="InstallLog"/> in the scope of a single
		/// <see cref="Transaction"/>. This also provides the means to commit and rollback the
		/// tracked changes.
		/// </summary>
		private class TransactionEnlistment : IEnlistmentNotification
		{
			private readonly ActiveModRegistry _activeModRegistry = new ActiveModRegistry();

			private readonly InstalledItemDictionary<string, object> _installedFiles;
			private readonly InstalledItemDictionary<string, object> _uninstalledFiles;

			private readonly InstalledItemDictionary<IniEdit, string> _installedIniEdits;
			private readonly InstalledItemDictionary<IniEdit, string> _replacedIniEdits;
			private readonly InstalledItemDictionary<IniEdit, string> _uninstalledIniEdits;

			private readonly InstalledItemDictionary<string, byte[]> _installedGameSpecificValueEdits;
			private readonly InstalledItemDictionary<string, byte[]> _replacedGameSpecificValueEdits;
			private readonly InstalledItemDictionary<string, byte[]> _uninstalledGameSpecificValueEdits;

			private readonly Set<string> _removedModKeys = new Set<string>();
			private bool _enlisted;

			#region Properties

			/// <summary>
			/// Gets the transaction into which we are enlisting.
			/// </summary>
			/// <value>The transaction into which we are enlisting.</value>
			protected Transaction CurrentTransaction { get; }

			/// <summary>
			/// Gets the install log whose actions are being transacted.
			/// </summary>
			/// <value>The install log whose actions are being transacted.</value>
			protected InstallLog EnlistedInstallLog { get; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="transaction">The transaction into which we are enlisting.</param>
			/// <param name="installLog">The install log whose actions are being transacted.</param>
			public TransactionEnlistment(Transaction transaction, InstallLog installLog)
			{
				CurrentTransaction = transaction;
				EnlistedInstallLog = installLog;
				_installedFiles = new InstalledItemDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				_uninstalledFiles = new InstalledItemDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

				_installedIniEdits = new InstalledItemDictionary<IniEdit, string>();
				_replacedIniEdits = new InstalledItemDictionary<IniEdit, string>();
				_uninstalledIniEdits = new InstalledItemDictionary<IniEdit, string>();

				_installedGameSpecificValueEdits = new InstalledItemDictionary<string, byte[]>();
				_replacedGameSpecificValueEdits = new InstalledItemDictionary<string, byte[]>();
				_uninstalledGameSpecificValueEdits = new InstalledItemDictionary<string, byte[]>();
			}

			#endregion

			#region IEnlistmentNotification Members

			/// <summary>
			/// Commits the changes to the install log.
			/// </summary>
			public void Commit()
			{
				// Merge registered mods
				foreach (var mod in _activeModRegistry.Registrations)
                {
                    EnlistedInstallLog._activeModRegistry.RegisterMod(mod.Key, mod.Value, _activeModRegistry.IsModHidden(mod.Key));
                }

                CommitFileChanges();
				CommitIniEditChanges();
				CommitGameSpecificValueEditChanges();

				// Remove registered mods
				foreach (var removedModKey in _removedModKeys)
                {
                    EnlistedInstallLog._activeModRegistry.DeregisterMod(removedModKey);
                }

                EnlistedInstallLog.SaveInstallLog();

				_enlisted = false;
				_activeModRegistry.Clear();
				_installedFiles.Clear();
				_installedIniEdits.Clear();
				_installedGameSpecificValueEdits.Clear();
			}

			/// <summary>
			/// Commits the changes made to installed files.
			/// </summary>
			protected void CommitFileChanges()
			{
				var installedFiles = EnlistedInstallLog._installedFiles;
				
                // Merge installed files
				foreach (var file in _installedFiles)
				{
					if (file.Installers.Count == 0 || GetModKey(OriginalValueMod).Equals(file.Installers.Peek().InstallerKey))
                    {
                        continue;
                    }

                    foreach (var isvMod in file.Installers)
					{
						installedFiles[file.Item].Remove(isvMod);
						installedFiles[file.Item].Push(isvMod);
					}
				}

				// Remove deleted files
				var filesToRemove = new List<string>();
				
                foreach (var insFile in _uninstalledFiles)
				{
					if (!installedFiles.ContainsItem(insFile.Item))
                    {
                        continue;
                    }

                    installedFiles[insFile.Item].RemoveRange(insFile.Installers);
					
                    if (installedFiles[insFile.Item].Count == 0 || GetModKey(OriginalValueMod).Equals(installedFiles[insFile.Item].Peek().InstallerKey))
                    {
                        filesToRemove.Add(insFile.Item);
                    }
                }
				
                // Remove all traces of removed mods from installed files
				// this step should be unnecessary, as if a mod has been removed
				// then all of it's files should have been entered in the dictionary
				// of uninstalled files, and already removed.
				// this is here, however, as a safeguard to help ensure the install log
				// doesn't get polluted with old entries.
				foreach (var file in installedFiles)
				{
					foreach (var strRemovedModKey in _removedModKeys)
                    {
                        file.Installers.Remove(strRemovedModKey);
                    }

                    if (file.Installers.Count == 0 || GetModKey(OriginalValueMod).Equals(file.Installers.Peek().InstallerKey))
                    {
                        filesToRemove.Add(file.Item);
                    }
                }

				filesToRemove.ForEach(x => installedFiles.Remove(x));
			}

			/// <summary>
			/// Commits the changes made to Ini file edits.
			/// </summary>
			protected void CommitIniEditChanges()
			{
				var installedIniEdits = EnlistedInstallLog._installedIniEdits;
				
                // Merge installed ini edits
				foreach (var iniEdit in _installedIniEdits)
				{
					if (iniEdit.Installers.Count == 0 || GetModKey(OriginalValueMod).Equals(iniEdit.Installers.Peek().InstallerKey))
                    {
                        continue;
                    }

                    foreach (var isvMod in iniEdit.Installers)
					{
						installedIniEdits[iniEdit.Item].Remove(isvMod);
						installedIniEdits[iniEdit.Item].Push(isvMod);
					}
				}
				
                // Replace replaced ini edits
				foreach (var iniEdit in _replacedIniEdits)
				{
					if (iniEdit.Installers.Count == 0 || GetModKey(OriginalValueMod).Equals(iniEdit.Installers.Peek().InstallerKey))
                    {
                        continue;
                    }

                    foreach (var mod in iniEdit.Installers)
                    {
                        installedIniEdits[iniEdit.Item].Find(x => x.Equals(mod)).Value = mod.Value;
                    }
                }
				
                // Remove deleted ini edits
				var lstIniEditsToRemove = new List<IniEdit>();
				
                foreach (var insIniEdit in _uninstalledIniEdits)
				{
					if (!installedIniEdits.ContainsItem(insIniEdit.Item))
                    {
                        continue;
                    }

                    installedIniEdits[insIniEdit.Item].RemoveRange(insIniEdit.Installers);
					
                    if (installedIniEdits[insIniEdit.Item].Count == 0 || GetModKey(OriginalValueMod).Equals(installedIniEdits[insIniEdit.Item].Peek().InstallerKey))
                    {
                        lstIniEditsToRemove.Add(insIniEdit.Item);
                    }
                }

				// Remove all traces of removed mods from installed ini edits
				// this step should be unnecessary, as if a mod has been removed
				// then all of it's ini edits should have been entered in the dictionary
				// of uninstalled ini edits, and already removed.
				// this is here, however, as a safeguard to help ensure the install log
				// doesn't get polluted with old entries.
				foreach (var insIniEdit in installedIniEdits)
				{
					foreach (var strRemovedModKey in _removedModKeys)
                    {
                        insIniEdit.Installers.Remove(strRemovedModKey);
                    }

                    if (insIniEdit.Installers.Count == 0 || GetModKey(OriginalValueMod).Equals(insIniEdit.Installers.Peek().InstallerKey))
                    {
                        lstIniEditsToRemove.Add(insIniEdit.Item);
                    }
                }

				lstIniEditsToRemove.ForEach(x => installedIniEdits.Remove(x));
			}

			/// <summary>
			/// Commits the changes made to game specific value edits.
			/// </summary>
			protected void CommitGameSpecificValueEditChanges()
			{
				var gameSpecificValueEdits = EnlistedInstallLog._gameSpecificValueEdits;
				
                // Merge installed game specific value edits
				foreach (var gameSpecificValueEdit in _installedGameSpecificValueEdits)
				{
					if (gameSpecificValueEdit.Installers.Count == 0 || GetModKey(OriginalValueMod).Equals(gameSpecificValueEdit.Installers.Peek().InstallerKey))
                    {
                        continue;
                    }

                    foreach (var mod in gameSpecificValueEdit.Installers)
					{
						gameSpecificValueEdits[gameSpecificValueEdit.Item].Remove(mod);
						gameSpecificValueEdits[gameSpecificValueEdit.Item].Push(mod);
					}
				}
				
                // Replace replaced game specific value edits
				foreach (var insGameSpecificValueEdit in _replacedGameSpecificValueEdits)
				{
					if (insGameSpecificValueEdit.Installers.Count == 0 || GetModKey(OriginalValueMod).Equals(insGameSpecificValueEdit.Installers.Peek().InstallerKey))
                    {
                        continue;
                    }

                    foreach (var mod in insGameSpecificValueEdit.Installers)
                    {
                        gameSpecificValueEdits[insGameSpecificValueEdit.Item].Find(x => x.Equals(mod)).Value = mod.Value;
                    }
                }

				// Remove deleted game specific value edits
				var lstGameSpecificValueEditsToRemove = new List<string>();
				
                foreach (var insGameSpecificValueEdit in _uninstalledGameSpecificValueEdits)
				{
					if (!gameSpecificValueEdits.ContainsItem(insGameSpecificValueEdit.Item))
                    {
                        continue;
                    }

                    gameSpecificValueEdits[insGameSpecificValueEdit.Item].RemoveRange(insGameSpecificValueEdit.Installers);
					
                    if (gameSpecificValueEdits[insGameSpecificValueEdit.Item].Count == 0 || GetModKey(OriginalValueMod).Equals(gameSpecificValueEdits[insGameSpecificValueEdit.Item].Peek().InstallerKey))
                    {
                        lstGameSpecificValueEditsToRemove.Add(insGameSpecificValueEdit.Item);
                    }
                }

				// Remove all traces of removed mods from installed game specific value edits
				// this step should be unnecessary, as if a mod has been removed
				// then all of it's game specific value edits should have been entered in the dictionary
				// of uninstalled game specific value edits, and already removed.
				// this is here, however, as a safeguard to help ensure the install log
				// doesn't get polluted with old entries.
				foreach (var insGameSpecificValueEdit in gameSpecificValueEdits)
				{
					foreach (var strRemovedModKey in _removedModKeys)
                    {
                        insGameSpecificValueEdit.Installers.Remove(strRemovedModKey);
                    }

                    if (insGameSpecificValueEdit.Installers.Count == 0 || GetModKey(OriginalValueMod).Equals(insGameSpecificValueEdit.Installers.Peek().InstallerKey))
                    {
                        lstGameSpecificValueEditsToRemove.Add(insGameSpecificValueEdit.Item);
                    }
                }

				lstGameSpecificValueEditsToRemove.ForEach(x => gameSpecificValueEdits.Remove(x));
			}

            /// <inheritdoc />
			public void Commit(Enlistment enlistment)
			{
				try
				{
					Commit();
					_enlistments.Remove(CurrentTransaction.TransactionInformation.LocalIdentifier);
					enlistment.Done();
				}
				catch (Exception e)
				{
					throw new TransactionException("Problem whilst committing Install Log.", e);
				}
			}

            /// <inheritdoc />
			public void InDoubt(Enlistment enlistment)
			{
				Rollback(enlistment);
			}

            /// <inheritdoc />
			public void Prepare(PreparingEnlistment preparingEnlistment)
			{
				preparingEnlistment.Prepared();
			}

			/// <inheritdoc />
			public void Rollback(Enlistment enlistment)
			{
				_enlisted = false;
				_activeModRegistry.Clear();
				_installedFiles.Clear();
				_installedIniEdits.Clear();
				_installedGameSpecificValueEdits.Clear();
				_enlistments.Remove(CurrentTransaction.TransactionInformation.LocalIdentifier);
				enlistment.Done();
			}

			#endregion

			/// <summary>
			/// Enlists the install log into the current transaction.
			/// </summary>
			private void Enlist()
			{
				if (!_enlisted)
				{
					CurrentTransaction.EnlistVolatile(this, EnlistmentOptions.None);
					_enlisted = true;
				}
			}

			#region Mod Tracking

			/// <summary>
			/// Adds a mod to the install log, in a transaction.
			/// </summary>
			/// <remarks>
			/// Adding a mod to the install log assigns it a key. Keys are used to track file and
			/// edit versions.
			/// 
			/// If there is no current transaction, the mod is added directly to the install log. Otherwise,
			/// the mod is added to a buffer than can later be committed or rolled back.
			/// </remarks>
			/// <param name="mod">The <see cref="IMod"/> being added.</param>
			/// <param name="isSpecial">Indicates that the mod is a special mod, internal to the
			/// install log, and show not be included in the list of active mods.</param>
			/// <returns>The key of the added mod.</returns>
			public string AddActiveMod(IMod mod, bool isSpecial)
			{
				var key = GetModKey(mod);
				
                if (string.IsNullOrEmpty(key))
				{
					do
					{
						key = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());
					} while (_activeModRegistry.DoesKeyExist(key) || EnlistedInstallLog._activeModRegistry.DoesKeyExist(key));
					
                    _activeModRegistry.RegisterMod(mod, key, isSpecial);
					_removedModKeys.Remove(key);
					
                    if (CurrentTransaction == null)
                    {
                        Commit();
                    }
                    else
                    {
                        Enlist();
                    }
                }

				return key;
			}

			/// <summary>
			/// Replaces a mod in the install log, in a transaction.
			/// </summary>
			/// <remarks>
			/// This replaces a mod in the install log without changing its key.
			/// </remarks>
			/// <param name="oldMod">The mod with to be replaced with the new mod in the install log.</param>
			/// <param name="newMod">The mod with which to replace the old mod in the install log.</param>
			public void ReplaceActiveMod(IMod oldMod, IMod newMod)
			{
				if (!_activeModRegistry.IsModRegistered(oldMod) && !EnlistedInstallLog._activeModRegistry.IsModRegistered(oldMod))
				{
					AddActiveMod(newMod, false);
					return;
				}

				var key = GetModKey(oldMod);
				_activeModRegistry.DeregisterMod(oldMod);
				_activeModRegistry.RegisterMod(newMod, key, false);
				_removedModKeys.Remove(key);
				
                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Gets the key that was assigned to the specified mod.
			/// </summary>
			/// <param name="mod">The mod whose key is to be retrieved.</param>
			/// <returns>The key that was assigned to the specified mod, or <c>null</c> if
			/// the specified mod has no key.</returns>
			public string GetModKey(IMod mod)
			{
                return _activeModRegistry.GetKey(mod) ?? EnlistedInstallLog._activeModRegistry.GetKey(mod);
			}

			/// <summary>
			/// Gets the mod identified by the given key.
			/// </summary>
			/// <param name="key">The key of the mod to be retrieved.</param>
			/// <returns>The mod identified by the given key, or <c>null</c> if
			/// no mod is identified by the given key.</returns>
			public IMod GetMod(string key)
			{
				return _activeModRegistry.GetMod(key) ?? EnlistedInstallLog._activeModRegistry.GetMod(key);
			}

			#endregion

			#region Uninstall

			/// <summary>
			/// Removes the mod, as well as entries for items installed by the given mod,
			/// from the install log.
			/// </summary>
			/// <param name="uninstaller">The mod to remove.</param>
			public void RemoveMod(IMod uninstaller)
			{
				var uninstallerKey = GetModKey(uninstaller);
				_removedModKeys.Add(uninstallerKey);

				// Remove the mod's files
				foreach (var file in _installedFiles)
                {
                    file.Installers.Remove(uninstallerKey);
                }

                foreach (var file in EnlistedInstallLog._installedFiles)
                {
                    if (file.Installers.Contains(uninstallerKey))
                    {
                        _uninstalledFiles[file.Item].Push(uninstallerKey, null);
                    }
                }

                // Remove the mod's ini edits
				foreach (var insIniEdit in _installedIniEdits)
                {
                    insIniEdit.Installers.Remove(uninstallerKey);
                }

                foreach (var insIniEdit in EnlistedInstallLog._installedIniEdits)
                {
                    if (insIniEdit.Installers.Contains(uninstallerKey))
                    {
                        _uninstalledIniEdits[insIniEdit.Item].Push(uninstallerKey, null);
                    }
                }

                // Remove the mod's game specific value edits
				foreach (var insGameSpecificValueEdit in _installedGameSpecificValueEdits)
                {
                    insGameSpecificValueEdit.Installers.Remove(uninstallerKey);
                }

                foreach (var insGameSpecificValueEdit in EnlistedInstallLog._gameSpecificValueEdits)
                {
                    if (insGameSpecificValueEdit.Installers.Contains(uninstallerKey))
                        _uninstalledGameSpecificValueEdits[insGameSpecificValueEdit.Item].Push(uninstallerKey, null);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			#endregion

			#region File Version Management

			/// <summary>
			/// Gets the ordered list of mod that have installed the specified file.
			/// </summary>
			/// <param name="path">The path of the file for which to retrieve the list of installing mods.</param>
			/// <returns>The ordered list of mods that have installed the specified file.</returns>
			protected InstallerStack<object> GetCurrentFileInstallers(string path)
			{
				var normalizedPath = FileUtil.NormalizePath(path);
				var installers = new InstallerStack<object>();
				
                if (EnlistedInstallLog._installedFiles.ContainsItem(normalizedPath))
                {
                    installers.PushRange(EnlistedInstallLog._installedFiles[normalizedPath]);
                }

                if (_installedFiles.ContainsItem(normalizedPath))
                {
                    foreach (var isvMod in _installedFiles[normalizedPath])
                    {
                        installers.Remove(isvMod);
                        installers.Push(isvMod);
                    }
                }

                if (_uninstalledFiles.ContainsItem(normalizedPath))
                {
                    foreach (var isvMod in _uninstalledFiles[normalizedPath])
                        installers.Remove(isvMod);
                }

                _removedModKeys.ForEach(x => installers.Remove(x));
				
                return installers;
			}

			/// <summary>
			/// Logs the specified data file as having been installed by the given mod.
			/// </summary>
			/// <param name="installingMod">The mod installing the specified data file.</param>
			/// <param name="dataFilePath">The file being installed.</param>
			public void AddDataFile(IMod installingMod, string dataFilePath)
			{
				var installingModKey = AddActiveMod(installingMod, false);
				var normalizedPath = FileUtil.NormalizePath(dataFilePath);
				_installedFiles[normalizedPath].Push(installingModKey, null);
				
                if (_uninstalledFiles.ContainsItem(normalizedPath))
                {
                    _uninstalledFiles[normalizedPath].Remove(installingModKey);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Removes the specified data file as having been installed by the given mod.
			/// </summary>
			/// <param name="uninstallingMod">The mod for which to remove the specified data file.</param>
			/// <param name="dataFilePath">The file being removed for the given mod.</param>
			public void RemoveDataFile(IMod uninstallingMod, string dataFilePath)
			{
				var uninstallingModKey = GetModKey(uninstallingMod);
				
                if (string.IsNullOrEmpty(uninstallingModKey))
                {
                    return;
                }

                var normalizedPath = FileUtil.NormalizePath(dataFilePath);
				_uninstalledFiles[normalizedPath].Push(uninstallingModKey, null);
				
                if (_installedFiles.ContainsItem(normalizedPath))
                {
                    _installedFiles[normalizedPath].Remove(uninstallingModKey);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Gets the mod that owns the specified file.
			/// </summary>
			/// <param name="path">The path of the file whose owner is to be retrieved.</param>
			/// <returns>The mod that owns the specified file.</returns>
			public IMod GetCurrentFileOwner(string path)
			{
				var currentFileInstallers = GetCurrentFileInstallers(path);
				
                return currentFileInstallers.Count == 0 ? null : GetMod(currentFileInstallers.Peek().InstallerKey);
            }

			/// <summary>
			/// Gets the mod that owned the specified file prior to the current owner.
			/// </summary>
			/// <param name="path">The path of the file whose previous owner is to be retrieved.</param>
			/// <returns>The mod that owned the specified file prior to the current owner.</returns>
			public IMod GetPreviousFileOwner(string path)
			{
				var currentFileInstallers = GetCurrentFileInstallers(path);
				
                if (currentFileInstallers.Count < 2)
                {
                    return null;
                }

                currentFileInstallers.Pop();
				
                return GetMod(currentFileInstallers.Peek().InstallerKey);
			}

			/// <summary>
			/// Logs that the specified data file is an original value.
			/// </summary>
			/// <remarks>
			/// Logging an original data file prepares it to be overwritten by a mod's file.
			/// </remarks>
			/// <param name="dataFilePath">The path of the data file to log as an
			/// original value.</param>
			public void LogOriginalDataFile(string dataFilePath)
			{
				if (GetCurrentFileOwner(dataFilePath) != null)
                {
                    return;
                }

                AddDataFile(OriginalValueMod, dataFilePath);
			}

			/// <summary>
			/// Gets the list of files that was installed by the given mod.
			/// </summary>
			/// <param name="installer">The mod whose installed files are to be returned.</param>
			/// <returns>The list of files that was installed by the given mod.</returns>
			public IList<string> GetInstalledModFiles(IMod installer)
			{
				var modFiles = new Set<string>(StringComparer.OrdinalIgnoreCase);
				var installerKey = GetModKey(installer);
				
                if (string.IsNullOrEmpty(installerKey) || _removedModKeys.Contains(installerKey))
                {
                    return modFiles;
                }

                modFiles.AddRange(from itm in _installedFiles
								  where itm.Installers.Contains(installerKey)
								  select itm.Item);
				modFiles.AddRange(from itm in EnlistedInstallLog._installedFiles
								  where itm.Installers.Contains(installerKey)
								  select itm.Item);
				modFiles.RemoveRange(from itm in _uninstalledFiles
									 where itm.Installers.Contains(installerKey)
									 select itm.Item);
				
                return modFiles;
			}

			/// <summary>
			/// Gets all of the mods that have installed the specified file.
			/// </summary>
			/// <remarks>
			/// The returned list is ordered by install date. In other words, the first
			/// mod in the list was the first to install the file, and the last mod in
			/// the list was the most recent. This implies that the current version of
			/// the specified file was installed by the last mod in the list. 
			/// </remarks>
			/// <param name="path">The path of the file whose installers are to be retrieved.</param>
			/// <returns>All of the mods that have installed the specified file.</returns>
			public IList<IMod> GetFileInstallers(string path)
			{
				var currentFileInstallers = GetCurrentFileInstallers(path);
				var installers = new List<IMod>();
				
                foreach (var ivlInstaller in currentFileInstallers)
                {
                    installers.Add(GetMod(ivlInstaller.InstallerKey));
                }

                return installers;
			}

			#endregion

			#region INI Version Management

			/// <summary>
			/// Gets the ordered list of mod that have installed the given ini edit.
			/// </summary>
			/// <param name="iniEdit">The ini edit for which to retrieve the list of installing mods.</param>
			/// <returns>The ordered list of mods that have installed the given ini edit.</returns>
			protected InstallerStack<string> GetCurrentIniEditInstallers(IniEdit iniEdit)
			{
				var installers = new InstallerStack<string>();
				
                if (EnlistedInstallLog._installedIniEdits.ContainsItem(iniEdit))
                {
                    installers.PushRange(EnlistedInstallLog._installedIniEdits[iniEdit]);
                }

                if (_installedIniEdits.ContainsItem(iniEdit))
                {
                    foreach (var mod in _installedIniEdits[iniEdit])
                    {
                        installers.Remove(mod);
                        installers.Push(mod);
                    }
                }

                if (_replacedIniEdits.ContainsItem(iniEdit))
                {
                    foreach (var isvMod in _replacedIniEdits[iniEdit])
                    {
                        installers.Find(x => x.Equals(isvMod)).Value = isvMod.Value;
                    }
                }

                if (_uninstalledIniEdits.ContainsItem(iniEdit))
                {
                    foreach (var mod in _uninstalledIniEdits[iniEdit])
                    {
                        installers.Remove(mod);
                    }
                }

                _removedModKeys.ForEach(x => installers.Remove(x));
				
                return installers;
			}

			/// <summary>
			/// Logs the specified INI edit as having been installed by the given mod.
			/// </summary>
			/// <param name="installingMod">The mod installing the specified INI edit.</param>
			/// <param name="settingsFileName">The name of the edited INI file.</param>
			/// <param name="section">The section containing the INI edit.</param>
			/// <param name="key">The key of the edited INI value.</param>
			/// <param name="value">The value installed by the mod.</param>
			public void AddIniEdit(IMod installingMod, string settingsFileName, string section, string key, string value)
			{
				var installingModKey = AddActiveMod(installingMod, false);
				var iniEdit = new IniEdit(settingsFileName, section, key);
				_installedIniEdits[iniEdit].Push(installingModKey, value);
				
                if (_uninstalledIniEdits.ContainsItem(iniEdit))
                {
                    _uninstalledIniEdits[iniEdit].Remove(installingModKey);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Replaces the edited value of the specified INI edit installed by the given mod.
			/// </summary>
			/// <param name="installingMod">The mod whose INI edit value is to be replaced.</param>
			/// <param name="settingsFileName">The name of the Ini value whose edited value is to be replaced.</param>
			/// <param name="section">The section of the Ini value whose edited value is to be replaced.</param>
			/// <param name="key">The key of the Ini value whose edited value is to be replaced.</param>
			/// <param name="value">The value with which to replace the edited value of the specified INI edit installed by the given mod.</param>
			public void ReplaceIniEdit(IMod installingMod, string settingsFileName, string section, string key, string value)
			{
				var installingModKey = GetModKey(installingMod);
				var iniEdit = new IniEdit(settingsFileName, section, key);
				_replacedIniEdits[iniEdit].Push(installingModKey, value);
				
                if (_uninstalledIniEdits.ContainsItem(iniEdit))
                {
                    _uninstalledIniEdits[iniEdit].Remove(installingModKey);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Removes the specified ini edit as having been installed by the given mod.
			/// </summary>
			/// <param name="uninstallingMod">The mod for which to remove the specified ini edit.</param>
			/// <param name="settingsFileName">The name of the edited INI file containing the INI edit being removed for the given mod.</param>
			/// <param name="section">The section containing the INI edit being removed for the given mod.</param>
			/// <param name="key">The key of the edited INI value whose edit is being removed for the given mod.</param>
			public void RemoveIniEdit(IMod uninstallingMod, string settingsFileName, string section, string key)
			{
				var uninstallingModKey = GetModKey(uninstallingMod);
				
                if (string.IsNullOrEmpty(uninstallingModKey))
                {
                    return;
                }

                var iniEdit = new IniEdit(settingsFileName, section, key);
				_uninstalledIniEdits[iniEdit].Push(uninstallingModKey, null);
				
                if (_installedIniEdits.ContainsItem(iniEdit))
                {
                    _installedIniEdits[iniEdit].Remove(uninstallingModKey);
                }

                if (_replacedIniEdits.ContainsItem(iniEdit))
                {
                    _replacedIniEdits[iniEdit].Remove(uninstallingModKey);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Gets the mod that owns the specified INI edit.
			/// </summary>
			/// <param name="settingsFileName">The name of the edited INI file.</param>
			/// <param name="section">The section containing the INI edit.</param>
			/// <param name="key">The key of the edited INI value.</param>
			/// <returns>The mod that owns the specified INI edit.</returns>
			public IMod GetCurrentIniEditOwner(string settingsFileName, string section, string key)
			{
				var iniEdit = new IniEdit(settingsFileName, section, key);
				var currentIniEditInstallers = GetCurrentIniEditInstallers(iniEdit);
				
                return currentIniEditInstallers.Count == 0 ? null : GetMod(currentIniEditInstallers.Peek().InstallerKey);
            }

			/// <summary>
			/// Gets the value of the specified key before it was most recently overwritten.
			/// </summary>
			/// <param name="settingsFileName">The Ini file containing the key whose previous value is to be retrieved.</param>
			/// <param name="section">The section containing the key whose previous value is to be retrieved.</param>
			/// <param name="key">The key whose previous value is to be retrieved.</param>
			/// <returns>The value of the specified key before it was most recently overwritten, or
			/// <c>null</c> if there was no previous value.</returns>
			public string GetPreviousIniValue(string settingsFileName, string section, string key)
			{
				var iniEdit = new IniEdit(settingsFileName, section, key);
				var currentIniEditInstallers = GetCurrentIniEditInstallers(iniEdit);
				
                if (currentIniEditInstallers.Count < 2)
                {
                    return null;
                }

                currentIniEditInstallers.Pop();
				
                return currentIniEditInstallers.Peek().Value;
			}

			/// <summary>
			/// Logs that the specified INI value is an original value.
			/// </summary>
			/// <remarks>
			/// Logging an original INI value prepares it to be overwritten by a mod's value.
			/// </remarks>
			/// <param name="settingsFileName">The name of the INI file containing the original value to log.</param>
			/// <param name="section">The section containing the original INI value to log.</param>
			/// <param name="key">The key of the original INI value to log.</param>
			/// <param name="value">The value installed by the mod.</param>
			public void LogOriginalIniValue(string settingsFileName, string section, string key, string value)
			{
				if (GetCurrentIniEditOwner(settingsFileName, section, key) != null)
                {
                    return;
                }

                AddIniEdit(OriginalValueMod, settingsFileName, section, key, value);
			}

			/// <summary>
			/// Gets the list of INI edit that were installed by the given mod.
			/// </summary>
			/// <param name="installer">The mod whose installed files are to be returned.</param>
			/// <returns>The list of files that was installed by the given mod.</returns>
			public IList<IniEdit> GetInstalledIniEdits(IMod installer)
			{
				var iniEdits = new Set<IniEdit>();
				var installerKey = GetModKey(installer);
				
                if (string.IsNullOrEmpty(installerKey) || _removedModKeys.Contains(installerKey))
                {
                    return iniEdits;
                }

                iniEdits.AddRange(from itm in _installedIniEdits
								  where itm.Installers.Contains(installerKey)
								  select itm.Item);
				iniEdits.AddRange(from itm in EnlistedInstallLog._installedIniEdits
								  where itm.Installers.Contains(installerKey)
								  select itm.Item);
				iniEdits.RemoveRange(from itm in _uninstalledIniEdits
									 where itm.Installers.Contains(installerKey)
									 select itm.Item);
				
                return iniEdits;
			}

			/// <summary>
			/// Gets all of the mods that have installed the specified Ini edit.
			/// </summary>
			/// <param name="settingsFileName">The Ini file containing the key whose installers are to be retrieved.</param>
			/// <param name="section">The section containing the key whose installers are to be retrieved.</param>
			/// <param name="key">The key whose installers are to be retrieved.</param>
			/// <returns>All of the mods that have installed the specified Ini edit.</returns>
			public IList<IMod> GetIniEditInstallers(string settingsFileName, string section, string key)
			{
				var iniEdit = new IniEdit(settingsFileName, section, key);
				var currentIniEditInstallers = GetCurrentIniEditInstallers(iniEdit);
				var installers = new List<IMod>();
				
                foreach (var ivlInstaller in currentIniEditInstallers)
                {
                    installers.Add(GetMod(ivlInstaller.InstallerKey));
                }

                return installers;
			}

			#endregion

			#region Game Specific Value Version Management

			/// <summary>
			/// Gets the ordered list of mod that have installed the specified game specific value edit.
			/// </summary>
			/// <param name="key">The key of the game specific value edit for which to retrieve the list of installing mods.</param>
			/// <returns>The ordered list of mods that have installed the specified game specific value edit.</returns>
			protected InstallerStack<byte[]> GetCurrentGameSpecificValueInstallers(string key)
			{
				var installers = new InstallerStack<byte[]>();
				
                if (EnlistedInstallLog._gameSpecificValueEdits.ContainsItem(key))
                {
                    installers.PushRange(EnlistedInstallLog._gameSpecificValueEdits[key]);
                }

                if (_installedGameSpecificValueEdits.ContainsItem(key))
                {
                    foreach (var isvMod in _installedGameSpecificValueEdits[key])
                    {
                        installers.Remove(isvMod);
                        installers.Push(isvMod);
                    }
                }

                if (_replacedGameSpecificValueEdits.ContainsItem(key))
                {
                    foreach (var isvMod in _replacedGameSpecificValueEdits[key])
                    {
                        installers.Find(x => x.Equals(isvMod)).Value = isvMod.Value;
                    }
                }

                if (_uninstalledGameSpecificValueEdits.ContainsItem(key))
                {
                    foreach (var isvMod in _uninstalledGameSpecificValueEdits[key])
                    {
                        installers.Remove(isvMod);
                    }
                }

                _removedModKeys.ForEach(x => installers.Remove(x));
				
                return installers;
			}

			/// <summary>
			/// Logs the specified Game Specific Value edit as having been installed by the given mod.
			/// </summary>
			/// <param name="installingMod">The mod installing the specified INI edit.</param>
			/// <param name="key">The key of the edited Game Specific Value.</param>
			/// <param name="value">The value installed by the mod.</param>
			public void AddGameSpecificValueEdit(IMod installingMod, string key, byte[] value)
			{
				var installingModKey = AddActiveMod(installingMod, false);
				_installedGameSpecificValueEdits[key].Push(installingModKey, value);
				
                if (_uninstalledGameSpecificValueEdits.ContainsItem(key))
                {
                    _uninstalledGameSpecificValueEdits[key].Remove(installingModKey);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Replaces the edited value of the specified game specific value edit installed by the given mod.
			/// </summary>
			/// <param name="installingMod">The mod whose game specific value edit value is to be replaced.</param>
			/// <param name="key">The key of the game specified value whose edited value is to be replaced.</param>
			/// <param name="value">The value with which to replace the edited value of the specified game specific value edit installed by the given mod.</param>
			public void ReplaceGameSpecificValueEdit(IMod installingMod, string key, byte[] value)
			{
				var installingModKey = GetModKey(installingMod);
				_replacedGameSpecificValueEdits[key].Push(installingModKey, value);
				
                if (_uninstalledGameSpecificValueEdits.ContainsItem(key))
                {
                    _uninstalledGameSpecificValueEdits[key].Remove(installingModKey);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Removes the specified Game Specific Value edit as having been installed by the given mod.
			/// </summary>
			/// <param name="uninstallingMod">The mod for which to remove the specified Game Specific Value edit.</param>
			/// <param name="key">The key of the Game Specific Value whose edit is being removed for the given mod.</param>
			public void RemoveGameSpecificValueEdit(IMod uninstallingMod, string key)
			{
				var uninstallingModKey = GetModKey(uninstallingMod);
				
                if (string.IsNullOrEmpty(uninstallingModKey))
                {
                    return;
                }

                _uninstalledGameSpecificValueEdits[key].Push(uninstallingModKey, null);
				
                if (_installedGameSpecificValueEdits.ContainsItem(key))
                {
                    _installedGameSpecificValueEdits[key].Remove(uninstallingModKey);
                }

                if (_replacedGameSpecificValueEdits.ContainsItem(key))
                {
                    _replacedGameSpecificValueEdits[key].Remove(uninstallingModKey);
                }

                if (CurrentTransaction == null)
                {
                    Commit();
                }
                else
                {
                    Enlist();
                }
            }

			/// <summary>
			/// Gets the mod that owns the specified Game Specific Value edit.
			/// </summary>
			/// <param name="key">The key of the edited Game Specific Value.</param>
			/// <returns>The mod that owns the specified Game Specific Value edit.</returns>
			public IMod GetCurrentGameSpecificValueEditOwner(string key)
			{
				var currentGameSpecificValueInstallers = GetCurrentGameSpecificValueInstallers(key);
				
                return currentGameSpecificValueInstallers.Count == 0 ? null : GetMod(currentGameSpecificValueInstallers.Peek().InstallerKey);
            }

			/// <summary>
			/// Gets the value of the specified key before it was most recently overwritten.
			/// </summary>
			/// <param name="key">The key of the edited Game Specific Value.</param>
			/// <returns>The value of the specified key before it was most recently overwritten, or
			/// <c>null</c> if there was no previous value.</returns>
			public byte[] GetPreviousGameSpecificValue(string key)
			{
				var currentGameSpecificValueInstallers = GetCurrentGameSpecificValueInstallers(key);
				
                if (currentGameSpecificValueInstallers.Count < 2)
                {
                    return null;
                }

                currentGameSpecificValueInstallers.Pop();
				
                return currentGameSpecificValueInstallers.Peek().Value;
			}

			/// <summary>
			/// Logs that the specified Game Specific Value is an original value.
			/// </summary>
			/// <remarks>
			/// Logging an original Game Specific Value prepares it to be overwritten by a mod's value.
			/// </remarks>
			/// <param name="key">The key of the edited Game Specific Value.</param>
			/// <param name="value">The original value.</param>
			public void LogOriginalGameSpecificValue(string key, byte[] value)
			{
				if (GetCurrentGameSpecificValueEditOwner(key) != null)
                {
                    return;
                }

                AddGameSpecificValueEdit(OriginalValueMod, key, value);
			}

			/// <summary>
			/// Gets the list of Game Specific Value edited keys that were installed by the given mod.
			/// </summary>
			/// <param name="installer">The mod whose installed edits are to be returned.</param>
			/// <returns>The list of edited keys that was installed by the given mod.</returns>
			public IList<string> GetInstalledGameSpecificValueEdits(IMod installer)
			{
				var gameSpecificValues = new Set<string>();
				var installerKey = GetModKey(installer);
				
                if (string.IsNullOrEmpty(installerKey) || _removedModKeys.Contains(installerKey))
                {
                    return gameSpecificValues;
                }

                gameSpecificValues.AddRange(from itm in _installedGameSpecificValueEdits
												where itm.Installers.Contains(installerKey)
												select itm.Item);
				gameSpecificValues.AddRange(from itm in EnlistedInstallLog._gameSpecificValueEdits
												where itm.Installers.Contains(installerKey)
												select itm.Item);
				gameSpecificValues.RemoveRange(from itm in _uninstalledGameSpecificValueEdits
												  where itm.Installers.Contains(installerKey)
												  select itm.Item);
				
                return gameSpecificValues;
			}

			/// <summary>
			/// Gets all of the mods that have installed the specified game specific value edit.
			/// </summary>
			/// <param name="key">The key whose installers are to be retrieved.</param>
			/// <returns>All of the mods that have installed the specified game specific value edit.</returns>
			public IList<IMod> GetGameSpecificValueEditInstallers(string key)
			{
				var currentGameSpecificValueInstallers = GetCurrentGameSpecificValueInstallers(key);
				var installers = new List<IMod>();
				
                foreach (var ivlInstaller in currentGameSpecificValueInstallers)
                {
                    installers.Add(GetMod(ivlInstaller.InstallerKey));
                }

                return installers;
			}

			#endregion
		}
	}
}
