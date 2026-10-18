using System.Collections.Generic;

namespace Nexus.Client.Games.ManagedContent
{
	/// <summary>Optional read-only component identity rules; native file histories remain the sole ownership authority.</summary>
	public interface IManagedContentComponentProvider
	{
		/// <summary>Immutable target-side folder rules shared with the mode's configured install layout.</summary>
		IReadOnlyList<ManagedContentLayoutRule> ComponentRules { get; }
	}
}
