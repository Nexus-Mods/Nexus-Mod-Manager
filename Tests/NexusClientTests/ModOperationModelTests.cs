using System;
using System.Threading;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies the immutable operation identity/result model introduced before native task submission is refactored.
	/// </summary>
	[TestFixture]
	public class ModOperationModelTests
	{
		/// <summary>
		/// Verifies that target, install context and recipe all participate in request compatibility.
		/// </summary>
		[Test]
		public void Fingerprint_EqualityIncludesTargetContextAndRecipe()
		{
			var virtualData = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var directData = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
			var first = new ModOperationFingerprint("game:alpha", virtualData, "recipe:1");
			var same = new ModOperationFingerprint("game:alpha", virtualData, "recipe:1");

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
			Assert.That(first, Is.Not.EqualTo(new ModOperationFingerprint("game:beta", virtualData, "recipe:1")));
			Assert.That(first, Is.Not.EqualTo(new ModOperationFingerprint("game:alpha", directData, "recipe:1")));
			Assert.That(first, Is.Not.EqualTo(new ModOperationFingerprint("game:alpha", virtualData, "recipe:2")));
		}

		/// <summary>
		/// Verifies that ordinary manual operations may deliberately carry no recipe identity.
		/// </summary>
		[Test]
		public void Fingerprint_AllowsAbsentRecipeButRejectsAmbiguousTokens()
		{
			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);

			var fingerprint = new ModOperationFingerprint("game:alpha", context, null);

			Assert.That(fingerprint.RecipeFingerprint, Is.Null);
			Assert.Throws<ArgumentException>(() => new ModOperationFingerprint(" ", context, null));
			Assert.Throws<ArgumentNullException>(() => new ModOperationFingerprint("game:alpha", null, null));
			Assert.Throws<ArgumentException>(() => new ModOperationFingerprint("game:alpha", context, " "));
		}

		/// <summary>
		/// Verifies that operation and attempt identity are independent and an attempt rollover preserves the logical request.
		/// </summary>
		[Test]
		public void Identity_NextAttemptPreservesOperationAndRequest()
		{
			var fingerprint = CreateFingerprint();
			ModOperationIdentity first = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint);

			ModOperationIdentity next = first.CreateNextAttempt();

			Assert.That(first.OperationId, Is.Not.EqualTo(Guid.Empty));
			Assert.That(first.AttemptId, Is.Not.EqualTo(Guid.Empty));
			Assert.That(next.OperationId, Is.EqualTo(first.OperationId));
			Assert.That(next.AttemptId, Is.Not.EqualTo(first.AttemptId));
			Assert.That(next.Origin, Is.EqualTo(first.Origin));
			Assert.That(next.Fingerprint, Is.SameAs(first.Fingerprint));
		}

		/// <summary>
		/// Verifies that identities cannot silently default to an unknown origin or empty identifiers.
		/// </summary>
		[Test]
		public void Identity_RejectsMissingIdentifiersAndUnknownOrigin()
		{
			var fingerprint = CreateFingerprint();
			Guid operationId = Guid.NewGuid();
			Guid attemptId = Guid.NewGuid();

			Assert.Throws<ArgumentException>(() => new ModOperationIdentity(Guid.Empty, attemptId, ModOperationOrigin.Manual, fingerprint));
			Assert.Throws<ArgumentException>(() => new ModOperationIdentity(operationId, Guid.Empty, ModOperationOrigin.Manual, fingerprint));
			Assert.Throws<ArgumentOutOfRangeException>(() => new ModOperationIdentity(operationId, attemptId, ModOperationOrigin.Unknown, fingerprint));
			Assert.Throws<ArgumentOutOfRangeException>(() => new ModOperationIdentity(operationId, attemptId, (ModOperationOrigin)999, fingerprint));
			Assert.Throws<ArgumentNullException>(() => new ModOperationIdentity(operationId, attemptId, ModOperationOrigin.Manual, null));
		}

		/// <summary>
		/// Verifies that reported completion and observed durability remain independent facts.
		/// </summary>
		[Test]
		public void Result_KeepsReportedStatusSeparateFromDurability()
		{
			ModOperationIdentity identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Recovery, CreateFingerprint());

			var failedAfterCommit = new ModOperationResult(identity, ModOperationReportedStatus.Failed,
				ModOperationDurability.VerifiedCommitted, "Post-commit publication failed.");

			Assert.That(failedAfterCommit.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Failed));
			Assert.That(failedAfterCommit.Durability, Is.EqualTo(ModOperationDurability.VerifiedCommitted));
			Assert.That(failedAfterCommit.Message, Is.EqualTo("Post-commit publication failed."));
		}

		/// <summary>
		/// Verifies that a terminal report may remain durability-unknown until authoritative native state is reconciled.
		/// </summary>
		[Test]
		public void Result_AllowsUnknownDurabilityForReconciliation()
		{
			ModOperationIdentity identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Recovery, CreateFingerprint());

			var unresolved = new ModOperationResult(identity, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown, null);

			Assert.That(unresolved.Durability, Is.EqualTo(ModOperationDurability.Unknown));
		}

		/// <summary>
		/// Verifies that cancellation and a verified no-op remain distinct terminal reports.
		/// </summary>
		[Test]
		public void Result_DistinguishesCancellationFromNoOp()
		{
			ModOperationIdentity identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Manual, CreateFingerprint());
			var cancelled = new ModOperationResult(identity, ModOperationReportedStatus.Cancelled, ModOperationDurability.NotStarted, null);
			var noOp = new ModOperationResult(identity, ModOperationReportedStatus.NoOp, ModOperationDurability.NotStarted, null);

			Assert.That(cancelled.ReportedStatus, Is.Not.EqualTo(noOp.ReportedStatus));
			Assert.That(cancelled.Durability, Is.EqualTo(noOp.Durability));
		}

		/// <summary>
		/// Verifies validation of terminal status and durability enum values without coupling the two dimensions.
		/// </summary>
		[Test]
		public void Result_RejectsUnknownReportedStatusAndInvalidDurability()
		{
			ModOperationIdentity identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Profile, CreateFingerprint());

			Assert.Throws<ArgumentOutOfRangeException>(() => new ModOperationResult(identity, ModOperationReportedStatus.Unknown, ModOperationDurability.Unknown, null));
			Assert.Throws<ArgumentOutOfRangeException>(() => new ModOperationResult(identity, (ModOperationReportedStatus)999, ModOperationDurability.Unknown, null));
			Assert.Throws<ArgumentOutOfRangeException>(() => new ModOperationResult(identity, ModOperationReportedStatus.Failed, (ModOperationDurability)999, null));
			Assert.Throws<ArgumentNullException>(() => new ModOperationResult(null, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown, null));
		}

		/// <summary>
		/// Verifies that identified task completion publishes the operation result before completion observers run.
		/// </summary>
		[Test]
		public void TaskSetCompletion_PublishesOperationResultBeforeEventObservers()
		{
			var taskSet = new OperationResultProbeTaskSet();
			ModOperationIdentity identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, CreateFingerprint());
			taskSet.Assign(identity);
			using (var completed = new ManualResetEventSlim(false))
			{
				taskSet.TaskSetCompleted += (sender, args) =>
				{
					Assert.That(taskSet.OperationResult, Is.Not.Null);
					Assert.That(taskSet.OperationResult.Identity, Is.SameAs(identity));
					Assert.That(taskSet.OperationResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Failed));
					Assert.That(taskSet.OperationResult.Durability, Is.EqualTo(ModOperationDurability.VerifiedCommitted));
					completed.Set();
				};

				taskSet.Complete(ModOperationReportedStatus.Failed, ModOperationDurability.VerifiedCommitted, false, "post-commit failure");

				Assert.That(completed.Wait(TimeSpan.FromSeconds(5)), Is.True);
			}
		}

		/// <summary>
		/// Verifies that legacy completion still yields an explicit durability-unknown result for identified tasks.
		/// </summary>
		[Test]
		public void TaskSetCompletion_LegacyCompletionDefaultsDurabilityToUnknown()
		{
			var taskSet = new OperationResultProbeTaskSet();
			taskSet.Assign(ModOperationIdentity.CreateNew(ModOperationOrigin.Manual, CreateFingerprint()));

			taskSet.CompleteLegacy(true, "legacy success");

			Assert.That(taskSet.OperationResult, Is.Not.Null);
			Assert.That(taskSet.OperationResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Succeeded));
			Assert.That(taskSet.OperationResult.Durability, Is.EqualTo(ModOperationDurability.Unknown));
		}

		private sealed class OperationResultProbeTaskSet : ModInstallerBase
		{
			public void Assign(ModOperationIdentity p_moiIdentity)
			{
				AssignOperationIdentity(p_moiIdentity);
			}

			public void Complete(ModOperationReportedStatus p_mrsStatus, ModOperationDurability p_modDurability,
				bool p_booSuccess, string p_strMessage)
			{
				OnTaskSetCompleted(p_mrsStatus, p_modDurability, p_booSuccess, p_strMessage, null);
			}

			public void CompleteLegacy(bool p_booSuccess, string p_strMessage)
			{
				OnTaskSetCompleted(p_booSuccess, p_strMessage, null);
			}
		}

		/// <summary>
		/// Creates the common native-operation fingerprint used by model tests.
		/// </summary>
		private static ModOperationFingerprint CreateFingerprint()
		{
			return new ModOperationFingerprint("game:alpha", new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), null);
		}
	}
}
