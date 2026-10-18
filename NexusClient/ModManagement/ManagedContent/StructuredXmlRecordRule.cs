using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Optional configured native-owned XML effects, independent of file layout and order observation.</summary>
	public interface IStructuredXmlRecordProvider
	{
		/// <summary>Immutable rule, or null when the mode declares no structured effects.</summary>
		StructuredXmlRecordRule StructuredRecords { get; }
	}

	/// <summary>One literal attribute-record field bound to metadata aliases or a constant.</summary>
	public sealed class StructuredXmlRecordField
	{
		/// <summary>Freezes literal field names and ordered metadata aliases; no expression evaluation is supported.</summary>
		public StructuredXmlRecordField(string id, string type, IEnumerable<string> sources, string value, string defaultValue)
		{
			StructuredXmlRecordRule.RequireToken(id, "field id");
			StructuredXmlRecordRule.RequireToken(type, "field type");
			string[] names = (sources ?? Enumerable.Empty<string>()).Take(9).ToArray();
			if (names.Length > 8 || names.Any(name => !StructuredXmlRecordRule.IsToken(name)) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length ||
				(names.Length == 0) == (value == null) || (value != null && (sources != null || defaultValue != null)))
				throw new ArgumentException("Each XML field requires either 1-8 unique source aliases or a constant value.");
			if ((value?.Length ?? 0) > 4096 || (defaultValue?.Length ?? 0) > 4096) throw new ArgumentException("XML field defaults are too long.");
			Id = id; Type = type; Sources = Array.AsReadOnly(names); Value = value; DefaultValue = defaultValue;
		}
		public string Id { get; }
		public string Type { get; }
		public IReadOnlyList<string> Sources { get; }
		public string Value { get; }
		public string DefaultValue { get; }
	}

	/// <summary>Versioned profiled attribute-record semantics, selected as data rather than arbitrary XPath or scripts.</summary>
	public sealed class StructuredXmlRecordRule
	{
		public const string ProviderId = "profiled-attribute-records-v1";
		/// <summary>Snapshots validated location, native key identity and metadata/XML field bindings.</summary>
		public StructuredXmlRecordRule(int schemaVersion, string provider, int revision, string metadataFile, string metadataArray,
			string metadataEncoding, string profileRoot, string manifestFile, IEnumerable<string> excludedProfiles, string keyPrefix,
			string containerId, string recordId, string identityField, IEnumerable<StructuredXmlRecordField> fields)
		{
			if (schemaVersion != 1 || provider != ProviderId || revision < 1) throw new ArgumentException("Unsupported structuredEffects schema, provider or revision.");
			MetadataFile = Segment(metadataFile); ManifestFile = Segment(manifestFile);
			RequireToken(metadataArray, "metadata array"); RequireToken(containerId, "container id");
			RequireToken(recordId, "record id"); RequireToken(identityField, "identity field");
			if (metadataEncoding != "Ascii" && metadataEncoding != "Utf8") throw new ArgumentException("structuredEffects.metadataEncoding must be Ascii or Utf8.");
			string[] parts;
			if (!ManagedContentPath.TryParse(profileRoot, out parts)) throw new ArgumentException("structuredEffects.profileRoot must be a safe relative LocalAppData path.");
			if (String.IsNullOrEmpty(keyPrefix) || !keyPrefix.EndsWith("|", StringComparison.Ordinal) || !IsToken(keyPrefix.Substring(0, keyPrefix.Length - 1)))
				throw new ArgumentException("structuredEffects.keyPrefix must be a stable literal identifier followed by '|'.");
			if (excludedProfiles == null) throw new ArgumentException("structuredEffects.excludedProfiles must be an explicit list.");
			string[] excluded = excludedProfiles.Take(257).Select(Segment).ToArray();
			if (excluded.Length > 256 || excluded.Distinct(StringComparer.OrdinalIgnoreCase).Count() != excluded.Length)
				throw new ArgumentException("Excluded profile names must be unique and bounded.");
			StructuredXmlRecordField[] declared = (fields ?? Enumerable.Empty<StructuredXmlRecordField>()).Take(33).ToArray();
			if (declared.Length == 0 || declared.Length > 32 || declared.Any(field => field == null) || declared.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() != declared.Length)
				throw new ArgumentException("structuredEffects requires 1-32 unique XML fields.");
			StructuredXmlRecordField identity = declared.SingleOrDefault(field => field.Id == identityField);
			if (identity == null || identity.Sources.Count == 0 || identity.DefaultValue != null)
				throw new ArgumentException("The identity field must bind metadata without an invented default GUID.");
			Revision = revision; MetadataArray = metadataArray; MetadataEncoding = metadataEncoding; ProfileRoot = String.Join("\\", parts);
			ExcludedProfiles = Array.AsReadOnly(excluded); KeyPrefix = keyPrefix; ContainerId = containerId;
			RecordId = recordId; IdentityField = identityField; Fields = Array.AsReadOnly(declared);
		}
		public int Revision { get; }
		public string MetadataFile { get; }
		public string MetadataArray { get; }
		public string MetadataEncoding { get; }
		public string ProfileRoot { get; }
		public string ManifestFile { get; }
		public IReadOnlyList<string> ExcludedProfiles { get; }
		public string KeyPrefix { get; }
		public string ContainerId { get; }
		public string RecordId { get; }
		public string IdentityField { get; }
		public IReadOnlyList<StructuredXmlRecordField> Fields { get; }

		/// <summary>Accepts literal bounded identifiers, excluding selectors, delimiters and executable expressions.</summary>
		internal static bool IsToken(string value) => !String.IsNullOrEmpty(value) && value.Length <= 128 && Char.IsLetter(value[0]) &&
			value.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '.');
		/// <summary>Reports invalid literal configuration before any native execution.</summary>
		internal static void RequireToken(string value, string label)
		{
			if (!IsToken(value)) throw new ArgumentException("Invalid structured XML " + label + ".");
		}
		/// <summary>Rejects traversal, device paths and native-key delimiters in a profile/file segment.</summary>
		internal static string Segment(string value)
		{
			string[] parts;
			if (!ManagedContentPath.TryParse(value, out parts) || parts.Length != 1 || value.IndexOf('|') >= 0)
				throw new ArgumentException("A safe literal profile/file segment is required.");
			return parts[0];
		}
	}
}
