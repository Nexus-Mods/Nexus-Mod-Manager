using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Retains confirmed native wizard choices for the exact target, revision, member, archive and install context.</summary>
	internal sealed class CollectionInstallerSelectionStore
	{
		private const string Format = "nmm-ce.collections.installer-selection/1";
		private readonly CollectionsRetainedArtifactStore _artifacts;
		private readonly CollectionsRetainedArtifactReferenceStore _references;

		/// <summary>Uses existing retained-content ownership so choices survive retries and restart without a schema migration.</summary>
		internal CollectionInstallerSelectionStore(CollectionsRetainedArtifactStore artifacts, CollectionsRetainedArtifactReferenceStore references)
		{
			_artifacts = artifacts;
			_references = references;
		}

		/// <summary>Loads only choices bound to the exact immutable preparation inputs.</summary>
		internal ModInstallationFomodSelectionRecipe Load(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			CollectionsRetainedArtifact archive, ModInstallContext context, out string artifactId, CancellationToken cancellationToken)
		{
			string role = GetRole(plan, member, archive, context);
			CollectionsRetainedArtifactReferenceRecord reference = _references.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Revision, CollectionsRevisionSourceStore.GetRevisionOwnerId(plan.Revision), role);
			artifactId = reference == null ? null : reference.ArtifactId;
			if (reference == null) return null;
			if (!_artifacts.VerifyArtifact(artifactId, cancellationToken))
				throw new InvalidDataException("The saved Collection installer choices failed integrity verification.");
			using (Stream stream = _artifacts.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8))
			{
				SelectionDocument document;
				try { document = JsonConvert.DeserializeObject<SelectionDocument>(reader.ReadToEnd()); }
				catch (JsonException ex) { throw new InvalidDataException("The saved Collection installer choices are invalid.", ex); }
				Version version;
				if (document == null || document.Format != Format || document.Binding != role ||
					!Version.TryParse(document.ScriptVersion, out version) || document.Steps == null)
					throw new InvalidDataException("The saved Collection installer choices do not match this preparation.");
				return new ModInstallationFomodSelectionRecipe(version, document.Steps, true);
			}
		}

		/// <summary>Publishes validated choices atomically; cancelled or invalid wizards never acquire a reference.</summary>
		internal string Save(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member, CollectionsRetainedArtifact archive,
			ModInstallContext context, ModInstallationFomodSelectionRecipe recipe, CancellationToken cancellationToken)
		{
			string role = GetRole(plan, member, archive, context);
			var document = new SelectionDocument
			{
				Format = Format, Binding = role, ScriptVersion = recipe.ScriptVersion.ToString(),
				Steps = new List<ModInstallationFomodStepSelection>(recipe.Steps)
			};
			byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document));
			using (var stream = new MemoryStream(bytes, false))
			{
				CollectionsRetainedArtifact artifact = _artifacts.Publish(stream, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				_references.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision,
					CollectionsRevisionSourceStore.GetRevisionOwnerId(plan.Revision), role);
				return artifact.ArtifactId;
			}
		}

		/// <summary>Loads captured native operations only for the exact revision, archive, target and install context.</summary>
		internal IReadOnlyList<ScriptedInstallOperation> LoadOperations(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			CollectionsRetainedArtifact archive, ModInstallContext context, out string artifactId, CancellationToken cancellationToken)
		{
			string role = GetRole(plan, member, archive, context) + "-operations";
			CollectionsRetainedArtifactReferenceRecord reference = _references.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Revision, CollectionsRevisionSourceStore.GetRevisionOwnerId(plan.Revision), role);
			artifactId = reference == null ? null : reference.ArtifactId;
			if (reference == null) return null;
			if (!_artifacts.VerifyArtifact(artifactId, cancellationToken)) throw new InvalidDataException("The saved installer plan failed integrity verification.");
			using (Stream stream = _artifacts.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8))
			{
				CapturedDocument document;
				try { document = JsonConvert.DeserializeObject<CapturedDocument>(reader.ReadToEnd()); }
				catch (JsonException ex) { throw new InvalidDataException("The saved installer plan is invalid.", ex); }
				if (document == null || document.Format != "nmm-ce.collections.installer-operations/1" || document.Binding != role || document.Operations == null)
					throw new InvalidDataException("The saved installer plan does not match this preparation.");
				return document.Operations.Select(operation => operation.ToOperation()).ToList().AsReadOnly();
			}
		}

		/// <summary>Retains confirmed native operations in the existing revision artifact store without changing XML choice data.</summary>
		internal string SaveOperations(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member, CollectionsRetainedArtifact archive,
			ModInstallContext context, IReadOnlyList<ScriptedInstallOperation> operations, CancellationToken cancellationToken)
		{
			string role = GetRole(plan, member, archive, context) + "-operations";
			var document = new CapturedDocument { Format = "nmm-ce.collections.installer-operations/1", Binding = role,
				Operations = operations.Select(CapturedOperation.FromOperation).ToList() };
			using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document)), false))
			{
				CollectionsRetainedArtifact artifact = _artifacts.Publish(stream, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				_references.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision,
					CollectionsRevisionSourceStore.GetRevisionOwnerId(plan.Revision), role);
				return artifact.ArtifactId;
			}
		}

		/// <summary>Versioned native operation data, bound to the same exact revision/archive/context as XML choices.</summary>
		private sealed class CapturedDocument
		{
			public string Format { get; set; }
			public string Binding { get; set; }
			public List<CapturedOperation> Operations { get; set; }
		}

		/// <summary>Stores supported native actions as data; executable script types are never deserialized from retained plans.</summary>
		private sealed class CapturedOperation
		{
			public string Kind { get; set; }
			public string Source { get; set; }
			public string Destination { get; set; }
			public byte[] Data { get; set; }
			public string Section { get; set; }
			public string Key { get; set; }
			public string Value { get; set; }

			/// <summary>Serializes only concrete native effects admitted by Collection preparation.</summary>
			internal static CapturedOperation FromOperation(ScriptedInstallOperation operation)
			{
				InstallModFileOperation file = operation as InstallModFileOperation;
				if (file != null) return new CapturedOperation { Kind = "file", Source = file.SourcePath, Destination = file.DestinationPath };
				GenerateDataFileOperation generated = operation as GenerateDataFileOperation;
				if (generated != null) return new CapturedOperation { Kind = "generated", Source = generated.PreparationSourcePath, Destination = generated.DestinationPath, Data = generated.Data };
				EditIniOperation ini = operation as EditIniOperation;
				if (ini != null) return new CapturedOperation { Kind = "ini", Destination = ini.SettingsFileName, Section = ini.Section, Key = ini.Key, Value = ini.Value };
				EditGameSpecificValueOperation value = operation as EditGameSpecificValueOperation;
				if (value != null) return new CapturedOperation { Kind = "value", Key = value.Key, Data = value.Value };
				throw new NotSupportedException("The captured installer contains an unsupported native operation: " + operation.GetType().Name);
			}

			/// <summary>Recreates typed native effects for revalidation, without rerunning the script.</summary>
			internal ScriptedInstallOperation ToOperation()
			{
				switch (Kind)
				{
					case "file": return new InstallModFileOperation(Source, Destination);
					case "generated": return new GenerateDataFileOperation(Destination, Data, Source);
					case "ini": return new EditIniOperation(Destination, Section, Key, Value);
					case "value": return new EditGameSpecificValueOperation(Key, Data, true);
					default: throw new InvalidDataException("The saved installer contains an unknown operation kind.");
				}
			}
		}

		/// <summary>Uses content identity rather than archive filenames or mod-page IDs to prevent cross-version choice reuse.</summary>
		private static string GetRole(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			CollectionsRetainedArtifact archive, ModInstallContext context)
		{
			string binding = String.Join("\0", Format, plan.Target.Fingerprint, plan.ManifestSource.ContentHash.Value,
				member.MemberKey.ToString(), member.RecipeIdentity.Fingerprint, archive.ArtifactId,
				context.Method.ToString(), context.InstallRoot.ToString());
			using (SHA256 sha = SHA256.Create())
				return "installer-selection:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(binding))).Replace("-", String.Empty).ToLowerInvariant();
		}

		/// <summary>Versioned choice data; destinations are always regenerated from the verified native installer definition.</summary>
		private sealed class SelectionDocument
		{
			public string Format { get; set; }
			public string Binding { get; set; }
			public string ScriptVersion { get; set; }
			public List<ModInstallationFomodStepSelection> Steps { get; set; }
		}
	}
	/// <summary>Atomically retains unapproved optional-member choices independently from installation review and wizard caches.</summary>
	internal sealed class CollectionOptionalSelectionStore
	{
		private const string Format = "nmm-ce.collections.optional-selection/1";
		private readonly string _directory;

		/// <summary>Uses the active Collection storage directory; construction and reads never create preference files.</summary>
		internal CollectionOptionalSelectionStore(string collectionDirectory)
		{
			_directory = Path.Combine(collectionDirectory, "OptionalSelections");
		}

		/// <summary>Restores validated optional decisions for this exact target, revision and manifest content.</summary>
		internal IReadOnlyList<CollectionOptionalMemberSelection> Load(string targetFingerprint, NormalizedCollectionManifest manifest)
		{
			string binding = GetBinding(targetFingerprint, manifest);
			string path = Path.Combine(_directory, binding + ".json");
			if (!File.Exists(path)) return new CollectionOptionalMemberSelection[0];
			if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The saved optional selection is too large.");
			OptionalDocument document;
			try { document = JsonConvert.DeserializeObject<OptionalDocument>(File.ReadAllText(path, Encoding.UTF8)); }
			catch (JsonException error) { throw new InvalidDataException("The saved optional selection is invalid.", error); }
			if (document == null || document.Format != Format || document.Binding != binding || document.Choices == null)
				throw new InvalidDataException("The saved optional selection does not match this Collection.");
			Dictionary<string, CollectionMemberKey> optional = OptionalKeys(manifest);
			if (document.Choices.Count > optional.Count) throw new InvalidDataException("The saved optional selection contains unexpected members.");
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var result = new List<CollectionOptionalMemberSelection>();
			foreach (OptionalChoice choice in document.Choices)
			{
				CollectionMemberKey key;
				if (choice == null || choice.Key == null || !seen.Add(choice.Key) || !optional.TryGetValue(choice.Key, out key) ||
					(choice.Selection != CollectionMemberSelection.Selected && choice.Selection != CollectionMemberSelection.Unselected))
					throw new InvalidDataException("The saved optional selection contains an invalid or duplicate decision.");
				result.Add(new CollectionOptionalMemberSelection(key, choice.Selection));
			}
			return result.AsReadOnly();
		}

		/// <summary>Writes only explicit optional decisions; atomic replacement preserves the previous draft on interrupted saves.</summary>
		internal void Save(string targetFingerprint, NormalizedCollectionManifest manifest, IEnumerable<CollectionOptionalMemberSelection> selections)
		{
			string binding = GetBinding(targetFingerprint, manifest);
			Dictionary<string, CollectionMemberKey> optional = OptionalKeys(manifest);
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var choices = new List<OptionalChoice>();
			foreach (CollectionOptionalMemberSelection selection in selections)
			{
				if (selection == null || !optional.ContainsKey(selection.MemberKey.ToString()) || !seen.Add(selection.MemberKey.ToString()))
					throw new ArgumentException("Only unique optional-member decisions can be saved.", nameof(selections));
				choices.Add(new OptionalChoice { Key = selection.MemberKey.ToString(), Selection = selection.Selection });
			}
			var document = new OptionalDocument { Format = Format, Binding = binding, Choices = choices.OrderBy(choice => choice.Key, StringComparer.Ordinal).ToList() };
			byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document));
			if (data.Length > 4 * 1024 * 1024) throw new InvalidDataException("The optional selection is too large to save.");
			Directory.CreateDirectory(_directory);
			string path = Path.Combine(_directory, binding + ".json");
			string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
			try
			{
				using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					stream.Write(data, 0, data.Length);
					stream.Flush(true);
				}
				if (File.Exists(path)) File.Replace(temporary, path, null);
				else File.Move(temporary, path);
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}

		/// <summary>Includes the target and immutable manifest digest while leaving normalizer upgrades free to preserve user intent.</summary>
		private static string GetBinding(string targetFingerprint, NormalizedCollectionManifest manifest)
		{
			if (manifest == null) throw new ArgumentNullException(nameof(manifest));
			if (String.IsNullOrWhiteSpace(targetFingerprint)) throw new ArgumentException("A Collection target is required.", nameof(targetFingerprint));
			string value = String.Join("\0", Format, targetFingerprint, manifest.Revision.ToString(), manifest.Source.ContentHash.Value);
			using (SHA256 sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", String.Empty).ToLowerInvariant();
		}

		/// <summary>Required and unresolved members cannot be changed by persisted optional preferences.</summary>
		private static Dictionary<string, CollectionMemberKey> OptionalKeys(NormalizedCollectionManifest manifest) => manifest.Members
			.Where(member => member.Requirement == CollectionMemberRequirement.Optional && member.IdentityResolution.IsResolved)
			.ToDictionary(member => member.IdentityResolution.Key.ToString(), member => member.IdentityResolution.Key, StringComparer.Ordinal);

		/// <summary>Versioned draft preferences; these records are never an approved native installation plan.</summary>
		private sealed class OptionalDocument
		{
			public string Format { get; set; }
			public string Binding { get; set; }
			public List<OptionalChoice> Choices { get; set; }
		}

		/// <summary>One explicit checkbox decision bound to a stable optional-member key.</summary>
		private sealed class OptionalChoice
		{
			public string Key { get; set; }
			public CollectionMemberSelection Selection { get; set; }
		}
	}

}
