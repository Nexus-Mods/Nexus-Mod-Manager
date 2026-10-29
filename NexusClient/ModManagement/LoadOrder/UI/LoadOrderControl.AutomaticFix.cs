using System;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.XtraEditors;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.ModManagement.LoadOrder.UI
{
	/// <summary>Default-off consent and completed-operation scheduling for safe module-order repairs.</summary>
	public sealed partial class LoadOrderControl
	{
		private readonly CheckEdit _automaticFix;
		private SynchronizationContext _automaticFixContext;
		private bool _automaticFixPending;
		private volatile bool _automaticFixEnabled;
		private int _automaticFixConsent;
		private string _automaticFixProfile;
		private string AutomaticFixKey => "loadOrder.AutomaticOrderRepair." + _service.ModeId;

		/// <summary>Loads a default-off per-game option without running diagnosis or changing the current order.</summary>
		private void InitializeAutomaticFix()
		{
			_automaticFixContext = SynchronizationContext.Current ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();
			_binding = true;
			try
			{
				_automaticFix.Checked = _main.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(AutomaticFixKey) &&
					Boolean.TryParse(_main.EnvironmentInfo.Settings.DockPanelLayouts[AutomaticFixKey], out bool enabled) && enabled;
			}
			finally { _binding = false; }
			_automaticFixEnabled = _automaticFix.Checked;
			_changes.OperationCompleted += NativeOperationCompleted;
		}

		/// <summary>Persists consent for future completed mod operations using the host's existing settings-save lifecycle.</summary>
		private void AutomaticFixSettingChanged(object sender, EventArgs args)
		{
			if (_binding || _service == null) return;
			_main.EnvironmentInfo.Settings.DockPanelLayouts[AutomaticFixKey] = _automaticFix.Checked ? "True" : "False";
			_automaticFixEnabled = _automaticFix.Checked;
			CancelPendingAutomaticFix();
			_status.Text = _automaticFix.Checked ? T("AutomaticFixEnabled", "Automatic module-order fixes are enabled for future mod installs/removals. The current order has not changed.")
				: T("AutomaticFixDisabled", "Automatic module-order fixes are off. Existing module positions are preserved when mods change.");
		}

		/// <summary>Only a successful, committed ordinary mod operation is eligible; scans, profiles and Collections cannot trigger this option.</summary>
		internal static bool IsAutomaticFixTrigger(bool success, ModOperationResult result) => success && result != null &&
			result.Identity.Origin == ModOperationOrigin.Manual && result.ReportedStatus == ModOperationReportedStatus.Succeeded &&
			result.Durability == ModOperationDurability.VerifiedCommitted;

		/// <summary>Marshals a completion hint even when this page has never created a native window handle.</summary>
		private void NativeOperationCompleted(object sender, TaskSetCompletedEventArgs args)
		{
			var operation = sender as ModInstallerBase;
			int consent = Volatile.Read(ref _automaticFixConsent);
			string profile = Volatile.Read(ref _selectedProfile);
			bool eligible = _automaticFixEnabled && IsAutomaticFixTrigger(args.Success, operation?.OperationResult);
			if (_released) return;
			_automaticFixContext.Post(async state =>
			{
				if (_released || IsDisposed) return;
				if (consent != Volatile.Read(ref _automaticFixConsent) || profile != _selectedProfile) return;
				if (!eligible || !_automaticFix.Checked || _main.IsSwitching) { _automaticFixPending = false; return; }
				_automaticFixProfile = profile; _automaticFixPending = true;
				await TryAutomaticFixAsync();
			}, null);
		}

		/// <summary>Invalidates queued completion hints when the user edits, changes profiles or changes automatic-fix consent.</summary>
		private void CancelPendingAutomaticFix()
		{
			Interlocked.Increment(ref _automaticFixConsent); _automaticFixPending = false;
		}

		/// <summary>Coalesces native batches, protects drafts and applies order-only repairs through fresh reviewed publication.</summary>
		private async Task TryAutomaticFixAsync()
		{
			if (!_automaticFixPending || _released) return;
			if (!_automaticFix.Checked || _main.IsSwitching || _automaticFixProfile == null || _automaticFixProfile != _selectedProfile)
			{ _automaticFixPending = false; return; }
			if (_session?.IsDirty == true || _dragToken != null)
			{
				_automaticFixPending = false;
				_status.Text = T("AutomaticFixDraft", "Automatic order repair was skipped to preserve your pending edits. Save or discard them, then use Fix automatically."); return;
			}
			if (_busy || _changes.HasRunningNativeTask || _main.ModActivationMonitor.HasPendingNativeOperations) return;
			_automaticFixPending = false;
			string profile = _selectedProfile;
			await WorkAsync(T("AutomaticFixRunning", "Checking module order after mod changes…"), async token =>
			{
				await ReadCoreAsync(token);
				if (_selectedProfile != profile || _session == null || !_session.Analysis.HasAutomaticRepairs)
				{
					_status.Text = T("AutomaticFixChecked", "Module order checked. No safe automatic order repairs were applied; review any remaining problems."); return;
				}
				NativeLoadOrderRepairReview review = await Task.Run(() => _service.ReviewRepairs(profile, _session.Analysis, token), token);
				if (!_automaticFix.Checked || _main.IsSwitching || _main.ModActivationMonitor.HasPendingNativeOperations)
					throw new InvalidOperationException(T("AutomaticFixBusy", "Mod activity changed before automatic repair. Review the current Load Order before applying fixes."));
				NativeLoadOrderRecovery result = await Task.Run(() => _service.ApplyRepairs(review, true, token), token);
				await RefreshAfterWriteAsync(token, result.State == NativeLoadOrderRecoveryState.None
					? T("AutomaticFixApplied", "Automatic module-order fixes applied after mod changes. Review any remaining problems.")
					: T("RecoveryNeeded", "An interrupted save needs recovery. Open Recovery before editing this profile."));
			}, false);
		}
	}
}
