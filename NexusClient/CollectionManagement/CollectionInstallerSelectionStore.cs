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
}
