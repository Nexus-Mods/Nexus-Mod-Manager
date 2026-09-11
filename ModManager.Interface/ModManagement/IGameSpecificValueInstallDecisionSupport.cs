namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Exposes game-specific overwrite decisions separately from approved value mutations.
	/// </summary>
	public interface IGameSpecificValueInstallDecisionSupport
	{
		/// <summary>
		/// Resolves whether the specified game-specific value may be edited.
		/// </summary>
		/// <param name="p_strKey">The key identifying the game-specific value.</param>
		/// <returns><c>true</c> if the edit is approved; otherwise, <c>false</c>.</returns>
		bool ResolveGameSpecificValueEdit(string p_strKey);

		/// <summary>
		/// Applies a game-specific value edit that has already passed overwrite-decision processing.
		/// </summary>
		/// <param name="p_strKey">The key identifying the game-specific value.</param>
		/// <param name="p_bteValue">The value to apply.</param>
		/// <returns><c>true</c> when the edit is applied.</returns>
		bool ApplyResolvedGameSpecificValueEdit(string p_strKey, byte[] p_bteValue);
	}
}
