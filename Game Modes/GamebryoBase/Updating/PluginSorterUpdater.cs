using System;
using System.Diagnostics;
using Nexus.Client.Games.Gamebryo.PluginManagement.Sorter;
using Nexus.Client.Updating;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo.Updating
{
	/// <summary>
	/// Updates LOOT's files.
	/// </summary>
	/// <remarks>
	/// This updater is currently limited to updating the masterlist. The LOOT API
	/// DLLs are currently only distributed with each programme build.
	/// </remarks>
	public class PluginSorterUpdater : UpdaterBase
	{
		#region Properties

		/// <summary>
		/// Gets the updater's name.
		/// </summary>
		/// <value>The updater's name.</value>
		public override string Name
		{
			get
			{
				return "masterlist.yaml file Updater";
			}
		}

		/// <summary>
		/// Gets the plugin sorter.
		/// </summary>
		/// <value>The plugin sorter.</value>
		protected PluginSorter PluginSorter { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_lstSorter">The PluginSorter instance to use to set plugin order.</param>
		public PluginSorterUpdater(IEnvironmentInfo p_eifEnvironmentInfo, PluginSorter p_lstSorter)
			: base(p_eifEnvironmentInfo)
		{
			SetRequiresRestart(false);
			PluginSorter = p_lstSorter;
		}

		#endregion

		/// <summary>
		/// Performs the update.
		/// </summary>
		/// <returns><c>true</c> if the update completed successfully;
		/// <c>false</c> otherwise.</returns>
		public override bool Update()
		{
			try
			{
				SetMessage(String.Format("Checking for new {0} version...", "masterlist.yaml"));
				PluginSorter.UpdateMasterlist();
			}
			catch (Exception e)
			{
				Trace.TraceError("Unable to update masterlist.yaml file.");
				TraceUtil.TraceException(e);
				return false;
			}
			
			SetProgress(2);
			Trace.Unindent();
			return true;
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		/// <remarks>
		/// This is a convience method that allows the setting of the message and
		/// the determination of the return value in one call.
		/// </remarks>
		/// <returns>Always <c>true</c>.</returns>
		private bool CancelUpdate()
		{
			SetMessage("Cancelled masterlist.yaml file update.");
			SetProgress(2);
			return true;
		}
	}
}
