using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Games;
using Nexus.UI.Controls;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display mod management.
	/// </summary>
	public class ProfileManagerVM
	{
		#region Properties

		/// <summary>
		/// Gets the mod manager to use to manage mods.
		/// </summary>
		/// <value>The mod manager to use to manage mods.</value>
		public IProfileManager ProfileManager { get; private set; }

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		public IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<IMod> ManagedMods { get; private set; }

		/// <summary>
		/// Gets the current virtual mod activator.
		/// </summary>
		/// <value>The current virtual mod activator.</value>
		public IVirtualModActivator VirtualModActivator
		{
			get
			{
				return VirtualModActivator;
			}
		}

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		/// <summary>
		/// Gets the theme to use for the UI.
		/// </summary>
		/// <value>The theme to use for the UI.</value>
		public Theme CurrentTheme { get; private set; }

		/// <summary>
		/// Gets whether the manager is in offline mode.
		/// </summary>
		/// <value>Whether the manager is in offline mode.</value>
		public bool OfflineMode
		{
			get
			{
				return ModRepository.IsOffline;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_mmdProfileManager">The mod manager to use to manage mods.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		/// <param name="p_thmTheme">The current theme to use for the views.</param>
		public ProfileManagerVM(IProfileManager p_mmdProfileManager, ReadOnlyObservableList<IMod> p_rolManagedMods, IModRepository p_mmrModRepository, ISettings p_setSettings, Theme p_thmTheme)
		{
			ProfileManager = p_mmdProfileManager;
			ModRepository = p_mmrModRepository;
			Settings = p_setSettings;
			CurrentTheme = p_thmTheme;
			ManagedMods = p_rolManagedMods;
		}

		#endregion
	}
}
