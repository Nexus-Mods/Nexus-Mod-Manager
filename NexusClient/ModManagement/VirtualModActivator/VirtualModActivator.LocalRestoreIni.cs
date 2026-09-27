using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ChinhDo.Transactions;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModActivator
	{
		/// <summary>Replaces the derived VMA INI replay log from one exact native owner/history restore state.</summary>
		internal void RestoreCapturedIniEditLog(IEnumerable<ModIniRestoreEntry> entries, TxFileManager fileManager)
		{
			if (entries == null) throw new ArgumentNullException(nameof(entries));
			if (fileManager == null) throw new ArgumentNullException(nameof(fileManager));
			if (!Directory.Exists(m_strVirtualActivatorPath)) fileManager.CreateDirectory(m_strVirtualActivatorPath);

			var iniEdits = new XElement("iniEdits");
			foreach (ModIniRestoreEntry entry in entries.OrderBy(x => x.File, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.Section, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
			{
				foreach (ModIniRestoreOwner owner in entry.Owners.Where(x => !x.OriginalValue))
				{
					iniEdits.Add(new XElement("iniEdit",
						new XAttribute("modFile", owner.Mod.Filename),
						new XElement("iniFile", entry.File),
						new XElement("iniSection", entry.Section),
						new XElement("iniKey", entry.Key),
						new XElement("iniValue", owner.Value)));
				}
			}
			var document = new XDocument(new XElement("virtualModActivator", new XAttribute("fileVersion", CURRENT_VERSION), iniEdits));
			fileManager.WriteAllText(m_strVirtualActivatorIniEditsPath, document.ToString());
		}

		/// <summary>Checks whether the current VMA INI replay log exactly represents the supplied restored owner history.</summary>
		internal bool CapturedIniEditLogMatches(IEnumerable<ModIniRestoreEntry> entries)
		{
			if (entries == null) throw new ArgumentNullException(nameof(entries));
			string[] expected = CanonicalIniEditLogEntries(entries).OrderBy(x => x, StringComparer.Ordinal).ToArray();
			if (!File.Exists(m_strVirtualActivatorIniEditsPath)) return expected.Length == 0;
			try
			{
				XDocument document = XDocument.Load(m_strVirtualActivatorIniEditsPath);
				XElement root = document.Element("virtualModActivator");
				if (root == null || !StringComparer.Ordinal.Equals((string)root.Attribute("fileVersion"), CURRENT_VERSION.ToString())) return false;
				XElement iniEdits = root.Element("iniEdits");
				string[] actual = (iniEdits == null ? Enumerable.Empty<XElement>() : iniEdits.Elements("iniEdit"))
					.Select(x => String.Join("\u001f", (string)x.Attribute("modFile") ?? String.Empty,
						(string)x.Element("iniFile") ?? String.Empty, (string)x.Element("iniSection") ?? String.Empty,
						(string)x.Element("iniKey") ?? String.Empty, (string)x.Element("iniValue") ?? String.Empty))
					.OrderBy(x => x, StringComparer.Ordinal).ToArray();
				return actual.SequenceEqual(expected, StringComparer.Ordinal);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is System.Xml.XmlException)
			{
				return false;
			}
		}

		private static IEnumerable<string> CanonicalIniEditLogEntries(IEnumerable<ModIniRestoreEntry> entries)
		{
			return entries.SelectMany(entry => entry.Owners.Where(owner => !owner.OriginalValue)
				.Select(owner => String.Join("\u001f", owner.Mod.Filename ?? String.Empty, entry.File, entry.Section, entry.Key, owner.Value ?? String.Empty)));
		}
	}
}
