namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes a requested change to a game-specific installation value.
	/// </summary>
	public sealed class EditGameSpecificValueOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the game-specific value key to edit.
		/// </summary>
		public string Key { get; private set; }

		/// <summary>
		/// Gets the value to install for the specified key.
		/// </summary>
		/// <remarks>The operation retains the supplied buffer for efficient deferred execution.</remarks>
		public byte[] Value { get; private set; }

		/// <summary>
		/// Gets whether overwrite-decision processing was completed before the operation was queued.
		/// </summary>
		public bool HasResolvedOverwriteDecision { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new game-specific value operation.
		/// </summary>
		/// <param name="p_strKey">The game-specific value key to edit.</param>
		/// <param name="p_bteValue">The value to install.</param>
		public EditGameSpecificValueOperation(string p_strKey, byte[] p_bteValue)
		{
			Key = p_strKey;
			Value = p_bteValue;
		}

		/// <summary>
		/// Initializes a game-specific value operation whose overwrite decision has already been resolved.
		/// </summary>
		/// <param name="p_strKey">The game-specific value key to edit.</param>
		/// <param name="p_bteValue">The value to install.</param>
		/// <param name="p_booDecisionResolved">Whether overwrite-decision processing has already completed.</param>
		public EditGameSpecificValueOperation(string p_strKey, byte[] p_bteValue, bool p_booDecisionResolved)
			: this(p_strKey, p_bteValue)
		{
			HasResolvedOverwriteDecision = p_booDecisionResolved;
		}

		#endregion
	}
}
