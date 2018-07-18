using System;
using System.Diagnostics;
using Nexus.Client.Games.Morrowind.PluginManagement.Boss;
using Nexus.Client.Updating;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo.Updating
{
	/// <summary>
	/// Updates BOSS's files.
	/// </summary>
	/// <remarks>
	/// This updater is currently limited to updating the masterlist. The BAPI
	/// DLLs are currently only distributed with each programme build.
	/// </remarks>
	public class BossUpdater : UpdaterBase
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
				return "BOSS Updater";
			}
		}

		/// <summary>
		/// Gets the BOSS plugin sorter.
		/// </summary>
		/// <value>The BOSS plugin sorter.</value>
		protected BossSorter BossSorter { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_bstBoss">The BOSS instance to use to set plugin order.</param>
		public BossUpdater(IEnvironmentInfo p_eifEnvironmentInfo, BossSorter p_bstBoss)
			: base(p_eifEnvironmentInfo)
		{
			SetRequiresRestart(false);
			BossSorter = p_bstBoss;
		}

		#endregion

		/// <summary>
		/// Performs the update.
		/// </summary>
		/// <returns><c>true</c> if the update completed successfully;
		/// <c>false</c> otherwise.</returns>
		public override bool Update()
		{
			Trace.TraceInformation("Checking for new BOSS masterlist version...");
			Trace.Indent();

			SetProgressMaximum(2);

			SetMessage("Checking for new masterlist...");

			//this doesn't work for most game modes
			// we don't need the list right now anyway, so let's not bother
			/*try
			{
				bool booHasUpdate = BossSorter.MasterlistHasUpdate();
				SetProgress(1);

				if (CancelRequested)
				{
					Trace.Unindent();
					return CancelUpdate();
				}

				if (booHasUpdate)
				{
					if (!Confirm(String.Format("A new version of the BOSS masterlist is available.{0}Would you like to download and install it?", Environment.NewLine), "New Version"))
					{
						Trace.Unindent();
						return CancelUpdate();
					}

					SetMessage("Downloading new masterlist...");
					BossSorter.UpdateMasterlist();
				}
				else
					SetMessage("BOSS is already up to date.");
			}
			catch (BossException e)
			{
				Trace.TraceError("Unable to update masterlist.");
				TraceUtil.TraceException(e);
				return false;
			}*/

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
			SetMessage("Cancelled BOSS update.");
			SetProgress(2);
			return true;
		}
	}
}
