using System.Collections.Generic;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Optional game-mode contract for reviewing an ENTIRE basic archive selection before the
	/// native Direct/Virtual installer receives any file. The returned destinations are logical,
	/// relative to the mode's ordinary installation root; native ownership is never replaced.
	/// A mode implementing this contract must reject ambiguous selections, not omit files silently.
	/// Scripted installs and explicit collection recipes require their own reviewed contracts.
	/// </summary>
	public interface IReviewedBasicInstallMappingProvider
	{
		IReadOnlyList<KeyValuePair<string, string>> MapBasicInstallFiles(IMod mod,
			ModInstallContext installContext, IEnumerable<KeyValuePair<string, string>> selectedFiles);
	}
}
