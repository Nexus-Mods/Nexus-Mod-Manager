using Nexus.Client.ModManagement.LoadOrder;

namespace Nexus.Client.ModManagement.InstallationLog
{
	public partial class InstallLog : ICurrentGameSpecificValueReader
	{
		/// <summary>Returns a detached current value, including buffered native transaction changes.</summary>
		public bool TryReadCurrentGameSpecificValue(string key, out byte[] value) => GetEnlistment().TryReadCurrentGameSpecificValue(key, out value);
	}
}
