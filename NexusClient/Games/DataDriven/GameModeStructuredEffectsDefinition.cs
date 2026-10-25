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
			if (definition.BehaviorProfile == "baldursgate3") BaldursGate3DefinitionPolicy.RequireStructuredCompatibility(rule);
			return rule;
		}


	}
}
