using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies frozen folder correction, residual-file detection and exact repair assessment.</summary>
	[TestFixture]
	public class CollectionInstallRootCorrectionTests
	{
		/// <summary>Retains the complete cleanup and winner scope in durable correction data.</summary>
		[TestCase(ModInstallMethod.Virtual)]
		[TestCase(ModInstallMethod.Direct)]
		public void FrozenCorrection_RoundTripRetainsOldCleanupAndExistingGameFolderWinner(ModInstallMethod method)
		{
			using (var fixture = new Fixture(method))
			{
				File.WriteAllText(fixture.GameFile, "settings-winner");
				var destination = new CollectionInstallRootDestination(CollectionInstallRootCorrection.CaptureFile(fixture.NewTarget, fixture.GameFile), "settings", true);
				var correction = new CollectionInstallRootCorrection("preloader", method, fixture.Correction.Files, new[] { destination });
				CollectionInstallRootCorrection restored = CollectionInstallRootCorrection.Deserialize(CollectionInstallRootCorrection.Serialize(correction));
				Assert.AreEqual(method, restored.InstallMethod);
				Assert.AreEqual("preloader", restored.NativeModKey);
				Assert.AreEqual(ModDeploymentRoot.Data, restored.Files.Single().Before.Target.Root);
				Assert.AreEqual(ModDeploymentRoot.GameRoot, restored.Destinations.Single().Before.Target.Root);
				Assert.IsTrue(restored.Destinations.Single().PreserveWinner);
				Assert.AreEqual("settings", restored.Destinations.Single().CurrentOwnerKey);
				CollectionAssert.AreEqual(CollectionInstallRootCorrection.Serialize(correction), CollectionInstallRootCorrection.Serialize(restored));
			}
		}

		/// <summary>Recognizes an old-folder installation and verifies the exact completed move.</summary>
		[TestCase(ModInstallMethod.Virtual)]
		[TestCase(ModInstallMethod.Direct)]
		public void VerifyRepair_DetectsFolderMoveThenAcceptsExactCorrectedState(ModInstallMethod method)
		{
			using (var fixture = new Fixture(method))
			{
				var verifier = new CollectionVerifyRepairExactEffectVerifier();
				IReadOnlyList<CollectionVerifyRepairFinding> before = verifier.Verify(fixture.Association, fixture.State(false), new[] { fixture.Binding }, new[] { fixture.Preview });
				Assert.AreEqual(1, before.Count);
				Assert.IsTrue(before.Single().IsRepairable);
				Assert.AreEqual("install-folder", before.Single().Requirement.SubjectKey);
				Assert.IsTrue(fixture.Correction.Verify(fixture.State(false), false));
				File.Delete(fixture.DataFile);
				File.WriteAllText(fixture.GameFile, "preloader");
				Assert.IsTrue(fixture.Correction.Verify(fixture.State(true), true));
				Assert.IsEmpty(verifier.Verify(fixture.Association, fixture.State(true), new[] { fixture.Binding }, new[] { fixture.Preview }));
			}
		}

		/// <summary>Rejects a completed move while obsolete Data files remain.</summary>
		[Test]
		public void OldDataFileResidue_CannotBeReportedAsCommittedEvenWhenNewFolderIsCorrect()
		{
			using (var fixture = new Fixture(ModInstallMethod.Virtual))
			{
				File.WriteAllText(fixture.GameFile, "preloader");
				Assert.IsFalse(fixture.Correction.Verify(fixture.State(true), true));
				Assert.IsTrue(new CollectionVerifyRepairExactEffectVerifier().Verify(fixture.Association, fixture.State(true),
					new[] { fixture.Binding }, new[] { fixture.Preview }).Any(x => x.IsRepairable));
			}
		}

		/// <summary>Verifies restored original content after the managed owner is removed.</summary>
		[Test]
		public void RestoredOriginalFile_RequiresItsExactReviewedBytesWithoutAStaleManagedOwner()
		{
			using (var fixture = new Fixture(ModInstallMethod.Direct))
			{
				string original = Path.Combine(fixture.Root, "original.bin");
				File.WriteAllText(original, "original");
				var cleanup = new CollectionInstallRootFileRemoval(fixture.Correction.Files.Single().Before,
					CollectionInstallRootCorrection.CaptureFile(fixture.OldTarget, original), new[] { "preloader" }, new string[0]);
				var correction = new CollectionInstallRootCorrection("preloader", ModInstallMethod.Direct, new[] { cleanup });
				File.WriteAllText(fixture.DataFile, "original");
				Assert.IsTrue(correction.Verify(fixture.State(true), true));
				File.WriteAllText(fixture.DataFile, "unexpected-leftover");
				Assert.IsFalse(correction.Verify(fixture.State(true), true));
			}
		}

		/// <summary>Rejects a changed game-folder destination after its preimage was reviewed.</summary>
		[Test]
		public void DestinationChangedAfterReview_IsRejectedBeforeNativeMutation()
		{
			using (var fixture = new Fixture(ModInstallMethod.Virtual))
			{
				var correction = new CollectionInstallRootCorrection("preloader", ModInstallMethod.Virtual, fixture.Correction.Files,
					new[] { new CollectionInstallRootDestination(CollectionInstallRootCorrection.CaptureFile(fixture.NewTarget, fixture.GameFile), null, false) });
				Assert.IsTrue(correction.VerifyDestinations(fixture.State(false)));
				File.WriteAllText(fixture.GameFile, "unreviewed-file");
				Assert.IsFalse(correction.VerifyDestinations(fixture.State(false)));
			}
		}

		/// <summary>Verifies the inactive owner payload rather than another mod's deployed bytes.</summary>
		[TestCase(ModInstallMethod.Virtual)]
		[TestCase(ModInstallMethod.Direct)]
		public void InactiveMember_VerifiesStoredOwnerPayloadRatherThanTheOtherModsWinningBytes(ModInstallMethod method)
		{
			using (var fixture = new Fixture(method))
			{
				File.Delete(fixture.DataFile);
				File.WriteAllText(fixture.GameFile, "winning-settings");
				string stored = Path.Combine(fixture.Root, "stored-payload.bin");
				File.WriteAllText(stored, "preloader");
				CollectionNativeStateIndex state = fixture.State(true, "settings");
				CollectionMemberEffectPreview preview = fixture.Preview.WithInstallRootCorrection(null);
				var verifier = new CollectionVerifyRepairExactEffectVerifier();
				Assert.IsEmpty(verifier.Verify(fixture.Association, state, new[] { fixture.Binding }, new[] { preview }, null, (target, owner) => stored));
				File.WriteAllText(stored, "corrupt-backup");
				Assert.IsTrue(verifier.Verify(fixture.Association, state, new[] { fixture.Binding }, new[] { preview }, null,
					(target, owner) => stored).Any(x => x.Kind == CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch));
			}
		}

		/// <summary>Detects a damaged inactive payload even when the preserved physical winner is correct.</summary>
		[TestCase(ModInstallMethod.Virtual)]
		[TestCase(ModInstallMethod.Direct)]
		public void CorrectedInactiveMember_RequiresItsOwnStoredBytesAsWellAsThePreservedWinner(ModInstallMethod method)
		{
			using (var fixture = new Fixture(method))
			{
				File.Delete(fixture.DataFile);
				File.WriteAllText(fixture.GameFile, "winning-settings");
				string stored = Path.Combine(fixture.Root, "stored-payload.bin");
				File.WriteAllText(stored, "preloader");
				var correction = new CollectionInstallRootCorrection("preloader", method, fixture.Correction.Files,
					new[] { new CollectionInstallRootDestination(CollectionInstallRootCorrection.CaptureFile(fixture.NewTarget, fixture.GameFile), "settings", true) });
				CollectionMemberEffectPreview preview = fixture.Preview.WithInstallRootCorrection(correction);
				CollectionNativeStateIndex state = fixture.State(true, "settings");
				var verifier = new CollectionVerifyRepairExactEffectVerifier();
				Assert.IsEmpty(verifier.Verify(fixture.Association, state, new[] { fixture.Binding }, new[] { preview }, null, (target, owner) => stored));
				File.WriteAllText(stored, "corrupt-backup");
				Assert.IsTrue(verifier.Verify(fixture.Association, state, new[] { fixture.Binding }, new[] { preview }, null,
					(target, owner) => stored).Any(x => x.Kind == CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch));
			}
		}

		/// <summary>Rejects method conversion and malformed retained correction data.</summary>
		[Test]
		public void FrozenScope_RejectsChangedMethodAndTrailingPersistenceData()
		{
			using (var fixture = new Fixture(ModInstallMethod.Virtual))
			{
				var changed = new CollectionNativeModState(fixture.Binding.NativeMod, "archive.7z", "archive.7z", "33946", "323314", "1", "1", ModInstallRoot.Data, ModInstallMethod.Direct);
				Assert.IsFalse(fixture.Correction.MatchesPrevious(changed));
				byte[] trailing = CollectionInstallRootCorrection.Serialize(fixture.Correction).Concat(new byte[] { 0 }).ToArray();
				Assert.Throws<InvalidDataException>(() => CollectionInstallRootCorrection.Deserialize(trailing));
			}
		}

		/// <summary>Builds isolated game folders and immutable native observations without reading live NMM state.</summary>
		private sealed class Fixture : IDisposable
		{
			private readonly ModInstallMethod _method;
			private readonly CollectionTargetIdentity _target = CollectionTargetIdentity.FromFingerprint("root-correction-test");
			private readonly CollectionMemberKey _member = CollectionMemberKey.FromProvider("preloader");
			private readonly CollectionRecipeIdentity _recipe = CollectionRecipeIdentity.FromFingerprint("preloader-recipe");

			/// <summary>Creates fixture files and the old-folder cleanup review.</summary>
			public Fixture(ModInstallMethod method)
			{
				_method = method;
				Root = Path.Combine(Path.GetTempPath(), "nmm-root-correction-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(Path.Combine(Root, "Data"));
				OldTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "WinHTTP.dll");
				NewTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, "WinHTTP.dll");
				DataFile = Path.Combine(Root, "Data", "WinHTTP.dll"); GameFile = Path.Combine(Root, "WinHTTP.dll");
				File.WriteAllText(DataFile, "preloader");
				Correction = new CollectionInstallRootCorrection("preloader", method, new[] {
					new CollectionInstallRootFileRemoval(CollectionInstallRootCorrection.CaptureFile(OldTarget, DataFile),
						new CollectionNativeFileContentEvidence(OldTarget, false, null, 0), new[] { "preloader" }, new string[0]) });
				CollectionNativeFileContentEvidence content = CollectionInstallRootCorrection.CaptureFile(NewTarget, DataFile);
				Preview = new CollectionMemberEffectPreview(_member, _recipe, method, ModInstallRoot.GameRoot,
					new[] { new CollectionPlannedFileEffect(NewTarget, content.ContentHash, content.ByteLength) },
					new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0],
					new CollectionEffectPreviewIssue[0], Correction);
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("root-test"), "revision", 1);
				Association = new CollectionTargetAssociation(Guid.NewGuid(), revision, _target, CollectionAssociationState.Applied);
				Binding = new CollectionMemberBinding(Association, _member, new NativeModInstanceIdentity(_target, "preloader"), _recipe, CollectionMemberBindingKind.InstalledForCollection);
			}

			public string Root { get; }
			public string DataFile { get; }
			public string GameFile { get; }
			public ModDeploymentTarget OldTarget { get; }
			public ModDeploymentTarget NewTarget { get; }
			public CollectionInstallRootCorrection Correction { get; }
			public CollectionMemberEffectPreview Preview { get; }
			public CollectionTargetAssociation Association { get; }
			public CollectionMemberBinding Binding { get; }

			/// <summary>Returns the before or committed observation, optionally with another physical winner.</summary>
			public CollectionNativeStateIndex State(bool corrected, string winner = "preloader")
			{
				var native = new CollectionNativeModState(Binding.NativeMod, "archive.7z", "archive.7z", "33946", "323314", "1", "1",
					corrected ? ModInstallRoot.GameRoot : ModInstallRoot.Data, _method);
				var owner = new CollectionNativeOwnerState("preloader", null, CollectionNativeOwnerKind.NativeMod, true, 0, null);
				var file = new CollectionNativeFileState(corrected ? NewTarget : OldTarget, corrected ? GameFile : DataFile,
					_method == ModInstallMethod.Direct, true, _method == ModInstallMethod.Virtual, winner, new[] { owner }, new CollectionNativeOwnerState[0], new[] { owner });
				return new CollectionNativeStateIndex(_target,
					new[] { new CollectionNativeRootState(ModDeploymentRoot.Data, Path.Combine(Root, "Data")), new CollectionNativeRootState(ModDeploymentRoot.GameRoot, Root) },
					new[] { native }, new[] { file }, new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
					CollectionNativeStateCoverage.NotApplicable, new[] { Association }, new[] { Binding }, new UserOverride[0],
					CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			}

			/// <summary>Removes only the temporary fixture directory.</summary>
			public void Dispose() { Directory.Delete(Root, true); }
		}
	}
}
