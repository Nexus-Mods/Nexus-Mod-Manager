namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes a requested value change in a game settings file.
	/// </summary>
	public sealed class EditIniOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the name of the settings file to edit.
		/// </summary>
		public string SettingsFileName { get; private set; }

		/// <summary>
		/// Gets the section containing the setting to edit.
		/// </summary>
		public string Section { get; private set; }

		/// <summary>
		/// Gets the key of the setting to edit.
		/// </summary>
		public string Key { get; private set; }

		/// <summary>
		/// Gets the value to assign to the setting.
		/// </summary>
		public string Value { get; private set; }

		/// <summary>
		/// Gets whether overwrite-decision processing was completed before the operation was queued.
		/// </summary>
		public bool HasResolvedOverwriteDecision { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new INI-edit operation.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file to edit.</param>
		/// <param name="p_strSection">The section containing the setting to edit.</param>
		/// <param name="p_strKey">The key of the setting to edit.</param>
		/// <param name="p_strValue">The value to assign to the setting.</param>
		public EditIniOperation(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			SettingsFileName = p_strSettingsFileName;
			Section = p_strSection;
			Key = p_strKey;
			Value = p_strValue;
		}

		/// <summary>
		/// Initializes an INI-edit operation whose overwrite decision has already been resolved.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file to edit.</param>
		/// <param name="p_strSection">The section containing the setting to edit.</param>
		/// <param name="p_strKey">The key of the setting to edit.</param>
		/// <param name="p_strValue">The value to assign to the setting.</param>
		/// <param name="p_booDecisionResolved">Whether overwrite-decision processing has already completed.</param>
		public EditIniOperation(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue, bool p_booDecisionResolved)
			: this(p_strSettingsFileName, p_strSection, p_strKey, p_strValue)
		{
			HasResolvedOverwriteDecision = p_booDecisionResolved;
		}

		#endregion
	}
}
