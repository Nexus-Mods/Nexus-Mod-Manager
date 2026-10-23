namespace Nexus.Client.Games
{
	using System;

	/// <summary>
	/// First-session admission policy, independent of WinForms and native game services.
	/// It is deliberately not a second source of configuration truth: the catalog
	/// decides membership; the initializer still performs authoritative validation.
	/// </summary>
	public sealed class GameSessionEntryGate
	{
		public enum EntryState { NoGame, Loading, Ready, Failed }

		public EntryState State { get; private set; } = EntryState.NoGame;
		public string ModeId { get; private set; }

		public static bool CanManage(GameDiscoveryEntry entry)
		{
			return entry != null && entry.Collection == GameDiscoveryCollection.Managed &&
				!entry.NeedsAttention && !entry.IsInstallationMissing;
		}

		/// <summary>Never start a second initializer while another entry is in flight or ready.</summary>
		public bool TryBegin(GameDiscoveryEntry entry)
		{
			if (!CanManage(entry) || (State != EntryState.NoGame && State != EntryState.Failed))
				return false;
			ModeId = entry.ModeId;
			State = EntryState.Loading;
			return true;
		}

		/// <summary>Called only after all old UI controls, IPC and native services have been released.
		/// Keeps the current game registered until teardown has actually completed.</summary>
		public void EndSession(string modeId)
		{
			if (State != EntryState.Ready || !String.Equals(ModeId, modeId, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The session being released is not the current game.");
			State = EntryState.NoGame;
			ModeId = null;
		}

		public void Finish(string modeId, bool succeeded)
		{
			if (State != EntryState.Loading || !String.Equals(ModeId, modeId, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The game-session completion does not match the current load.");
			State = succeeded ? EntryState.Ready : EntryState.Failed;
			if (!succeeded)
				ModeId = null;
		}
	}
}
