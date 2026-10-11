using System.IO;

namespace Nexus.Client.Mods
{
	/// <summary>Optional archive source that writes an entry directly to a caller-owned stream without temporary extraction.</summary>
	public interface IModFileStreamSource
	{
		/// <summary>Copies the exact logical file bytes to a writable stream without closing that stream.</summary>
		void CopyFileTo(string file, Stream destination);
	}
}
