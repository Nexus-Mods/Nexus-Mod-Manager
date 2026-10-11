using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Keeps successful uninstall dates independently of active install records and archive metadata.
	/// </summary>
	internal sealed class ModUninstallHistory
	{
		private static readonly object StoresLock = new object();
		private static readonly Dictionary<string, ModUninstallHistory> Stores = new Dictionary<string, ModUninstallHistory>(StringComparer.OrdinalIgnoreCase);
		private readonly object _sync = new object();
		private readonly Dictionary<string, DateTime> _dates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
		private readonly string _path;
		private bool _canSave = true;

		/// <summary>Notifies mod views after an uninstall date changes.</summary>
		internal static event Action<IMod> Changed;

		/// <summary>Loads one game's supplemental history once, without opening any mod archives.</summary>
		private ModUninstallHistory(string directory)
		{
			_path = Path.Combine(directory, "ModUninstallHistory.xml");
			if (!File.Exists(_path)) return;
			try
			{
				XElement root = XDocument.Load(_path).Root;
				if (root == null || root.Name != "modUninstallHistory" || (string)root.Attribute("version") != "1")
					throw new InvalidDataException("Unsupported mod uninstall history format.");
				foreach (XElement entry in root.Elements("mod"))
				{
					string archive = (string)entry.Attribute("archive");
					DateTime date;
					if (String.IsNullOrWhiteSpace(archive) || archive != Path.GetFileName(archive) ||
						!DateTime.TryParseExact((string)entry.Attribute("uninstalledUtc"), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date) || date.Kind != DateTimeKind.Utc)
					{
						Trace.TraceWarning("Ignoring an invalid mod uninstall history entry in {0}.", _path);
						continue;
					}
					DateTime existing;
					if (!_dates.TryGetValue(archive, out existing) || date > existing)
						_dates[archive] = date;
				}
			}
			catch (Exception ex)
			{
				_canSave = false;
				Trace.TraceWarning("Cannot load mod uninstall history; preserving the existing file: {0}", ex.Message);
			}
		}

		/// <summary>Gets a cached history store scoped to the configured game install-info directory.</summary>
		private static ModUninstallHistory GetStore(string directory)
		{
			if (String.IsNullOrWhiteSpace(directory)) return null;
			string fullPath = Path.GetFullPath(directory);
			lock (StoresLock)
			{
				ModUninstallHistory store;
				if (!Stores.TryGetValue(fullPath, out store))
				{
					store = new ModUninstallHistory(fullPath);
					Stores.Add(fullPath, store);
				}
				return store;
			}
		}

		/// <summary>Returns the last successful uninstall in local time, or no value for an unrecorded mod.</summary>
		internal static DateTime? GetDate(string directory, IMod mod)
		{
			if (mod == null) return null;
			ModUninstallHistory store = GetStore(directory);
			string archive = Path.GetFileName(mod.ModArchivePath);
			if (store == null || String.IsNullOrWhiteSpace(archive)) return null;
			lock (store._sync)
			{
				DateTime date;
				return store._dates.TryGetValue(archive, out date) ? date.ToLocalTime() : (DateTime?)null;
			}
		}

		/// <summary>Records a completed removal without allowing supplemental history failures to fail the uninstall.</summary>
		internal static void Record(string directory, IMod mod)
		{
			bool changed = false;
			try
			{
				ModUninstallHistory store = GetStore(directory);
				string archive = mod == null ? null : Path.GetFileName(mod.ModArchivePath);
				if (store == null || String.IsNullOrWhiteSpace(archive)) return;
				lock (store._sync)
				{
					store._dates[archive] = DateTime.UtcNow;
					changed = true;
					if (store._canSave) store.Save();
				}
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Cannot save mod uninstall history: {0}", ex.Message);
			}
			finally
			{
				if (changed)
				{
					try { Changed?.Invoke(mod); }
					catch (Exception ex) { Trace.TraceWarning("Cannot refresh mod uninstall date: {0}", ex.Message); }
				}
			}
		}

		/// <summary>Atomically replaces the small history document while preserving the previous file on write failure.</summary>
		private void Save()
		{
			XElement root = new XElement("modUninstallHistory", new XAttribute("version", "1"));
			foreach (KeyValuePair<string, DateTime> entry in _dates)
				root.Add(new XElement("mod", new XAttribute("archive", entry.Key), new XAttribute("uninstalledUtc", entry.Value.ToString("O", CultureInfo.InvariantCulture))));
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			string temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					new XDocument(root).Save(stream);
					stream.Flush(true);
				}
				if (File.Exists(_path)) File.Replace(temporaryPath, _path, null);
				else File.Move(temporaryPath, _path);
			}
			finally
			{
				if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
			}
		}
	}
}
