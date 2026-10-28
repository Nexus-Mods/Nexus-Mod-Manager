using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Versioned JSON boundary for an isolated LSLib reader; package output is untrusted metadata.</summary>
	public static class LarianPackageInspectionProtocol
	{
		public const int SchemaVersion = 1;
		public const int MaximumResponseCharacters = 4 * 1024 * 1024;

		/// <summary>Serializes only observed sibling files; the helper must not extract into or write to the package directory.</summary>
		public static string CreateRequest(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> availableFiles)
		{
			if (file == null) throw new ArgumentNullException(nameof(file));
			if (availableFiles == null || availableFiles.Count == 0 || availableFiles.Count > 10000 || availableFiles.Any(item => item == null))
				throw new ArgumentException("An observed inventory of 1-10000 package files is required.", nameof(availableFiles));
			string root = Path.GetDirectoryName(file.Path);
			if (availableFiles.Any(item => !String.Equals(Path.GetDirectoryName(item.Path), root, StringComparison.OrdinalIgnoreCase)) ||
				availableFiles.Select(item => item.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != availableFiles.Count ||
				!availableFiles.Any(item => String.Equals(item.Path, file.Path, StringComparison.OrdinalIgnoreCase) && item.Length == file.Length && item.LastWriteTimeUtcTicks == file.LastWriteTimeUtcTicks))
				throw new ArgumentException("The input and its unique siblings must belong to one observed directory.", nameof(availableFiles));
			foreach (LoadOrderPackageFile item in availableFiles) LoadOrderInventoryValues.FileName(item.FileName);
			return JsonConvert.SerializeObject(new
			{
				schemaVersion = SchemaVersion, provider = LoadOrderCapability.LarianProviderId, root, fileName = file.FileName,
				files = availableFiles.Select(item => new { fileName = item.FileName, length = item.Length, lastWriteTimeUtcTicks = item.LastWriteTimeUtcTicks })
			});
		}

		/// <summary>Rejects oversized, duplicate-key, mismatched and malformed responses before exposing immutable models.</summary>
		public static LoadOrderPackageInspection ReadResponse(string json, string expectedFileName)
		{
			LoadOrderInventoryValues.FileName(expectedFileName);
			if (String.IsNullOrWhiteSpace(json) || json.Length > MaximumResponseCharacters) throw new InvalidDataException("The package inspection response is empty or oversized.");
			try
			{
				JObject document;
				using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
				{
					document = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
					if (reader.Read()) throw new InvalidDataException("The package reader returned more than one JSON document.");
				}
				if (document.DescendantsAndSelf().OfType<JObject>().Any(item => item.Properties()
					.GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)))
					throw new InvalidDataException("The package reader returned ambiguous JSON property names.");
				var serializer = JsonSerializer.Create(new JsonSerializerSettings
				{
					MissingMemberHandling = MissingMemberHandling.Error,
					ContractResolver = new CamelCasePropertyNamesContractResolver(),
					Converters = { new StringEnumConverter { AllowIntegerValues = false } }
				});
				Response response = document.ToObject<Response>(serializer);
				if (response.SchemaVersion != SchemaVersion || response.Provider != LoadOrderCapability.LarianProviderId ||
					!String.Equals(response.FileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException("The package reader returned a different protocol, provider or input file.");
				if (response.Modules.Length > 256 || response.Modules.Any(module => module == null) || response.ReadFiles.Length > 1024)
					throw new InvalidDataException("The package reader returned an invalid or oversized inventory.");
				LoadOrderModuleInfo[] modules = response.Modules.Select(ConvertModule).ToArray();
				if (modules.Select(module => module.Metadata.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != modules.Length ||
					response.ReadFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != response.ReadFiles.Length)
					throw new InvalidDataException("The package reader returned duplicate module identities or input files.");
				if (response.PrimaryFileName != null && String.Equals(response.PrimaryFileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException("A continuation package cannot refer to itself as its primary file.");
				return new LoadOrderPackageInspection(response.Status, response.Kind, modules, response.Features, response.ReadFiles,
					response.PrimaryFileName, response.TechnicalDetails);
			}
			catch (Exception error) when (error is JsonException || error is ArgumentException)
			{
				throw new InvalidDataException("Invalid package inspection response.", error);
			}
		}

		/// <summary>Converts one Larian module while keeping version and publication integers lossless.</summary>
		private static LoadOrderModuleInfo ConvertModule(Module module)
		{
			string identity = Identity(module.Identity);
			UnsignedInteger(module.Version);
			if (module.PublishHandle != null) UnsignedInteger(module.PublishHandle);
			LoadOrderInventoryValues.FileName(module.Folder);
			if (module.Dependencies.Length > 4096 || module.Conflicts.Length > 4096 ||
				module.Dependencies.Any(item => item == null) || module.Conflicts.Any(item => item == null))
				throw new InvalidDataException("Invalid or oversized module relationships.");
			return new LoadOrderModuleInfo(new LoadOrderModuleMetadata(identity, module.Name, module.Folder, module.Version,
				module.Author, module.Description, module.Md5, module.PublishHandle, module.Tags), module.Kind, module.Features,
				module.RequiredScriptExtenderVersion, module.Dependencies.Select(ConvertReference), module.Conflicts.Select(ConvertReference));
		}

		/// <summary>Retains declared relationships without deciding whether a version is compatible.</summary>
		private static LoadOrderModuleReference ConvertReference(Reference reference)
		{
			if (reference.Version != null) UnsignedInteger(reference.Version);
			return new LoadOrderModuleReference(Identity(reference.Identity), reference.Name, reference.Version);
		}

		/// <summary>Normalizes UUIDs for duplicate detection across packages.</summary>
		private static string Identity(string value)
		{
			Guid identity;
			if (!Guid.TryParse(value, out identity) || identity == Guid.Empty) throw new InvalidDataException("A module requires a nonempty UUID.");
			return identity.ToString("D");
		}

		/// <summary>Accepts raw unsigned 64-bit values without floating-point conversion.</summary>
		private static void UnsignedInteger(string value)
		{
			ulong number;
			if (value == null || value.Length > 20 || !UInt64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
				throw new InvalidDataException("A Larian version or publishing handle is not an unsigned 64-bit integer string.");
		}

		/// <summary>Strict protocol envelope; unknown or missing fields cannot silently become successful observations.</summary>
		private sealed class Response
		{
			[JsonProperty(Required = Required.Always)] public int SchemaVersion { get; set; }
			[JsonProperty(Required = Required.Always)] public string Provider { get; set; }
			[JsonProperty(Required = Required.Always)] public string FileName { get; set; }
			[JsonProperty(Required = Required.Always)] public LoadOrderInspectionStatus Status { get; set; }
			[JsonProperty(Required = Required.Always)] public LoadOrderPackageKind Kind { get; set; }
			[JsonProperty(Required = Required.Always)] public LoadOrderContentFeatures Features { get; set; }
			[JsonProperty(Required = Required.Always)] public string[] ReadFiles { get; set; }
			[JsonProperty(Required = Required.Always)] public Module[] Modules { get; set; }
			public string PrimaryFileName { get; set; }
			public string TechnicalDetails { get; set; }
		}

		/// <summary>Required module facts and optional descriptive metadata from the package helper.</summary>
		private sealed class Module
		{
			[JsonProperty(Required = Required.Always)] public string Identity { get; set; }
			[JsonProperty(Required = Required.Always)] public string Name { get; set; }
			[JsonProperty(Required = Required.Always)] public string Folder { get; set; }
			[JsonProperty(Required = Required.Always)] public string Version { get; set; }
			[JsonProperty(Required = Required.Always)] public LoadOrderModuleKind Kind { get; set; }
			[JsonProperty(Required = Required.Always)] public LoadOrderContentFeatures Features { get; set; }
			[JsonProperty(Required = Required.Always)] public Reference[] Dependencies { get; set; }
			[JsonProperty(Required = Required.Always)] public Reference[] Conflicts { get; set; }
			public string Author { get; set; }
			public string Description { get; set; }
			public string Md5 { get; set; }
			public string PublishHandle { get; set; }
			public string[] Tags { get; set; }
			public int? RequiredScriptExtenderVersion { get; set; }
		}

		/// <summary>A module relationship carries no executable expressions or implicit download actions.</summary>
		private sealed class Reference
		{
			[JsonProperty(Required = Required.Always)] public string Identity { get; set; }
			public string Name { get; set; }
			public string Version { get; set; }
		}
	}
}
