namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Optional native adapter used by Local Collection restore to observe and replace one supported game-specific binary value
	/// without modifying InstallLog ownership itself.
	/// </summary>
	/// <remarks>
	/// Implementations are intentionally scoped to game modes that can prove exact read/write semantics for their native value.
	/// Returning <c>false</c> means the key cannot be represented safely by the adapter and restore must fail closed.
	/// </remarks>
	public interface IGameSpecificValueRestoreSupport
	{
		/// <summary>Attempts to read the exact current physical bytes for one native game-specific key.</summary>
		bool TryReadGameSpecificValue(string p_strKey, out byte[] p_bteValue);

		/// <summary>Attempts to replace the exact current physical bytes for one native game-specific key.</summary>
		bool TryRestoreGameSpecificValue(string p_strKey, byte[] p_bteValue);
	}
}
