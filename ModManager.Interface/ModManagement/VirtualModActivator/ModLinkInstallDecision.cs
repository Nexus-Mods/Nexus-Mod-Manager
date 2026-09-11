namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Represents a user-visible virtual-link overwrite choice resolved before link deployment.
	/// </summary>
	public sealed class ModLinkInstallDecision
	{
		#region Properties

		/// <summary>
		/// Gets whether a user-visible overwrite choice was resolved.
		/// </summary>
		public bool IsResolved { get; private set; }

		/// <summary>
		/// Gets whether the incoming file should replace the conflicting file when the resolved choice is required.
		/// </summary>
		public bool Overwrite { get; private set; }

		/// <summary>
		/// Gets whether the planning pass captured the effective link outcome.
		/// </summary>
		public bool HasLinkOutcome { get; private set; }

		/// <summary>
		/// Gets the planned link outcome: <c>true</c> for active, <c>false</c> for inactive, and <c>null</c> for no link change.
		/// </summary>
		public bool? LinkOutcome { get; private set; }

		/// <summary>
		/// Gets whether the planned link operation is expected to create or replace the active link.
		/// </summary>
		public bool CreatesActiveLink { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a decision indicating that no user-visible overwrite choice was required during planning.
		/// </summary>
		public ModLinkInstallDecision()
		{
		}

		/// <summary>
		/// Initializes a resolved overwrite choice.
		/// </summary>
		/// <param name="p_booOverwrite">Whether the incoming file should replace the conflicting file.</param>
		public ModLinkInstallDecision(bool p_booOverwrite)
		{
			IsResolved = true;
			Overwrite = p_booOverwrite;
		}

		#endregion

		#region Outcome Projection

		/// <summary>
		/// Creates a copy of the decision containing the effective link outcome observed during planning.
		/// </summary>
		/// <param name="p_booLinkOutcome"><c>true</c> to expose the incoming file as active, <c>false</c> to keep it inactive, or <c>null</c> when no link change is required.</param>
		/// <param name="p_booCreatesActiveLink">Whether execution is expected to create or replace the active link.</param>
		/// <returns>A decision containing both the user-visible overwrite choice and the planned link outcome.</returns>
		public ModLinkInstallDecision WithLinkOutcome(bool? p_booLinkOutcome, bool p_booCreatesActiveLink)
		{
			return new ModLinkInstallDecision
			{
				IsResolved = IsResolved,
				Overwrite = Overwrite,
				HasLinkOutcome = true,
				LinkOutcome = p_booLinkOutcome,
				CreatesActiveLink = p_booCreatesActiveLink
			};
		}

		#endregion
	}
}
