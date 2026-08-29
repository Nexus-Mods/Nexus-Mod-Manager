namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.IO;

	using Nexus.Client.Mods;

	/// <summary>
	/// Tracks mods added during the current Mod Manager session independently
	/// of any particular grid/tree presentation surface.
	/// </summary>
	internal sealed class ModSessionNewTracker : IDisposable
	{
		private readonly HashSet<IMod> _sessionNewMods = new HashSet<IMod>();
		private readonly HashSet<IMod> _filterSnapshot = new HashSet<IMod>();
		private readonly HashSet<string> _knownSessionModFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Occurs when metadata changes for a mod currently marked as new.
		/// </summary>
		public event EventHandler TrackedModChanged;

		/// <summary>
		/// Determines whether a mod was added after the current session baseline was established.
		/// </summary>
		public bool IsNew(IMod mod)
		{
			return mod != null && _sessionNewMods.Contains(mod);
		}

		/// <summary>
		/// Determines whether a mod belongs to the stable snapshot used by the New Mods category filter.
		/// </summary>
		public bool IsInFilterSnapshot(IMod mod)
		{
			return mod != null && _filterSnapshot.Contains(mod);
		}

		/// <summary>
		/// Resets tracking and records the supplied mods as already known for the current session.
		/// </summary>
		public void ResetBaseline(IEnumerable<IMod> mods)
		{
			ClearTrackedMods();
			_filterSnapshot.Clear();
			_knownSessionModFiles.Clear();

			if (mods == null)
				return;

			foreach (IMod mod in mods)
				RegisterKnownMod(mod);
		}

		/// <summary>
		/// Registers an added mod as new when its archive was not already known in the current session.
		/// </summary>
		public bool TrackAddedMod(IMod mod, bool addToFilterSnapshot)
		{
			// Baseline mods and archives already observed earlier in the session are not
			// reclassified as new if a collection refresh removes and re-adds them.
			if (!RegisterKnownMod(mod))
				return false;

			if (_sessionNewMods.Add(mod))
				mod.PropertyChanged += TrackedMod_PropertyChanged;

			if (addToFilterSnapshot)
				_filterSnapshot.Add(mod);

			return true;
		}

		/// <summary>
		/// Removes a mod from active new-mod tracking and optionally forgets its session identity.
		/// </summary>
		public void RemoveMod(IMod mod, bool removeKnownKey)
		{
			if (mod == null)
				return;

			if (_sessionNewMods.Remove(mod))
				mod.PropertyChanged -= TrackedMod_PropertyChanged;

			_filterSnapshot.Remove(mod);

			if (removeKnownKey)
			{
				string key = GetSessionModKey(mod);
				if (!String.IsNullOrEmpty(key))
					_knownSessionModFiles.Remove(key);
			}
		}

		/// <summary>
		/// Marks the supplied mods as seen and returns whether the tracked set changed.
		/// </summary>
		public bool Acknowledge(IEnumerable<IMod> mods)
		{
			if (mods == null)
				return false;

			bool changed = false;
			foreach (IMod mod in mods)
			{
				if (mod == null || !_sessionNewMods.Remove(mod))
					continue;

				mod.PropertyChanged -= TrackedMod_PropertyChanged;
				changed = true;
			}

			return changed;
		}

		/// <summary>
		/// Captures or clears the stable set used while the New Mods category filter is enabled.
		/// </summary>
		public void CaptureFilterSnapshot(bool enabled)
		{
			_filterSnapshot.Clear();
			if (!enabled)
				return;

			// The filter deliberately works from a snapshot. Acknowledging a row must not
			// make it disappear while the user is still reviewing the filtered result set.
			foreach (IMod mod in _sessionNewMods)
				_filterSnapshot.Add(mod);
		}

		/// <summary>
		/// Releases property-change subscriptions and clears all session tracking state.
		/// </summary>
		public void Dispose()
		{
			ClearTrackedMods();
			_filterSnapshot.Clear();
			_knownSessionModFiles.Clear();
		}

		/// <summary>
		/// Registers a mod archive identity as known and returns true only for the first registration in the session.
		/// </summary>
		public bool RegisterKnownMod(IMod mod)
		{
			if (mod == null)
				return false;

			string key = GetSessionModKey(mod);
			return !String.IsNullOrEmpty(key) && _knownSessionModFiles.Add(key);
		}

		/// <summary>
		/// Builds the normalized archive identity used to avoid treating the same mod file as newly added more than once.
		/// </summary>
		private static string GetSessionModKey(IMod mod)
		{
			if (mod == null || String.IsNullOrWhiteSpace(mod.Filename))
				return String.Empty;

			try
			{
				return Path.GetFullPath(mod.Filename);
			}
			catch
			{
				return mod.Filename.Trim();
			}
		}

		/// <summary>
		/// Removes property-change subscriptions from all currently tracked new mods.
		/// </summary>
		private void ClearTrackedMods()
		{
			foreach (IMod mod in _sessionNewMods)
			{
				if (mod != null)
					mod.PropertyChanged -= TrackedMod_PropertyChanged;
			}
			_sessionNewMods.Clear();
		}

		/// <summary>
		/// Forwards metadata changes for tracked mods so presentation surfaces can repaint their new-mod state.
		/// </summary>
		private void TrackedMod_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			TrackedModChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
