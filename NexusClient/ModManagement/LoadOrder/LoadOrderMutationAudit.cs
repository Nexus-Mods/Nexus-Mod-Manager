using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ChinhDo.Transactions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Retains the latest committed lifecycle change per native profile in the game's existing Install Info folder.</summary>
	internal static class LoadOrderMutationAudit
	{
		private const int MaximumBytes = 32 * 1024 * 1024;

		/// <summary>Rejects changes to unrelated inclusion, positions or metadata during an ordinary per-module operation.</summary>
		internal static void RequireScopedChange(LarianLoadOrderDocument before, LarianLoadOrderDocument after, IEnumerable<string> affected)
		{
			var scope = new HashSet<string>(affected, StringComparer.OrdinalIgnoreCase);
			string[] unchangedBefore = before.Entries.Where(entry => !scope.Contains(entry.Identity)).Select(entry => entry.Identity).ToArray();
			string[] unchangedAfter = after.Entries.Where(entry => !scope.Contains(entry.Identity)).Select(entry => entry.Identity).ToArray();
			if (!unchangedBefore.SequenceEqual(unchangedAfter) || unchangedBefore.Any(id => Normalize(before.Records[id]) != Normalize(after.Records[id])))
				throw new InvalidOperationException(LanguageManager.Get("LoadOrder.Install.UnrelatedChange", "The mod operation attempted to change unrelated load-order entries and cannot continue. Use an explicit load-order action to change other modules."));
		}

		/// <summary>Ignores writer indentation while retaining native fields, unknown attributes, comments and element order.</summary>
		private static string Normalize(XmlElement record)
		{
			var xml = new XmlDocument { XmlResolver = null }; xml.LoadXml(record.OuterXml); return xml.OuterXml;
		}

		/// <summary>Writes exact before/after images inside the same ambient file transaction as the installation.</summary>
		internal static void Write(string directory, string profile, string modName, string archive, bool upgrade, bool explicitOrder,
			LarianLoadOrderDocument before, LarianLoadOrderDocument after, TxFileManager files)
		{
			if (String.IsNullOrWhiteSpace(directory) || before.Sha256 == after.Sha256) return;
			string[] oldOrder = before.Entries.Select(entry => entry.Identity).ToArray(), newOrder = after.Entries.Select(entry => entry.Identity).ToArray();
			var oldIds = new HashSet<string>(oldOrder); var newIds = new HashSet<string>(newOrder);
			string path = FilePath(directory, before.FilePath);
			if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("The load-order audit file is redirected.");
			var audit = new
			{
				SchemaVersion = 1, RecordedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), GameProfile = profile,
				ModName = modName, Archive = archive, Upgrade = upgrade, OrderExplicitlySupplied = explicitOrder, ManifestPath = before.FilePath,
				BeforeHash = before.Sha256, AfterHash = after.Sha256,
				Added = newOrder.Except(oldOrder).ToArray(), Removed = oldOrder.Except(newOrder).ToArray(),
				ExistingRelativeOrderChanged = !oldOrder.Where(newIds.Contains).SequenceEqual(newOrder.Where(oldIds.Contains)),
				ChangedMetadata = oldOrder.Intersect(newOrder).Where(id => Normalize(before.Records[id]) != Normalize(after.Records[id])).ToArray(),
				BeforeOrder = oldOrder, AfterOrder = newOrder,
				BeforeLsxBase64 = Convert.ToBase64String(before.Bytes), AfterLsxBase64 = Convert.ToBase64String(after.Bytes)
			};
			byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(audit, Newtonsoft.Json.Formatting.Indented));
			if (bytes.Length > MaximumBytes) throw new InvalidDataException("The load-order audit exceeds its supported size.");
			files.WriteAllBytes(path, bytes);
		}

		/// <summary>Reads detached diagnostic evidence only; audit records never authorize native changes or recovery.</summary>
		internal static JObject Read(string directory, string manifest)
		{
			if (String.IsNullOrWhiteSpace(directory)) return null;
			string path = FilePath(directory, manifest);
			try
			{
				if (!File.Exists(path)) return null;
				byte[] bytes = LarianLoadOrderDocument.ReadBytes(path, MaximumBytes);
				using (var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes))) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
				{
					JObject audit = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
					if (reader.Read() || audit["SchemaVersion"]?.Type != JTokenType.Integer || audit["SchemaVersion"].Value<long>() != 1) throw new InvalidDataException("Unsupported load-order audit record.");
					return audit;
				}
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException || error is ArgumentException || error is FormatException || error is OverflowException || error is System.Security.SecurityException)
			{ return new JObject { ["Path"] = path, ["ReadError"] = error.Message }; }
		}

		/// <summary>Uses a fixed application-owned filename, independent of untrusted profile or archive names.</summary>
		internal static string FilePath(string directory, string manifest) => Path.Combine(directory, "LoadOrder.LastChange." +
			LarianLoadOrderDocument.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(manifest).ToUpperInvariant())) + ".json");
	}
}
