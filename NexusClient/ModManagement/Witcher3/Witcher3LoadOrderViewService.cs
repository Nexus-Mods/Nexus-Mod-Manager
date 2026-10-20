using System;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>
	/// One disposable read observation for the dedicated Witcher page. This is neither a
	/// native ownership snapshot with a mutation lease nor game-effective 5.x order evidence.
	/// </summary>
	public sealed class Witcher3LoadOrderViewSnapshot
	{
		internal Witcher3LoadOrderViewSnapshot(string physicalGameRoot,
			Witcher3ModSettingsLifecycleSnapshot lifecycle,
			Witcher3LoadOrderProjection projection, Witcher3LoadOrderEditorSession editor,
			Witcher3RemasteredHealthObservation remasteredHealth,
			Witcher3RemasteredComponentHealthReport remasteredComponents, Witcher3ComponentDiskObservation disk)
		{
			PhysicalGameRoot = physicalGameRoot;
			Lifecycle = lifecycle;
			Projection = projection;
			Editor = editor;
			RemasteredHealth = remasteredHealth;
			RemasteredComponents = remasteredComponents;
			Disk = disk;
		}

		/// <summary>Exact game root associated with this read observation; never reuse across game switches.</summary>
		public string PhysicalGameRoot { get; }
		public Witcher3ModSettingsLifecycleSnapshot Lifecycle { get; }
		public Witcher3LoadOrderProjection Projection { get; }
		public Witcher3LoadOrderEditorSession Editor { get; }
		/// <summary>Exact disk observation used for this view and any missing-entry cleanup preview.</summary>
		public Witcher3ComponentDiskObservation Disk { get; }
		/// <summary>Only populated for the Remastered game mode. Never authorizes game writes.</summary>
		public Witcher3RemasteredHealthObservation RemasteredHealth { get; }
		/// <summary>Pure, read-only diagnostics derived from this snapshot's native component index.</summary>
		public Witcher3RemasteredComponentHealthReport RemasteredComponents { get; }
	}

	/// <summary>
	/// W3R.6: captures fresh native services (never an InstallLog retained across a reload),
	/// obtains a bounded disk inventory, and joins them to the external game-order file.
	/// The caller schedules Read outside WinForms painting and serializes overlapping refreshes.
	/// </summary>
	public sealed class Witcher3LoadOrderViewService
	{
		private readonly Witcher3ModSettingsLifecycleService _lifecycle;
		private readonly Func<string> _gameRoot;
		private readonly Func<NativeStateCaptureSnapshot> _nativeCapture;
		private readonly Func<string, Witcher3ComponentDiskObservation> _diskScan;

		public Witcher3LoadOrderViewService(Witcher3ModSettingsLifecycleService lifecycle,
			Func<string> gameRoot, Func<NativeStateCaptureSnapshot> nativeCapture,
			Func<string, Witcher3ComponentDiskObservation> diskScan)
		{
			_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
			_gameRoot = gameRoot ?? throw new ArgumentNullException(nameof(gameRoot));
			_nativeCapture = nativeCapture ?? throw new ArgumentNullException(nameof(nativeCapture));
			_diskScan = diskScan ?? throw new ArgumentNullException(nameof(diskScan));
		}

		public Witcher3LoadOrderViewSnapshot Read()
		{
			Witcher3ModSettingsLifecycleSnapshot state = _lifecycle.Refresh();
			string physicalRoot = _gameRoot();
			NativeStateCaptureSnapshot native = _nativeCapture();
			Witcher3ComponentDiskObservation disk = _diskScan(physicalRoot);
			Witcher3NativeComponentIndex index = Witcher3NativeComponentIndexer.Build(native, physicalRoot, disk);
			Witcher3LoadOrderProjection projection = Witcher3LoadOrderProjector.Project(index,
				state.Settings, state.Edition);
			var editor = new Witcher3LoadOrderEditorSession(state.ModeId, state.Edition,
				state.HasPendingPublication, state.Settings, projection);
			// Health checks are observational and isolated from the Legacy load-order writer.
			// Do not mistake enabled DX12 switches for proof of effective 5.x ordering.
			Witcher3RemasteredHealthObservation health =
				String.Equals(state.ModeId, "Witcher3Remastered", StringComparison.OrdinalIgnoreCase)
					? Witcher3RemasteredHealthReader.Read(physicalRoot, _lifecycle.DocumentsDirectory, state.Edition)
					: null;
			Witcher3RemasteredComponentHealthReport components = health == null ? null
				: Witcher3RemasteredComponentHealthInspector.Inspect(index);
			return new Witcher3LoadOrderViewSnapshot(physicalRoot, state, projection, editor,
				health, components, disk);
		}
	}
}
