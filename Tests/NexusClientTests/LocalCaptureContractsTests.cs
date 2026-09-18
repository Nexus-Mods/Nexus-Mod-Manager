using System;
using System.Collections.Generic;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class LocalCaptureContractsTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void RecipeOnlyCapture_DoesNotClaimLocalRestorability()
		{
			LocalCapture capture = CreateCapture(LocalCaptureCapability.RecipeOnly);

			Assert.That(capture.IsRecipeOnly, Is.True);
			Assert.That(capture.IsLocallyRestorableWithinScope, Is.False);
			Assert.That(capture.Scope.Contains(LocalCaptureScopeArea.ManagedModState), Is.True);
			Assert.That(capture.Revision.Collection.Origin, Is.EqualTo(CollectionOrigin.Local));
		}

		[Test]
		public void RestorableCapture_ExposesExplicitWithinScopePromise()
		{
			LocalCapture capture = CreateCapture(LocalCaptureCapability.LocallyRestorableWithinScope);

			Assert.That(capture.IsRecipeOnly, Is.False);
			Assert.That(capture.IsLocallyRestorableWithinScope, Is.True);
			Assert.That(capture.Scope.Areas.Count, Is.EqualTo(3));
			Assert.That(capture.Exclusions.Count, Is.EqualTo(1));
		}

		[Test]
		public void LocalCapture_RejectsRemoteRevision()
		{
			CollectionIdentity remoteCollection = CollectionIdentity.FromNexus("collection-1");
			CollectionRevisionIdentity remoteRevision = CollectionRevisionIdentity.FromNexus(remoteCollection, "revision-1", 1);

			Assert.Throws<ArgumentException>(() => CreateCapture(
				LocalCaptureCapability.RecipeOnly,
				remoteRevision,
				CollectionTargetIdentity.FromFingerprint("target-1")));
		}

		[Test]
		public void LocalCaptureScope_IsVersionedCanonicalAndRejectsMalformedAreas()
		{
			LocalCaptureScope first = new LocalCaptureScope(1, new[]
			{
				LocalCaptureScopeArea.PluginState,
				LocalCaptureScopeArea.ManagedModState,
				LocalCaptureScopeArea.ModArchives
			});
			LocalCaptureScope same = new LocalCaptureScope(1, new[]
			{
				LocalCaptureScopeArea.ModArchives,
				LocalCaptureScopeArea.PluginState,
				LocalCaptureScopeArea.ManagedModState
			});

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.Areas[0], Is.EqualTo(LocalCaptureScopeArea.ManagedModState));
			Assert.That(first.Version, Is.EqualTo(1));
			Assert.Throws<ArgumentOutOfRangeException>(() => new LocalCaptureScope(0, new[] { LocalCaptureScopeArea.ManagedModState }));
			Assert.Throws<ArgumentException>(() => new LocalCaptureScope(1, new LocalCaptureScopeArea[0]));
			Assert.Throws<ArgumentOutOfRangeException>(() => new LocalCaptureScope(1, new[] { LocalCaptureScopeArea.Unknown }));
			Assert.Throws<ArgumentException>(() => new LocalCaptureScope(1, new[]
			{
				LocalCaptureScopeArea.ManagedModState,
				LocalCaptureScopeArea.ManagedModState
			}));
		}

		[Test]
		public void RetainedArtifactReference_RequiresVerifiedIdentityAndNeverOwnsDeployment()
		{
			RetainedArtifactReference reference = new RetainedArtifactReference(
				"blob-1",
				"mod-archive",
				CollectionContentHash.FromSha256(Sha256A),
				1234);

			Assert.That(reference.ProtectsRetainedContentFromCleanup, Is.True);
			Assert.That(reference.AuthorizesNativeDeploymentRetention, Is.False);
			Assert.That(reference.ByteLength, Is.EqualTo(1234));
			Assert.Throws<ArgumentNullException>(() => new RetainedArtifactReference("blob-1", "role", null, 1));
			Assert.Throws<ArgumentOutOfRangeException>(() => new RetainedArtifactReference(
				"blob-1", "role", CollectionContentHash.FromSha256(Sha256A), -1));
			Assert.Throws<ArgumentException>(() => new RetainedArtifactReference(
				"https://cdn.example.invalid/signed", "role", CollectionContentHash.FromSha256(Sha256A), 1));
		}

		[Test]
		public void Capture_RejectsExclusionOutsideDeclaredScope()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-1");
			LocalCaptureScope scope = new LocalCaptureScope(1, new[] { LocalCaptureScopeArea.ManagedModState });
			LocalCaptureExclusion exclusion = new LocalCaptureExclusion(
				LocalCaptureScopeArea.PluginState,
				"plugin-state-not-captured",
				"Plugin state is outside this capture.");

			Assert.Throws<ArgumentException>(() => CreateCapture(
				LocalCaptureCapability.RecipeOnly,
				CreateLocalRevision(),
				target,
				scope,
				new RetainedArtifactReference[0],
				new[] { exclusion },
				new LocalCaptureNativeRecordMapping[0]));
		}

		[Test]
		public void Capture_MappingsAreScopedToSourceTarget()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-1");
			CollectionTargetIdentity otherTarget = CollectionTargetIdentity.FromFingerprint("target-2");
			LocalCaptureNativeRecordMapping foreignMapping = new LocalCaptureNativeRecordMapping(
				CollectionMemberKey.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111")),
				new NativeModInstanceIdentity(otherTarget, "native-1"));

			Assert.Throws<ArgumentException>(() => CreateCapture(
				LocalCaptureCapability.RecipeOnly,
				CreateLocalRevision(),
				target,
				CreateScope(),
				new RetainedArtifactReference[0],
				new LocalCaptureExclusion[0],
				new[] { foreignMapping }));
		}

		[Test]
		public void Capture_RejectsDuplicateArtifactAndNativeMappingIdentities()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-1");
			RetainedArtifactReference artifact = CreateArtifact("blob-1");
			CollectionMemberKey firstMember = CollectionMemberKey.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111"));
			CollectionMemberKey secondMember = CollectionMemberKey.FromLocal(Guid.Parse("22222222-2222-2222-2222-222222222222"));
			NativeModInstanceIdentity native = new NativeModInstanceIdentity(target, "native-1");

			Assert.Throws<ArgumentException>(() => CreateCapture(
				LocalCaptureCapability.RecipeOnly,
				CreateLocalRevision(),
				target,
				CreateScope(),
				new[] { artifact, CreateArtifact("blob-1") },
				new LocalCaptureExclusion[0],
				new LocalCaptureNativeRecordMapping[0]));

			Assert.Throws<ArgumentException>(() => CreateCapture(
				LocalCaptureCapability.RecipeOnly,
				CreateLocalRevision(),
				target,
				CreateScope(),
				new RetainedArtifactReference[0],
				new LocalCaptureExclusion[0],
				new[]
				{
					new LocalCaptureNativeRecordMapping(firstMember, native),
					new LocalCaptureNativeRecordMapping(secondMember, native)
				}));
		}

		[Test]
		public void NativeRecordMapping_PreservesSnapshotIdentityWithoutPromisingNativeKeyReuse()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-1");
			CollectionMemberKey member = CollectionMemberKey.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111"));
			NativeModInstanceIdentity native = new NativeModInstanceIdentity(target, "source-native-key");
			LocalCaptureNativeRecordMapping mapping = new LocalCaptureNativeRecordMapping(member, native);

			Assert.That(mapping.SnapshotMemberKey, Is.EqualTo(member));
			Assert.That(mapping.SourceNativeInstance, Is.EqualTo(native));
		}

		[Test]
		public void Capture_DefensivelyCopiesCollectionsAndModelsRemainImmutable()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-1");
			List<RetainedArtifactReference> artifacts = new List<RetainedArtifactReference> { CreateArtifact("blob-1") };
			List<LocalCaptureExclusion> exclusions = new List<LocalCaptureExclusion>
			{
				new LocalCaptureExclusion(LocalCaptureScopeArea.UserMetadata, "screenshots-not-retained", "Screenshot overrides are excluded.")
			};
			List<LocalCaptureNativeRecordMapping> mappings = new List<LocalCaptureNativeRecordMapping>
			{
				new LocalCaptureNativeRecordMapping(
					CollectionMemberKey.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111")),
					new NativeModInstanceIdentity(target, "native-1"))
			};
			LocalCapture capture = CreateCapture(
				LocalCaptureCapability.LocallyRestorableWithinScope,
				CreateLocalRevision(),
				target,
				CreateScope(),
				artifacts,
				exclusions,
				mappings);

			artifacts.Clear();
			exclusions.Clear();
			mappings.Clear();

			Assert.That(capture.RetainedArtifacts.Count, Is.EqualTo(1));
			Assert.That(capture.Exclusions.Count, Is.EqualTo(1));
			Assert.That(capture.NativeRecordMappings.Count, Is.EqualTo(1));
			Assert.Throws<NotSupportedException>(() => ((IList<RetainedArtifactReference>)capture.RetainedArtifacts).Clear());
			AssertNoPublicSetters(typeof(LocalCaptureIdentity));
			AssertNoPublicSetters(typeof(LocalCaptureScope));
			AssertNoPublicSetters(typeof(LocalCaptureExclusion));
			AssertNoPublicSetters(typeof(RetainedArtifactReference));
			AssertNoPublicSetters(typeof(LocalCaptureNativeRecordMapping));
			AssertNoPublicSetters(typeof(LocalCapture));
		}

		private static LocalCapture CreateCapture(LocalCaptureCapability capability)
		{
			return CreateCapture(capability, CreateLocalRevision(), CollectionTargetIdentity.FromFingerprint("target-1"));
		}

		private static LocalCapture CreateCapture(
			LocalCaptureCapability capability,
			CollectionRevisionIdentity revision,
			CollectionTargetIdentity target)
		{
			return CreateCapture(
				capability,
				revision,
				target,
				CreateScope(),
				new[] { CreateArtifact("blob-1") },
				new[]
				{
					new LocalCaptureExclusion(
						LocalCaptureScopeArea.UserMetadata,
						"screenshots-not-retained",
						"Screenshot overrides are excluded.")
				},
				new[]
				{
					new LocalCaptureNativeRecordMapping(
						CollectionMemberKey.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111")),
						new NativeModInstanceIdentity(target, "native-1"))
				});
		}

		private static LocalCapture CreateCapture(
			LocalCaptureCapability capability,
			CollectionRevisionIdentity revision,
			CollectionTargetIdentity target,
			LocalCaptureScope scope,
			IEnumerable<RetainedArtifactReference> retainedArtifacts,
			IEnumerable<LocalCaptureExclusion> exclusions,
			IEnumerable<LocalCaptureNativeRecordMapping> mappings)
		{
			return new LocalCapture(
				LocalCaptureIdentity.From(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
				revision,
				target,
				new CollectionCurrentStateFingerprint("managed-state-v1", "state-1"),
				scope,
				capability,
				retainedArtifacts,
				exclusions,
				mappings);
		}

		private static LocalCaptureScope CreateScope()
		{
			return new LocalCaptureScope(1, new[]
			{
				LocalCaptureScopeArea.ManagedModState,
				LocalCaptureScopeArea.ModArchives,
				LocalCaptureScopeArea.UserMetadata
			});
		}

		private static CollectionRevisionIdentity CreateLocalRevision()
		{
			CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.Parse("12345678-1234-1234-1234-1234567890ab"));
			return CollectionRevisionIdentity.FromLocal(collection, Guid.Parse("abcdefab-cdef-cdef-cdef-abcdefabcdef"));
		}

		private static RetainedArtifactReference CreateArtifact(string id)
		{
			return new RetainedArtifactReference(id, "mod-archive", CollectionContentHash.FromSha256(Sha256A), 100);
		}

		private static void AssertNoPublicSetters(Type type)
		{
			foreach (var property in type.GetProperties())
				Assert.That(property.GetSetMethod(), Is.Null, type.Name + "." + property.Name + " must remain immutable.");
		}
	}
}
