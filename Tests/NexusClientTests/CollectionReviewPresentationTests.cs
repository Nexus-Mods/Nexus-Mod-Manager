using System;
using System.Linq;
using NUnit.Framework;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionReviewPresentationTests
	{
		[Test]
		public void ReviewItem_PreservesTypedSemanticsSeparatelyFromDisplayText()
		{
			CollectionMemberKey memberKey = CollectionMemberKey.FromProvider("member-1");
			var item = new CollectionReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.PlannedEffect,
				"Review", "impact.file", "Data/file.txt", "Reviewed winner: member-1", memberKey,
				"Review the change.", "owner stack detail");

			Assert.That(item.Severity, Is.EqualTo(CollectionReviewSeverity.Warning));
			Assert.That(item.Kind, Is.EqualTo(CollectionReviewItemKind.PlannedEffect));
			Assert.That(item.StatusText, Is.EqualTo("Review"));
			Assert.That(item.Code, Is.EqualTo("impact.file"));
			Assert.That(item.Subject, Is.EqualTo("Data/file.txt"));
			Assert.That(item.Explanation, Is.EqualTo("Reviewed winner: member-1"));
			Assert.That(item.MemberKey, Is.SameAs(memberKey));
			Assert.That(item.NextAction, Is.EqualTo("Review the change."));
			Assert.That(item.TechnicalDetail, Is.EqualTo("owner stack detail"));
		}

		[Test]
		public void ReviewItem_RequiresStableCode()
		{
			Assert.Throws<ArgumentException>(() => new CollectionReviewItem(CollectionReviewSeverity.Error,
				CollectionReviewItemKind.Diagnostic, "Blocked", " ", String.Empty, "reason"));
		}

		[Test]
		public void CapabilityClassification_DowngradesUnselectedOptionalIssueWithoutParsingDisplayText()
		{
			Assert.That(CollectionReviewPresentationClassifier.ForCapabilityIssue(CollectionCompatibilityStatus.Unsupported, true),
				Is.EqualTo(CollectionReviewSeverity.Error));
			Assert.That(CollectionReviewPresentationClassifier.ForCapabilityIssue(CollectionCompatibilityStatus.Unsupported, false),
				Is.EqualTo(CollectionReviewSeverity.Warning));
		}

		[Test]
		public void AcquisitionClassification_KeepsManualInputDistinctFromBackgroundProgress()
		{
			Assert.That(CollectionReviewPresentationClassifier.ForAcquisition(CollectionMemberAcquisitionDisposition.ManualInputRequired),
				Is.EqualTo(CollectionReviewSeverity.Error));
			Assert.That(CollectionReviewPresentationClassifier.KindForAcquisition(CollectionMemberAcquisitionDisposition.ManualInputRequired),
				Is.EqualTo(CollectionReviewItemKind.ManualAction));
			Assert.That(CollectionReviewPresentationClassifier.ForAcquisition(CollectionMemberAcquisitionDisposition.PremiumQueued),
				Is.EqualTo(CollectionReviewSeverity.Info));
			Assert.That(CollectionReviewPresentationClassifier.KindForAcquisition(CollectionMemberAcquisitionDisposition.PremiumQueued),
				Is.EqualTo(CollectionReviewItemKind.Progress));
		}

		[Test]
		public void ReviewFilter_DefaultErrorOnly_HidesWarningAndInfoDiagnostics()
		{
			var error = new CollectionReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
				"Blocked", "diagnostic.error", String.Empty, "error");
			var warning = new CollectionReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic,
				"Review", "diagnostic.warning", String.Empty, "warning");
			var info = new CollectionReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress,
				"Ready", "progress.info", String.Empty, "info");

			Assert.That(CollectionReviewPresentationFilter.MatchesSeverity(error, true, false, false), Is.True);
			Assert.That(CollectionReviewPresentationFilter.MatchesSeverity(warning, true, false, false), Is.False);
			Assert.That(CollectionReviewPresentationFilter.MatchesSeverity(info, true, false, false), Is.False);
		}

		[Test]
		public void ReviewFilter_SeverityTogglesAreIndependent()
		{
			var warning = new CollectionReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic,
				"Review", "diagnostic.warning", String.Empty, "warning");

			Assert.That(CollectionReviewPresentationFilter.MatchesSeverity(warning, false, true, false), Is.True);
			Assert.That(CollectionReviewPresentationFilter.MatchesSeverity(warning, true, false, true), Is.False);
		}

		[Test]
		public void ReviewFilter_ManualActionsAndPlannedEffectsAreOutsideSeverityFilteredIssueList()
		{
			var action = new CollectionReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.ManualAction,
				"Action required", "manual.download", String.Empty, "download");
			var effect = new CollectionReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.PlannedEffect,
				"Review", "impact.file", "Data/file.txt", "winner");

			Assert.That(CollectionReviewPresentationFilter.IsSeverityFiltered(action), Is.False);
			Assert.That(CollectionReviewPresentationFilter.IsSeverityFiltered(effect), Is.False);
			Assert.That(CollectionReviewPresentationFilter.IsPersistentReviewAction(action), Is.True);
			Assert.That(CollectionReviewPresentationFilter.IsPersistentReviewAction(effect), Is.True);
		}

		[Test]
		public void ReviewFilter_DiagnosticsAndProgressRemainSeverityFilterable()
		{
			var diagnostic = new CollectionReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
				"Blocked", "diagnostic.error", String.Empty, "error");
			var progress = new CollectionReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress,
				"Ready", "progress.info", String.Empty, "info");

			Assert.That(CollectionReviewPresentationFilter.IsSeverityFiltered(diagnostic), Is.True);
			Assert.That(CollectionReviewPresentationFilter.IsSeverityFiltered(progress), Is.True);
			Assert.That(CollectionReviewPresentationFilter.IsPersistentReviewAction(diagnostic), Is.False);
			Assert.That(CollectionReviewPresentationFilter.IsPersistentReviewAction(progress), Is.False);
		}

		[Test]
		public void ReviewOrder_ConcreteBlockersComeBeforeSummaryAndReadyArchives()
		{
			var ready = new CollectionReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress,
				"Ready", "acquisition.ready", "Mod A", "Archive ready");
			var summary = new CollectionReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
				"Paused", "workflow.preparation", "Collection", "Needs attention");
			var blocker = new CollectionReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
				"Action required", "impact.existingpluginstatedecisionrequired", "Mod B", "Plugin needs enabling");
			var warning = new CollectionReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic,
				"Warning", "manifest.guidance", "Collection", "Curator advice");

			CollectionReviewItem[] sorted = new[] { summary, ready, warning, blocker }
				.OrderBy(CollectionReviewPresentationFilter.GetIssuePriority).ToArray();

			Assert.That(sorted, Is.EqualTo(new[] { blocker, summary, warning, ready }));
			Assert.That(sorted.All(x => CollectionReviewPresentationFilter.MatchesSeverity(x, true, true, true)), Is.True);
		}

		[Test]
		public void WorkflowClassification_UsesDomainStateRatherThanLocalizedStatusLabels()
		{
			Assert.That(CollectionReviewPresentationClassifier.ForPreparation(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview, false, false),
				Is.EqualTo(CollectionReviewSeverity.Info));
			Assert.That(CollectionReviewPresentationClassifier.ForPreparation(CollectionAdditiveWorkflowPreparationStatus.ActionRequired, false, false),
				Is.EqualTo(CollectionReviewSeverity.Error));
			Assert.That(CollectionReviewPresentationClassifier.ForImpact(CollectionConflictImpactStatus.ActionRequired),
				Is.EqualTo(CollectionReviewSeverity.Error));
			Assert.That(CollectionReviewPresentationClassifier.ForRecovery(CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume),
				Is.EqualTo(CollectionReviewSeverity.Warning));
			Assert.That(CollectionReviewPresentationClassifier.ForApply(CollectionAdditiveWorkflowApplyStatus.Committed),
				Is.EqualTo(CollectionReviewSeverity.Info));
			Assert.That(CollectionReviewPresentationClassifier.ForAssociation(CollectionAssociationState.Modified),
				Is.EqualTo(CollectionReviewSeverity.Warning));
		}
	}
}
