using System;
using System.IO;

namespace Nexus.Client.Games.Gamebryo
{
	/// <summary>
	/// Provides common information about Gamebryo based games.
	/// </summary>
	public abstract class GamebryoGameModeDescriptorBase : GameModeDescriptorBase
	{
		private string[] m_strCriticalPlugins = null;

		#region Properties

		/// <summary>
		/// Gets the directory where Fallout 3 plugins are installed.
		/// </summary>
		/// <value>The directory where Fallout 3 plugins are installed.</value>
		protected virtual string PluginDirectory
		{
			get
			{
				return Path.Combine(InstallationPath, "Data");
			}
		}

		/// <summary>
		/// Gets the list of critical plugin filenames, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin filenames, ordered by load order.</value>
		protected abstract string[] OrderedCriticalPluginFilenames { get; }

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		public override string[] OrderedCriticalPluginNames
		{
			get
			{
				if (m_strCriticalPlugins == null)
				{
					m_strCriticalPlugins = new string[OrderedCriticalPluginFilenames.Length];
					for (Int32 i = OrderedCriticalPluginFilenames.Length - 1; i >= 0; i--)
						m_strCriticalPlugins[i] = Path.Combine(PluginDirectory, OrderedCriticalPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				}
				return m_strCriticalPlugins;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public GamebryoGameModeDescriptorBase(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
