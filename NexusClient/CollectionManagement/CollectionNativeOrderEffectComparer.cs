using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Verifies new curator recipes within their declared membership while retaining exact legacy comparisons.</summary>
	internal static class CollectionNativeOrderEffectComparer
	{
		/// <summary>Requires exact values except for explicitly scoped native Collection order payloads.</summary>
		internal static bool Matches(string key, byte[] expected, byte[] actual, IEnumerable<CollectionPlannedGameValueEffect> effects)
		{
			if (ReferenceEquals(expected, actual) || expected != null && actual != null && expected.SequenceEqual(actual)) return true;
			if (expected == null || actual == null || !key.StartsWith(BaldursGate3CollectionRecordStore.Prefix, StringComparison.Ordinal)) return false;
			string[] parts = key.Substring(BaldursGate3CollectionRecordStore.Prefix.Length).Split('|');
			if (parts.Length != 2 || parts[1] != BaldursGate3CollectionRecordStore.Identity) return false;
			CollectionPlannedGameValueEffect policy = effects.SingleOrDefault(effect => effect.Key == LarianOrderPreference.Key(parts[0]));
			if (policy == null) return false;
			try
			{
				LarianOrderPreference preference = LarianOrderPreference.Decode(policy.UnsafeValue);
				if (preference == null || preference.Source != "Curator") return false;
				XmlDocument desired = Read(expected), observed = Read(actual);
				return new[] { "Mods", "ModOrder" }.All(name => Project(desired, name, preference.Managed).SequenceEqual(Project(observed, name, preference.Managed)));
			}
			catch (Exception error) when (error is XmlException || error is IOException || error is ArgumentException || error is Newtonsoft.Json.JsonException)
			{ return false; }
		}

		/// <summary>Projects ordered native records onto declared members; additional active members remain detectable.</summary>
		private static IEnumerable<string> Project(XmlDocument document, string name, string[] identities)
		{
			XmlNode container = document.SelectSingleNode("/bg3-order/node[@id='" + name + "']");
			if (container == null) return new[] { "<absent>" };
			return container.SelectNodes("children/node").OfType<XmlElement>().Where(node => identities.Contains(node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value, StringComparer.OrdinalIgnoreCase))
				.Select(node => node.OuterXml).ToArray();
		}

		/// <summary>Bounds untrusted retained values and disables XML entity resolution.</summary>
		private static XmlDocument Read(byte[] bytes)
		{
			if (bytes.Length > LarianLoadOrderDocument.MaximumBytes) throw new InvalidDataException("Native order value is too large.");
			var document = new XmlDocument { XmlResolver = null };
			using (var stream = new MemoryStream(bytes, false))
			using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = LarianLoadOrderDocument.MaximumBytes })) document.Load(reader);
			if (document.DocumentElement?.Name != "bg3-order") throw new InvalidDataException("Invalid native order value.");
			return document;
		}
	}
}
