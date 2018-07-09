using System;
using System.Collections.Generic;
using System.Drawing;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.PluginManagement;
using Nexus.Client.Games;
using System.IO;

namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// This class manages the state of the installation.
	/// </summary>
	public class ConditionStateManager
	{
		/// <summary>
		/// Describe the owner and value of a condition flag.
		/// </summary>
		private class FlagValue
		{
			/// <summary>
			/// The value of the flag.
			/// </summary>
			public string Value;

			/// <summary>
			/// The owner of the flag.
			/// </summary>
			public Option Owner;
		}

		private Dictionary<string, FlagValue> m_dicFlags = new Dictionary<string, FlagValue>();
		private Dictionary<string, Image> m_dicImageCache = new Dictionary<string, Image>();

		#region Properties

		/// <summary>
		/// Gets the mod for which the script is running.
		/// </summary>
		/// <value>The mod for which the script is running.</value>
		public IMod Mod { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		public IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the manager to use to manage plugins.
		/// </summary>
		/// <value>The manager to use to manage plugins.</value>
		public IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the current values of the flags that have been set.
		/// </summary>
		/// <value>The current values of the flags that have been set.</value>
		public Dictionary<string, string> FlagValues
		{
			get
			{
				Dictionary<string, string> dicValues = new Dictionary<string, string>();
				foreach (KeyValuePair<string, FlagValue> kvpValue in m_dicFlags)
					dicValues[kvpValue.Key] = kvpValue.Value.Value;
				return dicValues;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The game mode currently bieng managed.</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public ConditionStateManager(IMod p_modMod, IGameMode p_gmdGameMode, IPluginManager p_pmgPluginManager, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			PluginManager = p_pmgPluginManager;
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		/// <summary>
		/// Sets the value of a conditional flag.
		/// </summary>
		/// <param name="p_strFlagName">The name of the falg whose value is to be set.</param>
		/// <param name="p_strValue">The value to which to set the flag.</param>
		/// <param name="p_pifPlugin">The plugin that is responsible for setting the flag's value.</param>
		public void SetFlagValue(string p_strFlagName, string p_strValue, Option p_pifPlugin)
		{
			if (!m_dicFlags.ContainsKey(p_strFlagName))
				m_dicFlags[p_strFlagName] = new FlagValue();
			m_dicFlags[p_strFlagName].Value = p_strValue;
			m_dicFlags[p_strFlagName].Owner = p_pifPlugin;
		}

		/// <summary>
		/// Removes the all flags owned by the given option.
		/// </summary>
		/// <param name="p_pifPlugin">The owner of the flags to remove.</param>
		public void RemoveFlags(Option p_pifPlugin)
		{
			List<string> lstFlags = new List<string>(m_dicFlags.Keys);
			foreach (string strFlag in lstFlags)
				if (m_dicFlags[strFlag].Owner == p_pifPlugin)
					m_dicFlags.Remove(strFlag);
		}

		/// <summary>
		/// Gets the specified image from the mod against which the script is running.
		/// </summary>
		/// <param name="p_strPath">The path to the image in the mod to retrieve.</param>
		/// <returns>The specified image from the mod against which the script is running.</returns>
		public Image GetImage(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath))
				return null;
			if (!m_dicImageCache.ContainsKey(p_strPath))
			{
				try
				{
					m_dicImageCache[p_strPath] = new ExtendedImage(Mod.GetFile(p_strPath));
				}
				catch (FileNotFoundException)
				{
					return Properties.Resources.notFoundJPG;
				}
			}
			return m_dicImageCache[p_strPath];
		}
	}
}
