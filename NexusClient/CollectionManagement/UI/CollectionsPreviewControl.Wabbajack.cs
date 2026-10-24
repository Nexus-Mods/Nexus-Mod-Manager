using System;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.Wabbajack;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Handoff from reconstructed Wabbajack profiles to the ordinary Collection workflow.</summary>
	public sealed partial class CollectionsPreviewControl
	{
		private CollectionRevisionIdentity _preparedWabbajackRevision;
		private bool _preparedWabbajackSetupAvailable;

		/// <summary>Resolves persisted Wabbajack provenance once when the displayed revision changes.</summary>
		private bool HasWabbajackSetup(CollectionRevisionIdentity revision)
		{
			if (!revision.Equals(_preparedWabbajackRevision))
			{
				_preparedWabbajackSetupAvailable = _workflow.SetupPackages.IsWabbajackRevision(revision);
				_preparedWabbajackRevision = revision;
			}
			return _preparedWabbajackSetupAvailable;
		}

		/// <summary>Retains a converted bundle and presents its existing installation/recovery controls.</summary>
		internal async Task OpenPreparedWabbajackAsync(WabbajackConvertedCollection collection, string settingsDirectory)
		{
			if (_workflow == null || _workflowBusy) throw new InvalidOperationException(L("Wabbajack.CollectionBusy", "Finish the current Collection operation before opening the prepared Wabbajack profile."));
			CancelPreviewWork(false);
			int generation = ++_previewGeneration;
			NexusCollectionPreviewSnapshot preview = new NexusCollectionPreviewSnapshot(null, null, null, collection.Definition, collection.Revision, null, null, null, null);
			ResetWorkflowViewState();
			_managedAssociationPresentation = null;
			CollectionUiContext context = CollectionUiContext.Incoming(generation, collection.Revision.Identity, null);
			_displayContext = context;
			_snapshot = preview;
			RenderSnapshot(preview);
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Importing,
				L("Collections.Workflow.Importing", "Importing and retaining the Collection source..."));
			PreviewActivated(this, EventArgs.Empty);
			try
			{
				NexusCollectionPreviewSnapshot retained = await Task.Run(() =>
				{
					NexusCollectionPreviewSnapshot imported = _workflow.ImportAndRetainLocalBundle(preview, collection.BundlePath, token);
					_workflow.SetupPackages.RetainWabbajackSetup(collection.Revision.Identity, collection.SetupPath);
					return imported;
				}, token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				_snapshot = retained;
				_preparedWabbajackRevision = collection.Revision.Identity;
				_preparedWabbajackSetupAvailable = true;
				RenderSnapshot(retained); BindMatchingRecovery();
			}
			finally { EndWorkflowWork(context); }
		}
	}
}
