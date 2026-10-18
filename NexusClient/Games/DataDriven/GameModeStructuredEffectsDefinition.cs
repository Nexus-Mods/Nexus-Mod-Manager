using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>Typed optional native-owned profiled XML record capability.</summary>
	public sealed class GameModeStructuredEffectsDefinition
	{
		public int SchemaVersion { get; set; }
		public string Provider { get; set; }
		public int Revision { get; set; }
		public string MetadataFile { get; set; }
		public string MetadataArray { get; set; }
		public string MetadataEncoding { get; set; }
		public string ProfileRoot { get; set; }
		public string ManifestFile { get; set; }
		public string[] ExcludedProfiles { get; set; }
		public string KeyPrefix { get; set; }
		public string ContainerId { get; set; }
		public string RecordId { get; set; }
		public string IdentityField { get; set; }
		public List<GameModeStructuredRecordFieldDefinition> Fields { get; set; }
	}

	/// <summary>Literal XML field binding; aliases are ordered fallbacks, not executable expressions.</summary>
	public sealed class GameModeStructuredRecordFieldDefinition
	{
		public string Id { get; set; }
		public string Type { get; set; }
		public string[] Sources { get; set; }
		public string Value { get; set; }
		public string DefaultValue { get; set; }
	}

	/// <summary>Resolves one immutable provider shared by definition validation, planning and native execution.</summary>
	public static class DataDrivenStructuredEffects
	{
		/// <summary>Rejects unsupported combinations instead of falling back to untracked file mutation.</summary>
		public static StructuredXmlRecordRule Create(GameModeDefinition definition)
		{
			GameModeStructuredEffectsDefinition source = definition?.StructuredEffects;
			if (source == null) return null;
			if (definition.BehaviorProfile != "generic" && definition.BehaviorProfile != "managed-content" && definition.BehaviorProfile != "baldursgate3")
				throw new ArgumentException("structuredEffects requires the generic, managed-content or baldursgate3 host.");
			if (definition.ModInstall?.PathAdjustmentProfile != "none")
				throw new ArgumentException("structuredEffects cannot replace an existing special path-adjustment provider.");
			if (source.Fields == null || source.Fields.Count == 0 || source.Fields.Count > 32) throw new ArgumentException("structuredEffects requires 1-32 field bindings.");
			var fields = new List<StructuredXmlRecordField>();
			foreach (GameModeStructuredRecordFieldDefinition field in source.Fields ?? Enumerable.Empty<GameModeStructuredRecordFieldDefinition>())
			{
				if (field == null) throw new ArgumentException("structuredEffects.fields cannot contain null.");
				fields.Add(new StructuredXmlRecordField(field.Id, field.Type, field.Sources, field.Value, field.DefaultValue));
			}
			var rule = new StructuredXmlRecordRule(source.SchemaVersion, source.Provider, source.Revision, source.MetadataFile, source.MetadataArray,
				source.MetadataEncoding, source.ProfileRoot, source.ManifestFile, source.ExcludedProfiles, source.KeyPrefix,
				source.ContainerId, source.RecordId, source.IdentityField, fields);
			if (definition.BehaviorProfile == "baldursgate3") RequireBaldursGate3Compatibility(rule);
			return rule;
		}

		/// <summary>Retains existing BG3 native keys, profile paths and payload meaning while selecting the shared provider.</summary>
		private static void RequireBaldursGate3Compatibility(StructuredXmlRecordRule rule)
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
