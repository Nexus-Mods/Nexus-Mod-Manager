using System;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionExecutionPolicyTests
	{
		[Test]
		public void InstallIntoCurrentSetup_PreservesUnrelatedManagedStateAndHasNoReplacementBackupDecision()
		{
			CollectionExecutionPolicy policy = CollectionExecutionPolicy.InstallIntoCurrentSetup();

			Assert.That(policy.Kind, Is.EqualTo(CollectionExecutionPolicyKind.InstallIntoCurrentSetup));
			Assert.That(policy.PreservesUnrelatedManagedModsByDefault, Is.True);
			Assert.That(policy.MayRemoveOutgoingManagedEffects, Is.False);
			Assert.That(policy.PreservesArchiveLibrary, Is.True);
			Assert.That(policy.PreservesUnknownOrUnmanagedFiles, Is.True);
			Assert.That(policy.RequiresReviewedRemovalScope, Is.False);
			Assert.That(policy.OffersOptionalLocalCollectionBackup, Is.False);
			Assert.That(policy.UsesIncomingIntendedEnvironment, Is.False);
			Assert.That(policy.ReplacementBackupChoice, Is.EqualTo(CollectionReplacementBackupChoice.NotApplicable));
			Assert.That(policy.HasResolvedReplacementBackupDecision, Is.True);
		}

		[Test]
		public void Replacement_IsExplicitReviewedTransitionThatPreservesArchivesAndUnknownFiles()
		{
			CollectionExecutionPolicy policy = CollectionExecutionPolicy.ReplaceCurrentManagedSetup();

			Assert.That(policy.Kind, Is.EqualTo(CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup));
			Assert.That(policy.PreservesUnrelatedManagedModsByDefault, Is.False);
			Assert.That(policy.MayRemoveOutgoingManagedEffects, Is.True);
			Assert.That(policy.PreservesArchiveLibrary, Is.True);
			Assert.That(policy.PreservesUnknownOrUnmanagedFiles, Is.True);
			Assert.That(policy.RequiresReviewedRemovalScope, Is.True);
			Assert.That(policy.OffersOptionalLocalCollectionBackup, Is.True);
			Assert.That(policy.UsesIncomingIntendedEnvironment, Is.True);
			Assert.That(policy.ReplacementBackupChoice, Is.EqualTo(CollectionReplacementBackupChoice.Undecided));
			Assert.That(policy.HasResolvedReplacementBackupDecision, Is.False);
		}

		[Test]
		public void Replacement_BackupDecisionMustBeExplicitBeforeLaterDestructiveBoundary()
		{
			CollectionExecutionPolicy undecided = CollectionExecutionPolicy.ReplaceCurrentManagedSetup();
			CollectionExecutionPolicy withBackup = undecided.WithReplacementBackupChoice(CollectionReplacementBackupChoice.CreateLocalCollection);
			CollectionExecutionPolicy withoutBackup = undecided.WithReplacementBackupChoice(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection);

			Assert.That(undecided.HasResolvedReplacementBackupDecision, Is.False);
			Assert.That(withBackup.HasResolvedReplacementBackupDecision, Is.True);
			Assert.That(withBackup.ReplacementBackupChoice, Is.EqualTo(CollectionReplacementBackupChoice.CreateLocalCollection));
			Assert.That(withoutBackup.HasResolvedReplacementBackupDecision, Is.True);
			Assert.That(withoutBackup.ReplacementBackupChoice, Is.EqualTo(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection));
		}

		[Test]
		public void AdditivePolicy_CannotCarryReplacementBackupChoice()
		{
			CollectionExecutionPolicy policy = CollectionExecutionPolicy.InstallIntoCurrentSetup();

			Assert.Throws<InvalidOperationException>(() =>
				policy.WithReplacementBackupChoice(CollectionReplacementBackupChoice.CreateLocalCollection));
		}

		[Test]
		public void ReplacementFactory_RejectsNotApplicableAndUndefinedBackupChoices()
		{
			Assert.Throws<ArgumentException>(() =>
				CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.NotApplicable));
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				CollectionExecutionPolicy.ReplaceCurrentManagedSetup((CollectionReplacementBackupChoice)999));
		}

		[Test]
		public void PolicyEquality_IncludesReplacementBackupDecision()
		{
			CollectionExecutionPolicy first = CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.CreateLocalCollection);
			CollectionExecutionPolicy same = CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.CreateLocalCollection);
			CollectionExecutionPolicy differentDecision = CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection);
			CollectionExecutionPolicy additive = CollectionExecutionPolicy.InstallIntoCurrentSetup();

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
			Assert.That(first, Is.Not.EqualTo(differentDecision));
			Assert.That(first, Is.Not.EqualTo(additive));
		}

		[Test]
		public void ExecutionPolicy_ExposesNoPublicPropertySetters()
		{
			foreach (var property in typeof(CollectionExecutionPolicy).GetProperties())
				Assert.That(property.GetSetMethod(), Is.Null, property.Name + " must remain immutable.");
		}
	}
}
