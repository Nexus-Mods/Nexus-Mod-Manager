using System;
using NUnit.Framework;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionUserMessagePresentationTests
	{
		[Test]
		public void InternalTerminologySanitizer_RemovesStageAndGateLabelsFromUserText()
		{
			string source = "C6.7 stopped after C6.4 review; C7.10b2, C5 and Gate A details remain.";
			string presented = CollectionUserMessagePresenter.SanitizeInternalTerminology(source);

			Assert.That(presented, Does.Not.Contain("C6."));
			Assert.That(presented, Does.Not.Contain("C7."));
			Assert.That(presented, Does.Not.Contain("Gate A"));
			Assert.That(presented, Does.Not.Contain("C5"));
			Assert.That(presented, Does.Contain("installation"));
			Assert.That(presented, Does.Contain("change-impact review"));
		}

		[Test]
		public void PreparationPresentation_UsesStructuredStatusAndPreservesRawTechnicalDetail()
		{
			const string raw = "C6.7 technical diagnostic";
			CollectionUserMessagePresentation presentation = CollectionUserMessagePresenter.ForPreparation(
				CollectionAdditiveWorkflowPreparationStatus.ReadyForReview, raw, false, false);

			Assert.That(presentation.MessageFallback, Does.Not.Contain("C6."));
			Assert.That(presentation.MessageFallback, Does.Contain("ready for review"));
			Assert.That(presentation.NextActionFallback, Does.Contain("Review and install"));
			Assert.That(presentation.TechnicalDetail, Is.EqualTo(raw));
		}

		[Test]
		public void FailurePresentation_DoesNotEchoInternalExceptionAsPrimaryUserMessage()
		{
			const string raw = "C6.9 unexpected native state id=123";
			CollectionUserMessagePresentation presentation = CollectionUserMessagePresenter.ForFailure("workflow.apply-failed", raw);

			Assert.That(presentation.MessageFallback, Is.EqualTo("Installation stopped unexpectedly before NMM could present a verified final result."));
			Assert.That(presentation.NextActionFallback, Does.Contain("recovery status"));
			Assert.That(presentation.TechnicalDetail, Is.EqualTo(raw));
		}

		[Test]
		public void AcquisitionPresentation_HidesRawDispositionBehindUserMeaning()
		{
			const string raw = "ManualInputRequired - Browser, Nxm";
			CollectionUserMessagePresentation presentation = CollectionUserMessagePresenter.ForAcquisition(
				CollectionMemberAcquisitionDisposition.ManualInputRequired, raw);

			Assert.That(presentation.MessageFallback, Does.Not.Contain("ManualInputRequired"));
			Assert.That(presentation.NextActionFallback, Does.Contain("Check downloads and continue"));
			Assert.That(presentation.TechnicalDetail, Is.EqualTo(raw));
		}

		[Test]
		public void LocalRestorePresentation_ExplainsStateChangeWithoutPhaseNames()
		{
			CollectionUserMessagePresentation presentation = CollectionUserMessagePresenter.ForLocalRestore(
				CollectionLocalRestoreWorkflowStatus.CurrentStateChanged, "C7.9 review no longer matches");

			Assert.That(presentation.MessageFallback, Does.Not.Contain("C7."));
			Assert.That(presentation.MessageFallback, Does.Contain("current managed setup changed"));
			Assert.That(presentation.NextActionFallback, Does.Contain("review the Local Collection restore again"));
		}
		[Test]
		public void BlockedAcquisitionPreparation_ExplainsWhatMustBeResolved()
		{
			CollectionUserMessagePresentation presentation = CollectionUserMessagePresenter.ForPreparation(
				CollectionAdditiveWorkflowPreparationStatus.AwaitingInput, "C6.15.5 blocked acquisition", false, true);

			Assert.That(presentation.MessageFallback, Does.Contain("cannot be acquired safely"));
			Assert.That(presentation.NextActionFallback, Does.Contain("blocked mod"));
			Assert.That(presentation.TechnicalDetail, Does.Contain("C6.15.5"));
		}

		[Test]
		public void ProviderPresentation_DoesNotExposeRawProviderFailureAsPrimaryMessage()
		{
			const string raw = "C6.15 provider field error: internal-id=42";
			CollectionUserMessagePresentation presentation = CollectionUserMessagePresenter.ForProviderMessage(raw, true);

			Assert.That(presentation.MessageFallback, Is.EqualTo("Nexus could not provide the Collection data required for this step."));
			Assert.That(presentation.MessageFallback, Does.Not.Contain("internal-id"));
			Assert.That(presentation.TechnicalDetail, Is.EqualTo(raw));
		}

		[Test]
		public void RetainedSourcePresentation_KeepsTechnicalCauseOutOfNormalUserText()
		{
			const string raw = "C7.4 retained payload path mismatch";
			CollectionUserMessagePresentation presentation = CollectionUserMessagePresenter.ForRetainedSourceIssue(raw);

			Assert.That(presentation.MessageFallback, Does.Not.Contain("C7."));
			Assert.That(presentation.MessageFallback, Does.Contain("retained source details"));
			Assert.That(presentation.NextActionFallback, Does.Contain("installed Collection"));
			Assert.That(presentation.TechnicalDetail, Is.EqualTo(raw));
		}

	}
}
