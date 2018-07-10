using System;
using Nexus.Client.ModManagement.Scripting.XmlScript;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript
{
	/// <summary>
	/// A condition that requires a minimum version of FOSE to be installed.
	/// </summary>
	public class FoseCondition : VersionCondition
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_verVersion">The minimum required version of fose.</param>
		public FoseCondition(Version p_verVersion)
			: base(p_verVersion)
		{
		}

		#endregion

		/// <summary>
		/// Gets whether or not the condition is fulfilled.
		/// </summary>
		/// <remarks>
		/// The dependency is fulfilled if the specified minimum version of
		/// FOSE is installed.
		/// </remarks>
		/// <param name="p_csmStateManager">The manager that tracks the currect install state.</param>
		/// <returns><c>true</c> if the condition is fulfilled;
		/// <c>false</c> otherwise.</returns>
		/// <seealso cref="ICondition.GetIsFulfilled(ConditionStateManager)"/>
		public override bool GetIsFulfilled(ConditionStateManager p_csmStateManager)
		{
			Version verInstalledVersion = ((Fallout3GameMode)p_csmStateManager.GameMode).ScriptExtenderVersion;
			return ((verInstalledVersion != null) && (verInstalledVersion >= MinimumVersion));
		}

		/// <summary>
		/// Gets a message describing whether or not the condition is fulfilled.
		/// </summary>
		/// If the dependency is fulfilled the message is "Passed." If the dependency is not fulfilled the
		/// message informs the user of the installed version.
		/// <param name="p_csmStateManager">The manager that tracks the currect install state.</param>
		/// <returns>A message describing whether or not the condition is fulfilled.</returns>
		/// <seealso cref="ICondition.GetMessage(ConditionStateManager)"/>
		public override string GetMessage(ConditionStateManager p_csmStateManager)
		{
			Version verInstalledVersion = ((Fallout3GameMode)p_csmStateManager.GameMode).ScriptExtenderVersion;
			if (verInstalledVersion == null)
				return String.Format("This mod requires FOSE v{0} or higher. Please download from http://fose.silverlock.org", MinimumVersion);
			else if (verInstalledVersion < MinimumVersion)
				return String.Format("This mod requires FOSE v{0} or higher. You have {1}. Please update from http://fose.silverlock.org", MinimumVersion, verInstalledVersion);
			else
				return "Passed";
		}
	}
}
