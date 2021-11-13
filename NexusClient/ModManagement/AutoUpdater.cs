namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Runtime.Remoting.Messaging;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Windows.Forms;
    using ModRepositories;
    using Mods;
    using Util.Collections;

    /// <summary>
	/// Check for newer versions of the registered mods.
	/// </summary>
	/// <remarks>
	/// If a newer version is found, it is optionally downloaded.
	/// </remarks>
	public class AutoUpdater
	{
		/// <summary>
		/// Describes the newest info available for a mod.
		/// </summary>
		public class UpdateInfo
		{
			#region Properties

			/// <summary>
			/// Gets the mod for which the information was retrieved.
			/// </summary>
			/// <value>The mod for which the information was retrieved.</value>
			public IMod Mod { get; private set; }

			/// <summary>
			/// Gets the newest info available for the mod.
			/// </summary>
			/// <value>The newest info available for the mod.</value>
			public IModInfo NewestInfo { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="mod">The mod for which the information was retrieved.</param>
			/// <param name="newestInfo">The newest info available for the mod.</param>
			public UpdateInfo(IMod mod, IModInfo newestInfo)
			{
				Mod = mod;
				NewestInfo = newestInfo;
			}

			#endregion

			/// <summary>
			/// Determines if the given version is the same as the version in this update info.
			/// </summary>
			/// <param name="version">The version to match.</param>
			/// <returns>true if the given version is the same as the version in this update info;
			/// false otherwise.</returns>
			public bool IsMatchingVersion(string version)
			{
				var clean = new Regex(@"([v(ver)]\.?)|((\.0)+$)", RegexOptions.IgnoreCase);
				var thisVersion = clean.Replace(NewestInfo.HumanReadableVersion ?? "", "");
				var thatVersion = clean.Replace(version ?? "", "");
				if (string.IsNullOrEmpty(thisVersion) || string.IsNullOrEmpty(thatVersion))
                {
                    return true;
                }

                return string.Equals(thisVersion, thatVersion, StringComparison.OrdinalIgnoreCase);
            }
		}

		private readonly ThreadSafeObservableList<UpdateInfo> _newInfo = new ThreadSafeObservableList<UpdateInfo>();

		#region Properties

		/// <summary>
		/// Gets the newest mod info available for the mods.
		/// </summary>
		/// <value>The newest mod info available for the mods.</value>
		public ReadOnlyObservableList<UpdateInfo> NewestModInfo { get; private set; }

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		protected IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.
		/// </summary>
		/// <value>The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</value>
		protected ModRegistry ManagedModRegistry { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		/// <param name="p_mrgModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public AutoUpdater(IModRepository p_mrpModRepository, ModRegistry p_mrgModRegistry, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			ModRepository = p_mrpModRepository;
			ManagedModRegistry = p_mrgModRegistry;
			EnvironmentInfo = p_eifEnvironmentInfo;
			ManagedModRegistry.RegisteredMods.CollectionChanged += RegisteredMods_CollectionChanged;
			NewestModInfo = new ReadOnlyObservableList<UpdateInfo>(_newInfo);
		}

		#endregion

		/// <summary>
		/// Toggles the endorsement for the given mod.
		/// </summary>
		/// <param name="mod">The mod to endorse/unendorse.</param>
		public void ToggleModEndorsement(IMod mod)
		{
			var booEndorsementState = ModRepository.ToggleEndorsement(mod.Id, mod.IsEndorsed == true ? 1 : mod.IsEndorsed == false ? -1 : 0, mod.HumanReadableVersion);

            if (booEndorsementState == null)
            {
                MessageBox.Show($"Could not change endorsement status of \"{mod.ModName}\".", "Endorsement toggle error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var mifUpdatedMod = new ModInfo(mod) {IsEndorsed = booEndorsementState};
            mifUpdatedMod.HumanReadableVersion = string.IsNullOrEmpty(mifUpdatedMod.LastKnownVersion) ? mifUpdatedMod.HumanReadableVersion : mifUpdatedMod.LastKnownVersion;
			AddNewVersionNumberForMod(mod, mifUpdatedMod);
			mod.UpdateInfo(mifUpdatedMod, false);
		}

		/// <summary>
		/// Switches the mod category.
		/// </summary>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_intCategoryId">The new category id.</param>
		public void SwitchModCategory(IMod p_modMod, int p_intCategoryId)
		{
			ModInfo mifUpdatedMod = new ModInfo(p_modMod);
			mifUpdatedMod.CustomCategoryId = p_intCategoryId;
			mifUpdatedMod.UpdateWarningEnabled = p_modMod.UpdateWarningEnabled;
			mifUpdatedMod.UpdateChecksEnabled = p_modMod.UpdateChecksEnabled;
			p_modMod.UpdateInfo(mifUpdatedMod, false);
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the list of
		/// managed mods.
		/// </summary>
		/// <remarks>
		/// This checks for the newest information for mods that are added to the mod manager.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void RegisteredMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (!EnvironmentInfo.Settings.CheckForNewModVersions)
            {
                return;
            }

            switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Replace:
					foreach (IMod modMod in e.NewItems)
                    {
						if (string.IsNullOrEmpty(modMod.DownloadId))
							((Func<IMod, IModInfo>)CheckForUpdate).BeginInvoke(modMod, GotNewVersionNumber, modMod);
                    }

                    break;
				case NotifyCollectionChangedAction.Remove:
				case NotifyCollectionChangedAction.Reset:
					foreach (IMod modMod in e.OldItems)
                    {
                        _newInfo.RemoveAll(x => x.Mod == modMod);
                    }

                    break;
			}
		}

		/// <summary>
		/// Checks for the updated information for the given mod.
		/// </summary>
		/// <param name="mod">The mod for which to check for updates.</param>
		/// <returns>The latest information available for the given mod,
		/// or <c>null</c> if no information is available.</returns>
		private IModInfo CheckForUpdate(IMod mod)
		{
			IModInfo modInfo = null;

            if (!ModRepository.IsOffline)
            {
                //get mod info
                for (var i = 0; i <= 2; i++)
                {
                    if (!string.IsNullOrEmpty(mod.Id))
                    {
                        modInfo = ModRepository.GetModInfo(mod.Id);
                    }

                    if (modInfo == null)
                    {
                        modInfo = ModRepository.GetModInfoForFile(mod.Filename);
                    }

                    if (modInfo != null)
                    {
                        break;
                    }

					System.Threading.Tasks.Task.Delay(1000);
                }
            }

            return modInfo;
        }

		/// <summary>
		/// The callback when information about a mod has been retrieved.
		/// </summary>
		/// <param name="result">The asynchronous result.</param>
		private void GotNewVersionNumber(IAsyncResult result)
		{
			var dlgUpdateChecker = (Func<IMod, IModInfo>)((AsyncResult)result).AsyncDelegate;
			var mifNewestInfo = dlgUpdateChecker.EndInvoke(result);
			result.AsyncWaitHandle.Close();
			var modMod = (IMod)result.AsyncState;

            AddNewVersionNumberForMod(modMod, mifNewestInfo);
		}

		/// <summary>
		/// Adds the newest information for the given mod.
		/// </summary>
		/// <param name="mod">The mod for which to add the newest info.</param>
		/// <param name="newestInfo">The newest info to add for the given mod.</param>
		public void AddNewVersionNumberForMod(IMod mod, IModInfo newestInfo)
		{
			lock (_newInfo)
			{
				var existingIndex = _newInfo.IndexOf(x => x.Mod == mod);

                if (existingIndex < 0)
                {
                    _newInfo.Add(new UpdateInfo(mod, newestInfo));
                }
                else
                {
                    _newInfo[existingIndex] = new UpdateInfo(mod, newestInfo);
                }
            }
		}
	}
}
