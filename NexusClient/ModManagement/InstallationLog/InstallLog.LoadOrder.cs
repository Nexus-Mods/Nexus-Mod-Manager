using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.InstallationLog
{
	public partial class InstallLog : ICurrentGameSpecificValueReader, IOwnedGameSpecificValueReader
	{
		/// <summary>Returns a detached current value, including buffered native transaction changes.</summary>
		public bool TryReadCurrentGameSpecificValue(string key, out byte[] value) => GetEnlistment().TryReadCurrentGameSpecificValue(key, out value);

		/// <summary>Reads a specific owner's buffered or committed value without requiring a committed-state snapshot.</summary>
		public bool TryReadOwnedGameSpecificValue(IMod owner, string key, out byte[] value) => GetEnlistment().TryReadOwnedGameSpecificValue(owner, key, out value);
	}
}
