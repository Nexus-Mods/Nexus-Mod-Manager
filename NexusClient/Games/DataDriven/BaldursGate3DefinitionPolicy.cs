using System;
using System.Linq;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>Preserves BG3's existing structured-record persistence contract.</summary>
	internal static class BaldursGate3DefinitionPolicy
	{
		/// <summary>Retains existing BG3 native keys, profile paths and payload meaning while selecting the shared provider.</summary>
		internal static void RequireStructuredCompatibility(StructuredXmlRecordRule rule)
		{
			StructuredXmlRecordField[] expected =
			{
				new StructuredXmlRecordField("Folder", "LSString", new[] { "Folder", "folderName" }, null, null),
				new StructuredXmlRecordField("MD5", "LSString", null, String.Empty, null),
				new StructuredXmlRecordField("Name", "LSString", new[] { "Name", "modName" }, null, null),
				new StructuredXmlRecordField("UUID", "FixedString", new[] { "UUID" }, null, null),
				new StructuredXmlRecordField("Version64", "int64", new[] { "Version" }, null, "1")
			};
			if (rule.MetadataFile != "info.json" || rule.MetadataArray != "Mods" || rule.MetadataEncoding != "Ascii" ||
				rule.ProfileRoot != @"Larian Studios\Baldur's Gate 3\PlayerProfiles" || rule.ManifestFile != "modsettings.lsx" ||
				!rule.ExcludedProfiles.SequenceEqual(new[] { "default" }, StringComparer.OrdinalIgnoreCase) || rule.KeyPrefix != "bg3-modsettings-v1|" ||
				rule.ContainerId != "Mods" || rule.RecordId != "ModuleShortDesc" || rule.IdentityField != "UUID" || rule.Fields.Count != expected.Length ||
				expected.Where((field, index) => !SameField(field, rule.Fields[index])).Any())
				throw new ArgumentException("The BG3 adapter requires its historical profile, metadata, native-key namespace and ModuleShortDesc payload contract.");
		}

		/// <summary>Prevents a JSON edit from silently reinterpreting existing BG3 native ownership payloads.</summary>
		private static bool SameField(StructuredXmlRecordField left, StructuredXmlRecordField right) => left.Id == right.Id && left.Type == right.Type &&
			left.Value == right.Value && left.DefaultValue == right.DefaultValue && left.Sources.SequenceEqual(right.Sources, StringComparer.Ordinal);
	}
}
