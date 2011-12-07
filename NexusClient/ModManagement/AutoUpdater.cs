using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using System.Collections.Generic;
using Nexus.Client.ModManagement.InstallationLog;
using System.IO;

namespace Nexus.Client.ModManagement
{
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
			/// <param name="p_modMod">The mod for which the information was retrieved.</param>
			/// <param name="p_strNewestInfo">The newest info available for the mod.</param>
			public UpdateInfo(IMod p_modMod, IModInfo p_strNewestInfo)
			{
				Mod = p_modMod;
				NewestInfo = p_strNewestInfo;
			}

			#endregion
		}

		private ThreadSafeObservableList<UpdateInfo> m_oclNewInfo = new ThreadSafeObservableList<UpdateInfo>();

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
			ManagedModRegistry.RegisteredMods.CollectionChanged += new NotifyCollectionChangedEventHandler(RegisteredMods_CollectionChanged);
			NewestModInfo = new ReadOnlyObservableList<UpdateInfo>(m_oclNewInfo);
		}

		#endregion

		/// <summary>
		/// Check for updates to the maanged mods.
		/// </summary>
		/// <remarks>
		/// This method performs its work asynchronously.
		/// </remarks>
		public void CheckForUpdates()
		{
			foreach (IMod modMod in ManagedModRegistry.RegisteredMods)
				((Func<IMod, IModInfo>)CheckForUpdate).BeginInvoke(modMod, GotNewVersionNumber, modMod);
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
				return;
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Replace:
					foreach (IMod modMod in e.NewItems)
						((Func<IMod, IModInfo>)CheckForUpdate).BeginInvoke(modMod, GotNewVersionNumber, modMod);
					break;
				case NotifyCollectionChangedAction.Remove:
				case NotifyCollectionChangedAction.Reset:
					foreach (IMod modMod in e.OldItems)
						m_oclNewInfo.RemoveAll(x => x.Mod == modMod);
					break;
			}
		}

		/// <summary>
		/// Checks for the updated information for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to check for updates.</param>
		/// <returns>The lastest informaiton available for the given mod,
		/// or <c>null</c> if no information is available.</returns>
		private IModInfo CheckForUpdate(IMod p_modMod)
		{
			IModInfo mifInfo = null;
			try
			{
				//get mod info
				if (!String.IsNullOrEmpty(p_modMod.Id))
					mifInfo = ModRepository.GetModInfo(p_modMod.Id);
				if (mifInfo == null)
					mifInfo = ModRepository.GetModInfoForFile(p_modMod.Filename);
				if (mifInfo == null)
				{
					string strSearchTerms = p_modMod.ModName;
					if (String.IsNullOrEmpty(strSearchTerms))
						strSearchTerms = Path.GetFileNameWithoutExtension(p_modMod.Filename).Replace("_", " ").Replace("-", " ");
					//use heuristics to find info
					if (!String.IsNullOrEmpty(strSearchTerms))
						mifInfo = ModRepository.FindMods(strSearchTerms, true).FirstOrDefault();
				}
				if (mifInfo == null)
					return null;
				return mifInfo;
			}
			catch (RepositoryUnavailableException)
			{
				//the repository is not available, so don't bother
				return null;
			}
		}

		/// <summary>
		/// The callback when information about a mod has been retrieved.
		/// </summary>
		/// <param name="p_arsResult">The asynchronous result.</param>
		private void GotNewVersionNumber(IAsyncResult p_arsResult)
		{
			Func<IMod, IModInfo> dlgUpdateChecker = (Func<IMod, IModInfo>)((AsyncResult)p_arsResult).AsyncDelegate;
			IModInfo mifNewestInfo = dlgUpdateChecker.EndInvoke(p_arsResult);
			p_arsResult.AsyncWaitHandle.Close();
			IMod modMod = (IMod)p_arsResult.AsyncState;
			AddNewVersionNumberForMod(modMod, mifNewestInfo);
		}

		/// <summary>
		/// Adds the newest information for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to add the newest info.</param>
		/// <param name="p_mifNewestInfo">The newest info to add for the given mod.</param>
		private void AddNewVersionNumberForMod(IMod p_modMod, IModInfo p_mifNewestInfo)
		{
			lock (m_oclNewInfo)
			{
				Int32 intExistingIndex = m_oclNewInfo.IndexOf(x => (x.Mod == p_modMod));
				if (intExistingIndex < 0)
					m_oclNewInfo.Add(new UpdateInfo(p_modMod, p_mifNewestInfo));
				else
					m_oclNewInfo[intExistingIndex] = new UpdateInfo(p_modMod, p_mifNewestInfo);
			}
		}
	}
}
