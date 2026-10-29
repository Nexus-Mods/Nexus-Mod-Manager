using System;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Runs profile-native order publication after file deployment, with existing profile-switch compensation on failure.</summary>
	internal sealed class NativeOrderProfileRestoreTask : ThreadedBackgroundTask
	{
		private readonly NativeLoadOrderService _service;
		private readonly byte[] _state;
		private CancellationTokenSource _cancellation = new CancellationTokenSource();
		private ConfirmActionMethod _confirmation;

		/// <summary>Captures a detached target policy without starting a package scan on the UI thread.</summary>
		internal NativeOrderProfileRestoreTask(NativeLoadOrderService service, byte[] state) { _service = service; _state = (byte[])state.Clone(); }

		/// <summary>Starts through the same background task mechanism as native profile activation.</summary>
		internal void Update(ConfirmActionMethod confirmation) { _confirmation = confirmation; Status = TaskStatus.Running; Start(confirmation); }

		/// <summary>Propagates cancellation to package inspection and publication preflight.</summary>
		public override void Cancel() { _cancellation.Cancel(); base.Cancel(); }

		/// <summary>Resumes using a fresh cooperative cancellation token.</summary>
		public override void Resume() { _cancellation.Dispose(); _cancellation = new CancellationTokenSource(); Status = TaskStatus.Running; Start(_confirmation); }

		/// <summary>Reports a failed publication to the existing profile rollback workflow.</summary>
		protected override object DoWork(object[] args, out string message)
		{
			message = null;
			try { _service.RestoreProfileState(_state, _cancellation.Token); return true; }
			catch (OperationCanceledException) { Status = TaskStatus.Cancelled; return null; }
			catch (Exception error)
			{
				Status = TaskStatus.Error;
				message = LanguageManager.Format("Profiles.NativeOrder.RestoreFailed", "The profile's game load order could not be restored: {0}", error.Message);
				return error;
			}
		}
	}
}
