using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement.InstallationLog.Upgraders;
using Nexus.Client.Util;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.InstallationLog
{
	public partial class InstallLog
	{
		private InstallLogSqliteStore _store;
		/// <summary>Detects either legacy input or an authoritative SQLite store, including a missing-store recovery marker.</summary>
		public static bool HasPersistedLog(string logPath)
		{
			return File.Exists(logPath) || InstallLogSqliteStore.IsAuthoritative(logPath);
		}

		/// <summary>Exports a consistent committed snapshot only when requested by backup/profile compatibility code.</summary>
		public static void ExportSnapshot(string logPath, string destination)
		{
			XDocument document = InstallLogSqliteStore.ReadDocument(logPath);
			SaveInstallLogAtomically(new XStreamingElement(document.Root.Name, document.Root.Attributes(), document.Root.Nodes()), destination);
		}

		/// <summary>Restores only validated SQLite backups once XML has ceased to be authoritative.</summary>
		private static bool RestoreDatabase(string logPath)
		{
			string database = InstallLogSqliteStore.DatabasePath(logPath);
			string suffix = "." + FileUtil.CreateTempToken() + ".bad";
			for (int i = 0; i < 6; i++)
			{
				string backup = database + ".bak" + (i == 0 ? "" : i.ToString());
				if (!File.Exists(backup))
					continue;
				try
				{
					var candidate = new InstallLogSqliteStore(backup);
					if (candidate.ReadVersion() != CurrentVersion) continue;
					candidate.Read();
				}
				catch (Exception)
				{
					continue;
				}

				string temporary = database + "." + FileUtil.CreateTempToken() + ".tmp";
				try
				{
					new InstallLogSqliteStore(backup).BackupTo(temporary);
					ReplaceRestoredDatabase(temporary, database, suffix);
					NativeStoreAuthority.Mark(database);
					return true;
				}
				finally
				{
					if (File.Exists(temporary))
						File.Delete(temporary);
				}
			}

			return false;
		}

		/// <summary>Preserves failed-store journals with their database so they cannot be replayed over a restored backup.</summary>
		private static void ReplaceRestoredDatabase(string temporary, string database, string suffix)
		{
			var preserved = new List<string>();
			try
			{
				foreach (string extension in new[] { "-journal", "-wal", "-shm" })
				{
					if (!File.Exists(database + extension)) continue;
					File.Move(database + extension, database + suffix + extension);
					preserved.Add(extension);
				}
				if (File.Exists(database)) File.Replace(temporary, database, database + suffix);
				else File.Move(temporary, database);
			}
			catch
			{
				foreach (string extension in preserved)
					File.Move(database + suffix + extension, database + extension);
				throw;
			}
		}

		/// <summary>Imports a legacy backup through an isolated database and the restore transaction's file rollback.</summary>
		internal static void RestoreSnapshot(string snapshot, string logPath, string modDirectory, ModRegistry registry, TxFileManager fileManager)
		{
			string temporaryXml = Path.Combine(Path.GetTempPath(), FileUtil.CreateTempToken() + ".xml");
			string temporaryDatabase = Path.ChangeExtension(temporaryXml, ".sqlite");
			try
			{
				File.Copy(snapshot, temporaryXml);
				new InstallLogUpgrader().UpgradeSnapshot(temporaryXml, modDirectory, registry);
				InstallLogSqliteStore.CreateFromDocument(temporaryDatabase, XDocument.Load(temporaryXml));
				if (!Directory.Exists(Path.GetDirectoryName(logPath)))
					fileManager.CreateDirectory(Path.GetDirectoryName(logPath));
				fileManager.Copy(temporaryDatabase, InstallLogSqliteStore.DatabasePath(logPath), true);
				// The marker participates in rollback when restoring to a previously unmigrated store.
				string marker = InstallLogSqliteStore.DatabasePath(logPath) + ".authority";
				if (!File.Exists(marker))
					fileManager.WriteAllText(marker, "nmm-ce.native-sqlite-authority/1");
			}
			finally
			{
				if (File.Exists(temporaryXml))
					File.Delete(temporaryXml);
				if (File.Exists(temporaryDatabase))
					File.Delete(temporaryDatabase);
			}
		}

		/// <summary>Serializes only a changed registration, preserving its stable key and installation context.</summary>
		private XElement SerializeMod(IMod mod, string key)
		{
			return new XElement("mod", new XAttribute("key", key), new XAttribute("path", mod is DummyMod ? mod.ModArchivePath : mod.ModArchivePath.Substring(ModInstallDirectory.Length)), CreateInstallRootAttribute(GetModInstallRootByKey(key)), CreateInstallMethodAttribute(GetModInstallMethodByKey(key)), _activeModRegistry.IsModHidden(mod) ? new XAttribute("hidden", true) : null, new XElement("version", new XAttribute("machineVersion", mod.MachineVersion ?? new Version()), mod.HumanReadableVersion ?? ""), new XElement("name", mod.ModName), new XElement("installDate", mod.InstallDate ?? DateTime.Now.ToString()));
		}

		private partial class TransactionEnlistment
		{
			private HashSet<string> _affectedFiles;
			private HashSet<IniEdit> _affectedIni;
			private HashSet<string> _affectedValues;
			private HashSet<ModDeploymentTarget> _affectedTargets;
			/// <summary>Collects transaction-local keys and indexed removal references before mutating committed memory.</summary>
			private void CollectAffectedRecords()
			{
				_affectedFiles = new HashSet<string>(_installedFiles.Select(x => x.Item).Concat(_uninstalledFiles.Select(x => x.Item)), StringComparer.OrdinalIgnoreCase);
				_affectedIni = new HashSet<IniEdit>(_installedIniEdits.Select(x => x.Item).Concat(_replacedIniEdits.Select(x => x.Item)).Concat(_uninstalledIniEdits.Select(x => x.Item)));
				_affectedValues = new HashSet<string>(_installedGameSpecificValueEdits.Select(x => x.Item).Concat(_replacedGameSpecificValueEdits.Select(x => x.Item)).Concat(_uninstalledGameSpecificValueEdits.Select(x => x.Item)));
				_affectedTargets = new HashSet<ModDeploymentTarget>(_deploymentChanges.Keys.Concat(_removedDeploymentTargets));
				foreach (InstallLogSqliteStore.Entry entry in EnlistedInstallLog._store.FindOwnedEntries(_removedModKeys))
				{
					if (entry.Kind == 0)
						_affectedFiles.Add((string)entry.Value.Attribute("path"));
					else if (entry.Kind == 1)
						_affectedIni.Add(ToIni(entry.Value));
					else if (entry.Kind == 2)
						_affectedValues.Add((string)entry.Value.Attribute("key"));
					else
						_affectedTargets.Add(ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)Enum.Parse(typeof(ModDeploymentRoot), (string)entry.Value.Attribute("root")), (string)entry.Value.Attribute("path")));
				}
			}

			/// <summary>Identifies only registrations whose metadata or membership can change in this transaction.</summary>
			private HashSet<string> AffectedModKeys()
			{
				return new HashSet<string>(_activeModRegistry.Registrations.Select(x => x.Value).Concat(_removedModKeys).Concat(_modInstallRoots.Keys).Concat(_modInstallMethods.Keys), StringComparer.OrdinalIgnoreCase);
			}

			/// <summary>Creates an INI identity from a persisted indexed reference.</summary>
			private static IniEdit ToIni(XElement element)
			{
				return new IniEdit((string)element.Attribute("file"), (string)element.Attribute("section"), (string)element.Attribute("key"));
			}

			/// <summary>Persists the final affected owner stacks and recovery marker without traversing unrelated records.</summary>
			private void PersistChanges(long sequence)
			{
				InstallLog log = EnlistedInstallLog;
				var entries = new List<InstallLogSqliteStore.Entry>();
				foreach (string path in _affectedFiles)
					entries.Add(new InstallLogSqliteStore.Entry(0, new XElement("file", new XAttribute("path", path), log._installedFiles.ContainsItem(path) ? new XElement("installingMods", log._installedFiles[path].Select(x => new XElement("mod", new XAttribute("key", x.InstallerKey)))) : null), !log._installedFiles.ContainsItem(path)));
				foreach (IniEdit ini in _affectedIni)
					entries.Add(new InstallLogSqliteStore.Entry(1, new XElement("ini", new XAttribute("file", ini.File), new XAttribute("section", ini.Section), new XAttribute("key", ini.Key), log._installedIniEdits.ContainsItem(ini) ? new XElement("installingMods", log._installedIniEdits[ini].Select(x => new XElement("mod", new XAttribute("key", x.InstallerKey), x.Value))) : null), !log._installedIniEdits.ContainsItem(ini)));
				foreach (string key in _affectedValues)
					entries.Add(new InstallLogSqliteStore.Entry(2, new XElement("edit", new XAttribute("key", key), log._gameSpecificValueEdits.ContainsItem(key) ? new XElement("installingMods", log._gameSpecificValueEdits[key].Select(x => new XElement("mod", new XAttribute("key", x.InstallerKey), Convert.ToBase64String(x.Value)))) : null), !log._gameSpecificValueEdits.ContainsItem(key)));
				foreach (ModDeploymentTarget target in _affectedTargets)
					entries.Add(new InstallLogSqliteStore.Entry(3, new XElement("file", new XAttribute("root", target.Root), new XAttribute("path", target.RelativePath), new XElement("installingMods", log.GetDeploymentOwnerKeysCore(target).Select(x => new XElement("mod", new XAttribute("key", x))))), !log._deploymentByTarget.ContainsKey(target)));
				log._store.Apply(AffectedModKeys().Where(x => !_removedModKeys.Contains(x)).Select(x => new { Key = x, Mod = log._activeModRegistry.GetMod(x) }).Where(x => x.Mod != null).Select(x => log.SerializeMod(x.Mod, x.Key)), _removedModKeys, entries, sequence);
			}
		}
	}
}
