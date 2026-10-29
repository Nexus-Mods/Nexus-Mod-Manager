using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DevExpress.XtraBars.Docking2010;
using DevExpress.XtraBars.Docking2010.Views.Tabbed;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	/// <summary>Exercises restored Collection sections inside the same tabbed MDI host used by NMM.</summary>
	[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
	public class CollectionsPreviewLayoutTests
	{
		/// <summary>Incoming previews must expose their stale installation recovery without requiring an installed association view.</summary>
		[TestCase(true, 0, true)]
		[TestCase(false, 0, false)]
		[TestCase(true, 1, false)]
		public void IncomingPreviewFindsOnlyItsCurrentInstallationRecovery(bool sameRevision, int generation, bool expected)
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("93228"), "779225", 58);
			CollectionRevisionIdentity displayed = sameRevision ? revision :
				CollectionRevisionIdentity.FromNexus(revision.Collection, "779226", 59);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				revision.Collection, CollectionTargetIdentity.FromFingerprint("target-preview-recovery"), revision,
				CollectionPlanIdentity.From(Guid.NewGuid(), 1), 6, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
			var recovery = new CollectionAdditiveWorkflowRecoveryResult(CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired,
				operation, null, "Native state changed.");
			using (CollectionsPreviewControl collections = new CollectionsPreviewControl())
			{
				typeof(CollectionsPreviewControl).GetField("_displayContext", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(collections, CollectionUiContext.Incoming(generation, displayed, operation.Identity));
				typeof(CollectionsPreviewControl).GetField("_recoveryResults", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(collections, new[] { recovery });
				object found = typeof(CollectionsPreviewControl).GetMethod("GetInterruptedInstallationRecovery", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(collections, null);
				if (expected) Assert.That(found, Is.SameAs(recovery));
				else Assert.That(found, Is.Null);
			}
		}

		/// <summary>A returned apply result must unlock the UI before result feedback can enter a modal message loop.</summary>
		[TestCase(CollectionAdditiveWorkflowApplyStatus.StoppedPartial, CollectionOperationPhase.Completed, CollectionOperationResultState.StoppedPartial)]
		[TestCase(CollectionAdditiveWorkflowApplyStatus.Committed, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed)]
		[TestCase(CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary, CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending)]
		[TestCase(CollectionAdditiveWorkflowApplyStatus.RecoveryRequired, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired)]
		[TestCase(CollectionAdditiveWorkflowApplyStatus.RepreparationRequired, CollectionOperationPhase.Completed, CollectionOperationResultState.FailedBeforeApply)]
		public void ApplyResultReleasesBusyOverlayAndReportCommands(CollectionAdditiveWorkflowApplyStatus status,
			CollectionOperationPhase phase, CollectionOperationResultState resultState)
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("93228"), "779225", 58);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				revision.Collection, CollectionTargetIdentity.FromFingerprint("target-preview-completion"), revision,
				CollectionPlanIdentity.From(Guid.NewGuid(), 1), 6, phase, resultState, new CollectionNativeChildOperation[0]);
			var result = new CollectionAdditiveWorkflowApplyResult(status, operation, null, "Retained installation outcome");
			CollectionUiContext context = CollectionUiContext.Incoming(0, revision, operation.Identity);
			using (CollectionsPreviewControl collections = new CollectionsPreviewControl())
			{
				typeof(CollectionsPreviewControl).GetField("_displayContext", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(collections, context);
				typeof(CollectionsPreviewControl).GetMethod("BeginWorkflowWork", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(collections, new object[] { context, CollectionWorkflowActivityPhase.Applying, "Finishing the last mod" });
				Assert.That(collections.HasActiveSessionWork, Is.True);
				Assert.That(GetField<bool>(collections, "_wasShowingMemberLoadingOverlay"), Is.True);

				typeof(CollectionsPreviewControl).GetMethod("CompleteAdditiveApply", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(collections, new object[] { result, context });

				Assert.That(collections.HasActiveSessionWork, Is.False);
				Assert.That(GetField<bool>(collections, "_wasShowingMemberLoadingOverlay"), Is.False);
				Assert.That(GetField<System.Windows.Forms.Timer>(collections, "_workflowActivityAnimationTimer").Enabled, Is.False);
				Assert.That(GetField<Control>(collections, "_membersGrid").Enabled, Is.True);
				Assert.That(GetField<Control>(collections, "_exportTechnicalReportButton").Enabled, Is.True);
				Assert.That(GetField<CollectionOperation>(collections, "_operationSnapshot"), Is.SameAs(operation));
				Assert.That(GetField<Control>(collections, "_workflowStatusLabel").Text, Does.Not.Contain("Finishing the last mod"));
				if (status == CollectionAdditiveWorkflowApplyStatus.StoppedPartial)
					Assert.That(GetField<CollectionWorkflowActivitySnapshot>(collections, "_workflowActivity").State,
						Is.EqualTo(CollectionWorkflowActivityState.Failed));

				// The caller's existing finally block remains safe after early completion cleanup.
				typeof(CollectionsPreviewControl).GetMethod("EndWorkflowWork", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(collections, new object[] { context });
				Assert.That(collections.HasActiveSessionWork, Is.False);
				Assert.That(GetField<Control>(collections, "_exportTechnicalReportButton").Enabled, Is.True);
			}
		}

		/// <summary>Loads an installed context while another document is selected, then opens Collections.</summary>
		[TestCase(2000, 900)]
		[TestCase(900, 1100)]
		public void InstalledCollectionLoadedBeforeActivationKeepsManagementAndDetails(int width, int height)
		{
			WithHiddenCollection(width, height, (host, tabs, mods, collections) =>
			{
				SetContext(collections, true);
				tabs.ActivateDocument(collections);
				Application.DoEvents();
				AssertInstalledSections(collections);
			});
		}

		/// <summary>Keeps the selector and tabs available after hiding, resizing and changing the UI font.</summary>
		[Test]
		public void ReturningToInstalledCollectionRemeasuresSections()
		{
			WithHiddenCollection(2000, 900, (host, tabs, mods, collections) =>
			{
				SetContext(collections, true);
				tabs.ActivateDocument(collections);
				Application.DoEvents();
				AssertInstalledSections(collections);
				tabs.ActivateDocument(mods);
				Application.DoEvents();
				host.ClientSize = new Size(1400, 1000);
				using (Font font = new Font(collections.Font.FontFamily, 11))
				{
					collections.Font = font;
					tabs.ActivateDocument(collections);
					Application.DoEvents();
					AssertInstalledSections(collections);
				}
			});
		}

		/// <summary>Preserves the intentional empty view and restores the sections when an installed context arrives.</summary>
		[Test]
		public void EmptyViewDoesNotPermanentlyCollapseInstalledSections()
		{
			WithHiddenCollection(2000, 900, (host, tabs, mods, collections) =>
			{
				tabs.ActivateDocument(collections);
				Application.DoEvents();
				Assert.That(GetField<Control>(collections, "_installedGroup").Visible, Is.False);
				Assert.That(GetField<Control>(collections, "_detailsSection").Visible, Is.False);
				SetContext(collections, true);
				Application.DoEvents();
				AssertInstalledSections(collections);
				SetContext(collections, false);
				Application.DoEvents();
				Assert.That(GetField<Control>(collections, "_installedGroup").Visible, Is.False);
				Assert.That(GetField<Control>(collections, "_detailsSection").Visible, Is.False);
			});
		}

		/// <summary>Creates documents before showing the main window, as during game-mode startup.</summary>
		private static void WithHiddenCollection(int width, int height, Action<Form, TabbedView, Form, CollectionsPreviewControl> action)
		{
			using (Form host = new Form { IsMdiContainer = true, ShowInTaskbar = false, Opacity = 0.01,
				StartPosition = FormStartPosition.Manual, Location = SystemInformation.WorkingArea.Location,
				ClientSize = new Size(width, height) })
			using (TabbedView tabs = new TabbedView())
			using (DocumentManager manager = new DocumentManager { MdiParent = host, View = tabs })
			using (CollectionsPreviewControl collections = new CollectionsPreviewControl { MdiParent = host })
			using (Form mods = new Form { MdiParent = host, Text = "Mods" })
			{
				collections.Show();
				mods.Show();
				host.Show();
				Application.DoEvents();
				tabs.ActivateDocument(mods);
				Application.DoEvents();
				Assert.That(tabs.ActiveDocument.Control, Is.SameAs(mods), "Preparation must occur while the Mods document is selected.");
				action(host, tabs, mods, collections);
			}
		}

		/// <summary>Changes only presentation identity, without opening or modifying real user stores.</summary>
		private static void SetContext(CollectionsPreviewControl collections, bool installed)
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), "803619", 104);
			CollectionUiContext context = installed ? CollectionUiContext.Installed(0, revision, Guid.NewGuid()) : CollectionUiContext.None(0);
			typeof(CollectionsPreviewControl).GetField("_displayContext", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(collections, context);
			typeof(CollectionsPreviewControl).GetMethod("UpdateEmptyCollectionView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(collections, null);
		}

		/// <summary>Checks actual screen bounds as well as visibility, so zero-height rows cannot pass.</summary>
		private static void AssertInstalledSections(CollectionsPreviewControl collections)
		{
			TableLayoutPanel layout = GetField<TableLayoutPanel>(collections, "_documentLayout");
			Control installed = GetField<Control>(collections, "_installedGroup");
			Control selector = GetField<Control>(collections, "_managedAssociationCombo");
			Control details = GetField<Control>(collections, "_detailsSection");
			Assert.That(installed.Visible && selector.Visible && details.Visible, Is.True);
			Assert.That(installed.Height, Is.GreaterThan(selector.Height));
			Assert.That(details.Height, Is.GreaterThan(100));
			Assert.That(installed.RectangleToScreen(installed.ClientRectangle).Contains(selector.RectangleToScreen(selector.ClientRectangle)), Is.True);
			Assert.That(details.Top, Is.GreaterThanOrEqualTo(installed.Bottom));
			Assert.That(details.Bottom, Is.LessThanOrEqualTo(layout.ClientSize.Height - layout.Padding.Bottom));
		}

		/// <summary>Reads a private layout section for bounds assertions without exposing production APIs.</summary>
		private static T GetField<T>(CollectionsPreviewControl collections, string name)
		{
			return (T)typeof(CollectionsPreviewControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(collections);
		}
	}
}
