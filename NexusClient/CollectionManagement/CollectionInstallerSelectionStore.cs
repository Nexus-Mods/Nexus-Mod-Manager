using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;

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
