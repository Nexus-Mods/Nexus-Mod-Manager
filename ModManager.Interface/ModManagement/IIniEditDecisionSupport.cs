namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Exposes INI overwrite decisions separately from the mutation that applies an approved edit.
	/// </summary>
	public interface IIniEditDecisionSupport
	{
		/// <summary>
		/// Resolves whether the specified INI edit is allowed using the current overwrite-decision state.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file to edit.</param>
		/// <param name="p_strSection">The section containing the setting to edit.</param>
		/// <param name="p_strKey">The key of the setting to edit.</param>
		/// <param name="p_strValue">The value that would be assigned to the setting.</param>
		/// <param name="p_strCurrentValue">The value visible to the caller before the edit is applied.</param>
		/// <returns><c>true</c> if the edit is approved; otherwise, <c>false</c>.</returns>
		bool ResolveIniEdit(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue, string p_strCurrentValue);

		/// <summary>
		/// Applies an INI edit that has already passed overwrite-decision processing.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file to edit.</param>
		/// <param name="p_strSection">The section containing the setting to edit.</param>
		/// <param name="p_strKey">The key of the setting to edit.</param>
		/// <param name="p_strValue">The value to assign to the setting.</param>
		/// <returns><c>true</c> when the edit is applied.</returns>
		bool ApplyResolvedIniEdit(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue);
	}
}
