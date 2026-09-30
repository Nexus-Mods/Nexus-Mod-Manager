using System;
using System.Collections.Generic;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement.UI;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionWorkflowActivityTests
	{
		[Test]
		public void ForegroundWorkLocksCommandsAndIsActive()
		{
			CollectionWorkflowActivitySnapshot snapshot = CollectionWorkflowActivityBuilder.Foreground(
				CollectionWorkflowActivityPhase.Preparing, "Preparing");

			Assert.That(snapshot.State, Is.EqualTo(CollectionWorkflowActivityState.Working));
			Assert.That(snapshot.CommandsLocked, Is.True);
			Assert.That(snapshot.IsWorkActive, Is.True);
			Assert.That(snapshot.IsBackgroundContinuation, Is.False);
		}

		[Test]
		public void AcquisitionDeduplicatesSharedProducerIdentity()
		{
			Guid shared = Guid.NewGuid();
			var producers = new List<CollectionWorkflowProducerActivity>
			{
				new CollectionWorkflowProducerActivity(shared, TaskStatus.Running),
				new CollectionWorkflowProducerActivity(shared, TaskStatus.Running),
				new CollectionWorkflowProducerActivity(Guid.NewGuid(), TaskStatus.Complete)
			};

			CollectionWorkflowActivitySnapshot snapshot = CollectionWorkflowActivityBuilder.FromAcquisition(producers, false, "Downloading");

			Assert.That(snapshot.State, Is.EqualTo(CollectionWorkflowActivityState.Working));
			Assert.That(snapshot.CommandsLocked, Is.False);
			Assert.That(snapshot.IsBackgroundContinuation, Is.True);
			Assert.That(snapshot.Current, Is.EqualTo(1));
			Assert.That(snapshot.Total, Is.EqualTo(2));
		}

		[Test]
		public void ManualAcquisitionIsWaitingNotActiveWork()
		{
			CollectionWorkflowActivitySnapshot snapshot = CollectionWorkflowActivityBuilder.FromAcquisition(
				new CollectionWorkflowProducerActivity[0], true, "Waiting for download");

			Assert.That(snapshot.State, Is.EqualTo(CollectionWorkflowActivityState.WaitingForUser));
			Assert.That(snapshot.CommandsLocked, Is.False);
			Assert.That(snapshot.IsWorkActive, Is.False);
		}

		[Test]
		public void QueuedProducerDoesNotPretendToBeRunning()
		{
			CollectionWorkflowActivitySnapshot snapshot = CollectionWorkflowActivityBuilder.FromAcquisition(
				new[] { new CollectionWorkflowProducerActivity(Guid.NewGuid(), TaskStatus.Queued) }, false, "Queued");

			Assert.That(snapshot.State, Is.EqualTo(CollectionWorkflowActivityState.Queued));
			Assert.That(snapshot.IsWorkActive, Is.False);
			Assert.That(snapshot.IsBackgroundContinuation, Is.True);
		}

		[Test]
		public void FailedProducerStopsActiveIndicator()
		{
			CollectionWorkflowActivitySnapshot snapshot = CollectionWorkflowActivityBuilder.FromAcquisition(
				new[] { new CollectionWorkflowProducerActivity(Guid.NewGuid(), TaskStatus.Error) }, false, "Failed");

			Assert.That(snapshot.State, Is.EqualTo(CollectionWorkflowActivityState.Failed));
			Assert.That(snapshot.IsWorkActive, Is.False);
		}

		[Test]
		public void ForegroundWorkingPresentationAnimatesAndCoversMemberEditor()
		{
			CollectionWorkflowActivitySnapshot snapshot = CollectionWorkflowActivityBuilder.Foreground(
				CollectionWorkflowActivityPhase.Preparing, "Preparing");

			CollectionWorkflowActivityPresentation presentation = CollectionWorkflowActivityPresentationBuilder.Build(snapshot);

			Assert.That(presentation.AnimateIcon, Is.True);
			Assert.That(presentation.ShowMemberLoadingOverlay, Is.True);
			Assert.That(presentation.IconText, Is.Not.Empty);
		}

		[Test]
		public void BackgroundAcquisitionAnimatesWithoutCoveringMemberEditor()
		{
			CollectionWorkflowActivitySnapshot snapshot = CollectionWorkflowActivityBuilder.FromAcquisition(
				new[] { new CollectionWorkflowProducerActivity(Guid.NewGuid(), TaskStatus.Running) }, false, "Downloading");

			CollectionWorkflowActivityPresentation presentation = CollectionWorkflowActivityPresentationBuilder.Build(snapshot);

			Assert.That(presentation.AnimateIcon, Is.True);
			Assert.That(presentation.ShowMemberLoadingOverlay, Is.False);
		}

		[Test]
		public void WaitingForUserUsesStaticIndicatorAndKeepsMemberEditorVisible()
		{
			CollectionWorkflowActivitySnapshot snapshot = CollectionWorkflowActivityBuilder.Waiting(
				CollectionWorkflowActivityPhase.Acquiring, "Waiting", false);

			CollectionWorkflowActivityPresentation presentation = CollectionWorkflowActivityPresentationBuilder.Build(snapshot);

			Assert.That(presentation.AnimateIcon, Is.False);
			Assert.That(presentation.ShowMemberLoadingOverlay, Is.False);
			Assert.That(presentation.IconText, Is.EqualTo("!"));
		}

		[Test]
		public void FailedAndCompletedStatesUseDifferentStaticIndicators()
		{
			CollectionWorkflowActivityPresentation failed = CollectionWorkflowActivityPresentationBuilder.Build(
				CollectionWorkflowActivityBuilder.Failed(CollectionWorkflowActivityPhase.Applying, "Failed"));
			CollectionWorkflowActivityPresentation completed = CollectionWorkflowActivityPresentationBuilder.Build(
				CollectionWorkflowActivityBuilder.Completed(CollectionWorkflowActivityPhase.Verifying, "Completed"));

			Assert.That(failed.AnimateIcon, Is.False);
			Assert.That(completed.AnimateIcon, Is.False);
			Assert.That(failed.ShowMemberLoadingOverlay, Is.False);
			Assert.That(completed.ShowMemberLoadingOverlay, Is.False);
			Assert.That(failed.IconText, Is.Not.EqualTo(completed.IconText));
		}

		[Test]
		public void AcquisitionEtaWaitsForStableByteProgressBeforePublishingNumber()
		{
			Guid producer = Guid.NewGuid();
			var estimator = new CollectionWorkflowEtaEstimator();

			CollectionWorkflowEtaSnapshot first = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 100, 1000, 100)
			});
			CollectionWorkflowEtaSnapshot second = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 200, 1000, 100)
			});
			CollectionWorkflowEtaSnapshot third = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 300, 1000, 100)
			});
			CollectionWorkflowEtaSnapshot fourth = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 400, 1000, 100)
			});

			Assert.That(first.IsEstimating, Is.True);
			Assert.That(second.IsEstimating, Is.True);
			Assert.That(third.IsEstimating, Is.True);
			Assert.That(fourth.IsAvailable, Is.True);
			Assert.That(fourth.Remaining.Value.TotalSeconds, Is.EqualTo(6D).Within(0.2D));
			Assert.That(fourth.Basis, Is.EqualTo("acquisition-known-bytes-smoothed-throughput-v1"));
		}

		[Test]
		public void AcquisitionEtaDeduplicatesSharedProducerMetrics()
		{
			Guid producer = Guid.NewGuid();
			var estimator = new CollectionWorkflowEtaEstimator();
			for (int progress = 100; progress <= 300; progress += 100)
			{
				estimator.UpdateAcquisition(new[]
				{
					new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, progress, 1000, 100),
					new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, progress, 1000, 100)
				});
			}
			CollectionWorkflowEtaSnapshot estimate = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 400, 1000, 100),
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 400, 1000, 100)
			});

			Assert.That(estimate.IsAvailable, Is.True);
			Assert.That(estimate.RemainingBytes, Is.EqualTo(600));
			Assert.That(estimate.BytesPerSecond.Value, Is.EqualTo(100D).Within(0.1D));
		}

		[Test]
		public void AcquisitionEtaStaysEstimatingWhenAQueuedProducerHasUnknownSize()
		{
			Guid running = Guid.NewGuid();
			Guid queued = Guid.NewGuid();
			var estimator = new CollectionWorkflowEtaEstimator();
			CollectionWorkflowEtaSnapshot result = null;
			for (int progress = 100; progress <= 500; progress += 100)
			{
				result = estimator.UpdateAcquisition(new[]
				{
					new CollectionWorkflowProducerActivity(running, TaskStatus.Running, true, progress, 1000, 100),
					new CollectionWorkflowProducerActivity(queued, TaskStatus.Queued)
				});
			}

			Assert.That(result.IsAvailable, Is.False);
			Assert.That(result.IsEstimating, Is.True);
			Assert.That(result.Basis, Is.EqualTo("acquisition-waiting-for-known-byte-totals"));
		}

		[Test]
		public void AcquisitionEtaResetsWhenProgressDomainChanges()
		{
			Guid producer = Guid.NewGuid();
			var estimator = new CollectionWorkflowEtaEstimator();
			for (int progress = 100; progress <= 400; progress += 100)
				estimator.UpdateAcquisition(new[] { new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, progress, 1000, 100) });

			CollectionWorkflowEtaSnapshot changed = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 1, 5, 100)
			});

			Assert.That(changed.IsAvailable, Is.False);
			Assert.That(changed.IsEstimating, Is.True);
		}

		[Test]
		public void AcquisitionEtaDoesNotSamplePausedOrRetryingTime()
		{
			Guid producer = Guid.NewGuid();
			var estimator = new CollectionWorkflowEtaEstimator();
			estimator.UpdateAcquisition(new[] { new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 100, 1000, 100) });
			estimator.UpdateAcquisition(new[] { new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 200, 1000, 100) });

			CollectionWorkflowEtaSnapshot paused = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Paused)
			});
			CollectionWorkflowEtaSnapshot resumed = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 300, 1000, 100)
			});

			Assert.That(paused.IsAvailable, Is.False);
			Assert.That(paused.IsEstimating, Is.False);
			Assert.That(resumed.IsAvailable, Is.False);
			Assert.That(resumed.IsEstimating, Is.True);
		}

		[Test]
		public void AcquisitionEtaSuppressesAbruptThroughputChanges()
		{
			Guid producer = Guid.NewGuid();
			var estimator = new CollectionWorkflowEtaEstimator();
			for (int progress = 100; progress <= 400; progress += 100)
				estimator.UpdateAcquisition(new[] { new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, progress, 2000, 100) });

			CollectionWorkflowEtaSnapshot unstable = estimator.UpdateAcquisition(new[]
			{
				new CollectionWorkflowProducerActivity(producer, TaskStatus.Running, true, 500, 2000, 1000)
			});

			Assert.That(unstable.IsAvailable, Is.False);
			Assert.That(unstable.IsEstimating, Is.True);
			Assert.That(unstable.Basis, Is.EqualTo("acquisition-throughput-changed"));
		}

	}
}
