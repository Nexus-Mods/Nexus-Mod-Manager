using System;
using System.IO;
using System.Reflection;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.8 authoritative durability and effect-verification coverage.</summary>
	[TestFixture]
	public class CollectionNativeChildVerificationCoordinatorTests
	{
		[Test]
		public void DetermineVerifiedDurability_CommittedRealityWinsOverReportedFailure()
		{
			ModOperationResult reported = CreateResult(ModOperationReportedStatus.Failed, ModOperationDurability.Unknown);
			Assert.AreEqual(ModOperationDurability.VerifiedCommitted,
				InvokeDurability(reported, true, false));
		}

		[Test]
		public void DetermineVerifiedDurability_VerifiedRollbackEvidenceDistinguishesNotStartedFromRollback()
		{
			Assert.AreEqual(ModOperationDurability.NotStarted,
				InvokeDurability(CreateResult(ModOperationReportedStatus.Cancelled, ModOperationDurability.NotStarted), false, true));
			Assert.AreEqual(ModOperationDurability.VerifiedRolledBack,
				InvokeDurability(CreateResult(ModOperationReportedStatus.Failed, ModOperationDurability.Unknown), false, true));
		}

		[Test]
		public void DetermineVerifiedDurability_UnverifiedRealityRemainsUnknown()
		{
			Assert.AreEqual(ModOperationDurability.Unknown,
				InvokeDurability(CreateResult(ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted), false, false));
		}

		[Test]
		public void DetermineVerifiedDurability_NativeCommittedBoundaryCannotBeReclassifiedAsRollback()
		{
			Assert.AreEqual(ModOperationDurability.Unknown,
				InvokeDurability(CreateResult(ModOperationReportedStatus.Failed, ModOperationDurability.VerifiedCommitted), false, true));
		}

		[Test]
		public void FileContentIdentity_RejectsChangedBytesWithSameLength()
		{
			string temp = Path.GetTempFileName();
			try
			{
				byte[] expectedBytes = { 1, 2, 3, 4 };
				File.WriteAllBytes(temp, expectedBytes);
				Type contentType = typeof(CollectionNativeChildVerificationCoordinator).GetNestedType(
					"FileContentIdentity", BindingFlags.NonPublic);
				Assert.IsNotNull(contentType);
				MethodInfo fromBytes = contentType.GetMethod("FromBytes", BindingFlags.Public | BindingFlags.Static);
				MethodInfo matches = contentType.GetMethod("Matches", BindingFlags.Public | BindingFlags.Instance);
				Assert.IsNotNull(fromBytes);
				Assert.IsNotNull(matches);
				object expected = fromBytes.Invoke(null, new object[] { expectedBytes });
				Assert.IsTrue((bool)matches.Invoke(expected, new object[] { temp }));

				File.WriteAllBytes(temp, new byte[] { 1, 9, 3, 4 });
				Assert.IsFalse((bool)matches.Invoke(expected, new object[] { temp }));
			}
			finally
			{
				File.Delete(temp);
			}
		}

		[Test]
		public void VerifyMemberEffects_RequiresExactNativeOwnerAndConfigurationValues()
		{
			string temp = Path.GetTempFileName();
			try
			{
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c68-test");
				var nativeMod = new CollectionNativeModState(new NativeModInstanceIdentity(target, "owner-a"),
					"archive.7z", "archive.7z", "10", "20", "1.0", "1.0", false,
					ModInstallRoot.Data, ModInstallMethod.Direct);
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\a.dds");
				var owner = new CollectionNativeOwnerState("owner-a", null, CollectionNativeOwnerKind.NativeMod, null, null, null);
				var file = new CollectionNativeFileState(deploymentTarget, temp, false, true, false, "owner-a",
					new[] { owner }, new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0]);
				var iniKey = new CollectionNativeIniKey("settings.ini", "Display", "Mode");
				var ini = new CollectionNativeIniState(iniKey, new[] { new CollectionNativeTextOwnerValue("owner-a", "High") });
				var game = new CollectionNativeGameValueState("setting", new[] { new CollectionNativeBinaryOwnerValue("owner-a", new byte[] { 1, 2 }) });
				var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { nativeMod }, new[] { file },
					new[] { ini }, new[] { game }, new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
					new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
					CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				var preview = new CollectionMemberEffectPreview(CollectionMemberKey.FromProvider("member-a"),
					CollectionRecipeIdentity.FromFingerprint("recipe-a"), ModInstallMethod.Direct, ModInstallRoot.Data,
					new[] { new CollectionPlannedFileEffect(deploymentTarget) },
					new[] { new CollectionPlannedIniEffect(iniKey, "High") },
					new[] { new CollectionPlannedGameValueEffect("setting", new byte[] { 1, 2 }) },
					new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);

				Assert.IsTrue(InvokeVerifyMemberEffects(state, nativeMod, preview));

				var wrongFile = new CollectionNativeFileState(deploymentTarget, temp, false, true, false, "owner-b",
					new[] { owner }, new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0]);
				var wrongState = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { nativeMod }, new[] { wrongFile },
					new[] { ini }, new[] { game }, new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
					new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
					CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				Assert.IsFalse(InvokeVerifyMemberEffects(wrongState, nativeMod, preview));
			}
			finally
			{
				File.Delete(temp);
			}
		}

		private static ModOperationResult CreateResult(ModOperationReportedStatus status, ModOperationDurability durability)
		{
			ModOperationIdentity identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-c68-test", new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "recipe-a"));
			return new ModOperationResult(identity, status, durability, null);
		}

		private static ModOperationDurability InvokeDurability(ModOperationResult reported, bool committed, bool preState)
		{
			MethodInfo method = typeof(CollectionNativeChildVerificationCoordinator).GetMethod(
				"DetermineVerifiedDurability", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			return (ModOperationDurability)method.Invoke(null, new object[] { reported, committed, preState });
		}

		private static bool InvokeVerifyMemberEffects(CollectionNativeStateIndex state, CollectionNativeModState nativeMod,
			CollectionMemberEffectPreview preview)
		{
			MethodInfo method = typeof(CollectionNativeChildVerificationCoordinator).GetMethod(
				"VerifyMemberEffects", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			return (bool)method.Invoke(null, new object[] { state, nativeMod, preview });
		}
	}
}
