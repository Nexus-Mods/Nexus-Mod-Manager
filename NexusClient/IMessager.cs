using System;

namespace Nexus.Client
{
	/// <summary>
	/// The methods provided for communication bewtween multiple instances of the mod manager.
	/// </summary>
	public interface IMessager : IDisposable
	{
		#region Mod Addition

		/// <summary>
		/// Adds the specified mod to the mod manager.
		/// </summary>
		/// <param name="p_strFilePath">The path or URL of the mod to add to the mod manager.</param>
		void AddMod(string p_strFilePath);

		#endregion

		/// <summary>
		/// Brings the currently running client to the front.
		/// </summary>
		void BringToFront();

		/// <summary>Used as a simple Power On Self Test method.</summary>
		/// <remarks>
		/// This method can be called to ensure a Messager is alive.
		/// </remarks>
		void Post();
	}
}
