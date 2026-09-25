using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.7 sealing/completeness gate characterization.</summary>
	[TestFixture]
	public class CollectionCaptureSealerTests
	{
		private const long Checkpoint = 17;

		[Test]
		public void Seal_RestorableCaptureFreezesArchiveAndPublishesFixedContractVersions()
		{
			string root = CreateTemporaryDirectory("nmm-c77-restorable-");
			try
			{
				string archivePath = Path.Combine(root, "Example.zip");
				File.WriteAllText(archivePath, "exact archive bytes", Encoding.UTF8);
				CollectionsStore store = CreateStore(root);
				var sealer = new CollectionCaptureSealer(new CollectionsRetainedArtifactStore(store),
					new CollectionsRetainedArtifactReferenceStore(store));
				CollectionCaptureSealRequest request = CreateRequest(root, archivePath,
					LocalCaptureCapability.LocallyRestorableWithinScope,
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
					{
						LocalCaptureScopeArea.ManagedModState,
						LocalCaptureScopeArea.ModArchives
					}));

				CollectionCaptureSealResult result = sealer.Seal(request);

				Assert.IsTrue(result.IsSealed);
				Assert.IsFalse(result.HasFatalIssues);
				Assert.IsFalse(result.HasRestorabilityBlockers);
				Assert.AreEqual(LocalCaptureCapability.LocallyRestorableWithinScope,
					result.SealedCapture.Capture.Capability);
				Assert.AreEqual(LocalCapture.CurrentSchemaVersion, result.SealedCapture.Capture.SchemaVersion);
				Assert.AreEqual(LocalCapture.CurrentCapabilityVersion, result.SealedCapture.Capture.CapabilityVersion);
				Assert.AreEqual(LocalCaptureScope.CurrentVersion, result.SealedCapture.Capture.Scope.Version);
				CollectionCapturedArchiveArtifact archive = result.SealedCapture.Archives.Single();
				Assert.AreEqual("native-a", archive.NativeSnapshotKey);
				Assert.IsTrue(archive.RetainedArtifact.Role.StartsWith("mod-archive:", StringComparison.Ordinal));
				Assert.IsTrue(new CollectionsRetainedArtifactStore(store).VerifyArtifact(archive.RetainedArtifact.StableArtifactId));
				Assert.IsTrue(result.SealedCapture.Capture.RetainedArtifacts.Contains(archive.RetainedArtifact));
				Assert.AreEqual(1, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, request.Identity.ToString())
					.Count(x => x.Role.StartsWith("mod-archive:", StringComparison.Ordinal)));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Seal_RestorableRequestNeverSilentlyDowngradesIncompleteOwnerPayloads()
		{
			string root = CreateTemporaryDirectory("nmm-c77-owner-partial-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var sealer = new CollectionCaptureSealer(new CollectionsRetainedArtifactStore(store),
					new CollectionsRetainedArtifactReferenceStore(store));
				LocalCaptureScope scope = new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
				{
					LocalCaptureScopeArea.ManagedModState,
					LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads
				});
				CollectionCaptureSealRequest restorable = CreateRequest(root, String.Empty,
					LocalCaptureCapability.LocallyRestorableWithinScope, scope,
					ownerPayloads: CreatePartialOwnerPayloads(CreateTarget(), CreateCaptureIdentity()),
					exclusions: new[]
					{
						new LocalCaptureExclusion(LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads,
							"missing-owner-payload", "One owner payload is deliberately excluded for recipe-only capture.")
					});

				CollectionCaptureSealResult restorableResult = sealer.Seal(restorable);

				Assert.IsFalse(restorableResult.IsSealed, "Restorable requests must fail rather than downgrade.");
				Assert.IsTrue(restorableResult.Issues.Any(x => x.Kind == CollectionCaptureSealIssueKind.OwnerPayloadIncomplete));

				CollectionCaptureSealRequest recipeOnly = CloneWithCapability(restorable, LocalCaptureCapability.RecipeOnly);
				CollectionCaptureSealResult recipeResult = sealer.Seal(recipeOnly);
				Assert.IsTrue(recipeResult.IsSealed);
				Assert.IsTrue(recipeResult.SealedCapture.Capture.IsRecipeOnly);
				Assert.IsTrue(recipeResult.HasRestorabilityBlockers);
				Assert.AreEqual(1, recipeResult.SealedCapture.Capture.Exclusions.Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Seal_StateFingerprintDriftBlocksEvenRecipeOnlyPublication()
		{
			string root = CreateTemporaryDirectory("nmm-c77-state-drift-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var sealer = new CollectionCaptureSealer(new CollectionsRetainedArtifactStore(store),
					new CollectionsRetainedArtifactReferenceStore(store));
				CollectionCaptureSealRequest request = CreateRequest(root, String.Empty, LocalCaptureCapability.RecipeOnly,
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[] { LocalCaptureScopeArea.ManagedModState }),
					sealingFingerprint: new CollectionCurrentStateFingerprint("state-v1", "changed"));

				CollectionCaptureSealResult result = sealer.Seal(request);

				Assert.IsFalse(result.IsSealed);
				Assert.IsTrue(result.HasFatalIssues);
				Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionCaptureSealIssueKind.CaptureStateChanged));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Seal_RestorableScriptedMemberRequiresCompleteValidatedReplaySet()
		{
			string root = CreateTemporaryDirectory("nmm-c77-replay-partial-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var sealer = new CollectionCaptureSealer(new CollectionsRetainedArtifactStore(store),
					new CollectionsRetainedArtifactReferenceStore(store));
				CollectionTargetIdentity target = CreateTarget();
				LocalCaptureIdentity captureIdentity = CreateCaptureIdentity();
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, String.Empty, true);
				var replay = new CollectionScriptedReplaySnapshot(target, captureIdentity, Checkpoint,
					new CollectionScriptedReplayArtifactSet[0], NativeStateCaptureCoverage.Partial,
					new[]
					{
						new CollectionScriptedReplayIssue(CollectionScriptedReplayIssueKind.ReplayFileMissing,
							"native-a", "Replay XML is missing.")
					});
				CollectionCaptureSealRequest request = CreateRequest(root, String.Empty,
					LocalCaptureCapability.LocallyRestorableWithinScope,
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
					{
						LocalCaptureScopeArea.ManagedModState,
						LocalCaptureScopeArea.InstallerReplayAndGeneratedPayloads
					}), identities: identities, scriptedReplay: replay);

				CollectionCaptureSealResult result = sealer.Seal(request);

				Assert.IsFalse(result.IsSealed);
				Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionCaptureSealIssueKind.ScriptedReplayIncomplete));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Seal_TamperedSuppliedArchiveReferenceBlocksAnyPublication()
		{
			string root = CreateTemporaryDirectory("nmm-c77-tamper-");
			try
			{
				string archivePath = Path.Combine(root, "Example.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("abcdef"));
				CollectionsStore store = CreateStore(root);
				var artifacts = new CollectionsRetainedArtifactStore(store);
				CollectionsRetainedArtifact artifact;
				using (var source = new MemoryStream(Encoding.ASCII.GetBytes("abcdef"), false))
					artifact = artifacts.Publish(source);
				string blobPath = Path.Combine(store.RetainedContentDirectory, "sha256",
					artifact.ContentHash.Value.Substring(0, 2), artifact.ContentHash.Value + ".blob");
				File.WriteAllBytes(blobPath, Encoding.ASCII.GetBytes("ABCDEF"));
				var supplied = new CollectionCapturedArchiveArtifact("native-a", "Example.zip", null,
					new RetainedArtifactReference(artifact.ArtifactId, "seed", artifact.ContentHash, artifact.ByteLength));
				var sealer = new CollectionCaptureSealer(artifacts, new CollectionsRetainedArtifactReferenceStore(store));
				CollectionCaptureSealRequest request = CreateRequest(root, archivePath, LocalCaptureCapability.RecipeOnly,
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
					{
						LocalCaptureScopeArea.ManagedModState,
						LocalCaptureScopeArea.ModArchives
					}), retainedArchives: new[] { supplied });

				CollectionCaptureSealResult result = sealer.Seal(request);

				Assert.IsFalse(result.IsSealed);
				Assert.IsTrue(result.HasFatalIssues);
				Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionCaptureSealIssueKind.RetainedArtifactCorrupt));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Seal_SuppliedArchiveWithConflictingSourceIdentityIsFatal()
		{
			string root = CreateTemporaryDirectory("nmm-c77-archive-identity-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var artifacts = new CollectionsRetainedArtifactStore(store);
				CollectionsRetainedArtifact artifact;
				using (var source = new MemoryStream(Encoding.ASCII.GetBytes("retained archive"), false))
					artifact = artifacts.Publish(source);
				var retained = new CollectionCapturedArchiveArtifact("native-a", "Example.zip",
					new CollectionInstalledSourceIdentity("nexus-mod-file", "wrong-source"),
					new RetainedArtifactReference(artifact.ArtifactId, "seed", artifact.ContentHash, artifact.ByteLength));
				CollectionTargetIdentity target = CreateTarget();
				var archive = new CollectionInstalledArchiveReference(String.Empty, "Example.zip", false,
					new CollectionInstalledSourceIdentity("nexus-mod-file", "expected-source"));
				var mod = new CollectionInstalledModIdentity("native-a", archive, "100", "200", "1.0", "1.0", false,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data),
					new CollectionInstalledMemberProvenance[0]);
				var identities = new CollectionInstalledIdentitySnapshot(target, Checkpoint, new[] { mod },
					NativeStateCaptureCoverage.NotApplicable, new CollectionInstalledIdentityIssue[0]);
				var sealer = new CollectionCaptureSealer(artifacts, new CollectionsRetainedArtifactReferenceStore(store));
				CollectionCaptureSealRequest request = CreateRequest(root, String.Empty, LocalCaptureCapability.RecipeOnly,
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
					{
						LocalCaptureScopeArea.ManagedModState,
						LocalCaptureScopeArea.ModArchives
					}), identities: identities, retainedArchives: new[] { retained });

				CollectionCaptureSealResult result = sealer.Seal(request);

				Assert.IsFalse(result.IsSealed);
				Assert.IsTrue(result.HasFatalIssues);
				Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionCaptureSealIssueKind.ArchiveIdentityMismatch));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Seal_OutOfScopeExclusionIsFatal()
		{
			string root = CreateTemporaryDirectory("nmm-c77-exclusion-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var sealer = new CollectionCaptureSealer(new CollectionsRetainedArtifactStore(store),
					new CollectionsRetainedArtifactReferenceStore(store));
				CollectionCaptureSealRequest request = CreateRequest(root, String.Empty, LocalCaptureCapability.RecipeOnly,
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[] { LocalCaptureScopeArea.ManagedModState }),
					exclusions: new[]
					{
						new LocalCaptureExclusion(LocalCaptureScopeArea.PluginState, "unsupported-plugin-effect",
							"This exclusion does not belong to the declared scope.")
					});

				CollectionCaptureSealResult result = sealer.Seal(request);

				Assert.IsFalse(result.IsSealed);
				Assert.IsTrue(result.HasFatalIssues);
				Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionCaptureSealIssueKind.InvalidExclusion));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Seal_MissingNativeMemberMappingIsFatal()
		{
			string root = CreateTemporaryDirectory("nmm-c77-mapping-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var sealer = new CollectionCaptureSealer(new CollectionsRetainedArtifactStore(store),
					new CollectionsRetainedArtifactReferenceStore(store));
				CollectionCaptureSealRequest request = CreateRequest(root, String.Empty, LocalCaptureCapability.RecipeOnly,
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[] { LocalCaptureScopeArea.ManagedModState }),
					mappings: new LocalCaptureNativeRecordMapping[0]);

				CollectionCaptureSealResult result = sealer.Seal(request);

				Assert.IsFalse(result.IsSealed);
				Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionCaptureSealIssueKind.NativeRecordMappingIncomplete));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionCaptureSealRequest CreateRequest(string root, string archivePath,
			LocalCaptureCapability capability, LocalCaptureScope scope,
			CollectionOwnerPayloadSnapshot ownerPayloads = null, CollectionInstalledIdentitySnapshot identities = null,
			CollectionScriptedReplaySnapshot scriptedReplay = null, CollectionCurrentStateFingerprint sealingFingerprint = null,
			IEnumerable<CollectionCapturedArchiveArtifact> retainedArchives = null,
			IEnumerable<LocalCaptureExclusion> exclusions = null, IEnumerable<LocalCaptureNativeRecordMapping> mappings = null)
		{
			CollectionTargetIdentity target = CreateTarget();
			LocalCaptureIdentity captureIdentity = CreateCaptureIdentity();
			CollectionInstalledIdentitySnapshot installed = identities ?? CreateIdentities(target, archivePath, false);
			CollectionCurrentStateFingerprint fingerprint = new CollectionCurrentStateFingerprint("state-v1", "same-state");
			return new CollectionCaptureSealRequest(captureIdentity, CreateLocalRevision(), target, fingerprint,
				sealingFingerprint ?? fingerprint, scope, capability, installed,
				ownerPayloads ?? new CollectionOwnerPayloadSnapshot(target, captureIdentity, Checkpoint,
					new CollectionOwnerPayloadTarget[0], NativeStateCaptureCoverage.NotApplicable,
					new CollectionOwnerPayloadIssue[0]),
				scriptedReplay ?? new CollectionScriptedReplaySnapshot(target, captureIdentity, Checkpoint,
					new CollectionScriptedReplayArtifactSet[0], installed.Mods.Any(x => x.HasInstallScript)
						? NativeStateCaptureCoverage.Partial : NativeStateCaptureCoverage.NotApplicable,
					new CollectionScriptedReplayIssue[0]),
				new CollectionNativeEffectSnapshot(target, Checkpoint,
					new CollectionCapturedIniEffect[0], NativeStateCaptureCoverage.NotApplicable,
					new CollectionCapturedGameValueEffect[0], NativeStateCaptureCoverage.NotApplicable,
					new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable,
					new CollectionNativeEffectIssue[0]),
				new CollectionUserMetadataSnapshot(target, captureIdentity, Checkpoint,
					new CollectionCapturedSortAssignment[0], NativeStateCaptureCoverage.NotApplicable,
					new CollectionCapturedScreenshotOverride[0], NativeStateCaptureCoverage.NotApplicable,
					new CollectionUserMetadataIssue[0]),
				retainedArchives ?? new CollectionCapturedArchiveArtifact[0],
				exclusions ?? new LocalCaptureExclusion[0],
				mappings ?? new[]
				{
					new LocalCaptureNativeRecordMapping(
						CollectionMemberKey.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111")),
						new NativeModInstanceIdentity(target, "native-a"))
				});
		}

		private static CollectionCaptureSealRequest CloneWithCapability(CollectionCaptureSealRequest source,
			LocalCaptureCapability capability)
		{
			return new CollectionCaptureSealRequest(source.Identity, source.Revision, source.SourceTarget,
				source.CapturedStateFingerprint, source.SealingStateFingerprint, source.Scope, capability,
				source.InstalledIdentities, source.OwnerPayloads, source.ScriptedReplay, source.NativeEffects,
				source.UserMetadata, source.RetainedArchives, source.Exclusions, source.NativeRecordMappings);
		}

		private static CollectionOwnerPayloadSnapshot CreatePartialOwnerPayloads(CollectionTargetIdentity target,
			LocalCaptureIdentity captureIdentity)
		{
			var deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\example.dds");
			var owner = new CollectionOwnerPayloadOwner(0, "native-a", null,
				NativeStateCaptureDeploymentOwnerKind.Virtual, true, null);
			return new CollectionOwnerPayloadSnapshot(target, captureIdentity, Checkpoint,
				new[] { new CollectionOwnerPayloadTarget(deploymentTarget, false, new[] { owner }) },
				NativeStateCaptureCoverage.Partial,
				new[]
				{
					new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.PayloadSourceUnavailable,
						deploymentTarget.ToString(), "Payload source unavailable.")
				});
		}

		private static CollectionInstalledIdentitySnapshot CreateIdentities(CollectionTargetIdentity target,
			string archivePath, bool hasInstallScript)
		{
			string fileName = String.IsNullOrWhiteSpace(archivePath) ? "Example.zip" : Path.GetFileName(archivePath);
			var archive = new CollectionInstalledArchiveReference(archivePath, fileName,
				!String.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath), null);
			var mod = new CollectionInstalledModIdentity("native-a", archive, "100", "200", "1.0", "1.0",
				hasInstallScript, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data),
				new CollectionInstalledMemberProvenance[0]);
			return new CollectionInstalledIdentitySnapshot(target, Checkpoint, new[] { mod },
				NativeStateCaptureCoverage.NotApplicable, new CollectionInstalledIdentityIssue[0]);
		}

		private static CollectionTargetIdentity CreateTarget()
		{
			return CollectionTargetIdentity.FromFingerprint("target-c77");
		}

		private static LocalCaptureIdentity CreateCaptureIdentity()
		{
			return LocalCaptureIdentity.From(Guid.Parse("77777777-7777-7777-7777-777777777777"));
		}

		private static CollectionRevisionIdentity CreateLocalRevision()
		{
			return CollectionRevisionIdentity.FromLocal(
				CollectionIdentity.FromLocal(Guid.Parse("12345678-1234-1234-1234-1234567890ab")),
				Guid.Parse("abcdefab-cdef-cdef-cdef-abcdefabcdef"));
		}

		private static CollectionsStore CreateStore(string root)
		{
			string featureRoot = Path.Combine(root, "Collections");
			var store = new CollectionsStore(featureRoot);
			store.CreateNew();
			return store;
		}

		private static string CreateTemporaryDirectory(string prefix)
		{
			string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
