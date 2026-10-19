namespace Nexus.Client.Games.UI
{
	using System;

	/// <summary>A user-selected game action, not an implicit discovery side effect.</summary>
	internal sealed class GameDiscoveryGameRequestedEventArgs : EventArgs
	{
		internal GameDiscoveryGameRequestedEventArgs(string modeId, string candidatePath = null)
		{
			ModeId = modeId ?? throw new ArgumentNullException(nameof(modeId));
			CandidatePath = candidatePath;
		}

		internal string ModeId { get; }
		internal string CandidatePath { get; }
	}
}
