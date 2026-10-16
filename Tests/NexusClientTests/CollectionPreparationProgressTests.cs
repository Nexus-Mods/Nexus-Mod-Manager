using System;
using System.Collections.Generic;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies that changing member stages retain their fixed counter and captured callbacks stay with their operation.</summary>
	[TestFixture]
	public class CollectionPreparationProgressTests
	{
		/// <summary>Preparation, layout and finishing statuses share one prefix, which ends with the member scope.</summary>
		[Test]
		public void MemberStages_KeepCounterUntilMemberFinishes()
		{
			var progress = new RecordedProgress();
			using (CollectionPreparationProgress.Begin(progress))
			{
				using (CollectionPreparationProgress.BeginMember(28, 104))
				{
					CollectionPreparationProgress.Report("Preparing installation instructions: Grass", true);
					CollectionPreparationProgress.Report("Resolving mod file layout: Grass (23%)", true);
					CollectionPreparationProgress.Report("Finishing mod installation: Grass", true);
					Assert.That(CollectionPreparationProgress.HasMemberCounter, Is.True);
				}
				CollectionPreparationProgress.Report("Checking mod file conflicts...", true);
				Assert.That(CollectionPreparationProgress.HasMemberCounter, Is.False);
			}
			CollectionAssert.AreEqual(new[]
			{
				"[28/104] - Preparing installation instructions: Grass",
				"[28/104] - Resolving mod file layout: Grass (23%)",
				"[28/104] - Finishing mod installation: Grass",
				"Checking mod file conflicts..."
			}, progress.Messages);
		}

		/// <summary>Archive-thread callbacks retain the originating operation and member even after the ambient scope advances.</summary>
		[Test]
		public void CapturedStatus_RetainsOriginalOperationAndCounter()
		{
			var first = new RecordedProgress();
			var second = new RecordedProgress();
			Action<string> callback;
			using (CollectionPreparationProgress.Begin(first))
			using (CollectionPreparationProgress.BeginMember(28, 104))
				callback = CollectionPreparationProgress.CaptureStatusReporter();
			using (CollectionPreparationProgress.Begin(second))
			using (CollectionPreparationProgress.BeginMember(29, 104))
			{
				callback("Resolving mod file layout: First mod (23%)");
				CollectionPreparationProgress.Report("Preparing installation instructions: Second mod", true);
			}
			CollectionAssert.AreEqual(new[] { "[28/104] - Resolving mod file layout: First mod (23%)" }, first.Messages);
			CollectionAssert.AreEqual(new[] { "[29/104] - Preparing installation instructions: Second mod" }, second.Messages);
		}

		/// <summary>Additional archive algorithm information is appended after the status without losing or duplicating the counter.</summary>
		[Test]
		public void ArchiveAlgorithmStatus_KeepsSingleCounterPrefix()
		{
			var progress = new RecordedProgress();
			using (CollectionPreparationProgress.Begin(progress))
			using (CollectionPreparationProgress.BeginMember(3, 10))
			{
				CollectionPreparationProgress.Report("Checking archive: mod.zip", true);
				CollectionPreparationProgress.ReportArchiveHashing(false);
			}
			Assert.That(progress.Messages[1], Is.EqualTo("[3/10] - Checking archive: mod.zip [SHA-256]"));
		}

		/// <summary>Failure cleanup restores the enclosing member counter instead of leaving the failed member's progress active.</summary>
		[Test]
		public void NestedMemberFailure_RestoresPreviousCounter()
		{
			var progress = new RecordedProgress();
			using (CollectionPreparationProgress.Begin(progress))
			using (CollectionPreparationProgress.BeginMember(3, 10))
			{
				Assert.Throws<InvalidOperationException>(() =>
				{
					using (CollectionPreparationProgress.BeginMember(4, 10))
						throw new InvalidOperationException("Interrupted member");
				});
				CollectionPreparationProgress.Report("Preparing installation instructions: Original mod", true);
			}
			CollectionAssert.AreEqual(new[] { "[3/10] - Preparing installation instructions: Original mod" }, progress.Messages);
		}

		/// <summary>Records reports synchronously so UI dispatch timing does not affect progress assertions.</summary>
		private sealed class RecordedProgress : IProgress<string>
		{
			internal readonly List<string> Messages = new List<string>();

			/// <summary>Records one formatted status without scheduling UI callbacks.</summary>
			public void Report(string value)
			{
				Messages.Add(value);
			}
		}
	}
}
