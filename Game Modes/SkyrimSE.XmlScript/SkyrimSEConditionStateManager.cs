﻿using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.Games.SkyrimSE.Scripting.XmlScript
{
	/// <summary>
	/// This class manages the state of the installation.
	/// </summary>
	public class SkyrimSEConditionStateManager : ConditionStateManager
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public SkyrimSEConditionStateManager(IMod p_modMod, IGameMode p_gmdGameMode, IPluginManager p_pmgPluginManager, IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_modMod, p_gmdGameMode, p_pmgPluginManager, p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
