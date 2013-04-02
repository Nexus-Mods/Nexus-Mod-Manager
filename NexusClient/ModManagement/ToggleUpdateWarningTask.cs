﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using ChinhDo.Transactions;

namespace Nexus.Client.ModManagement
{
	public class ToggleUpdateWarningTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Properties

		protected HashSet<IMod> m_hashMods = new HashSet<IMod>();
		protected TxFileManager tfmFileManager = new TxFileManager();
		protected bool? m_booEnable = false;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public ToggleUpdateWarningTask(HashSet<IMod> p_hashMods, bool? p_booEnable, ConfirmActionMethod p_camConfirm)
		{
			m_hashMods = p_hashMods;
			m_booEnable = p_booEnable;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			base.OnTaskEnded(e);
		}
		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod p_camConfirm)
		{
			Start(p_camConfirm);
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			base.Cancel();
			m_booCancel = true;
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			OverallMessage = "Toggling all update warnings...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = m_hashMods.Count;
			ShowItemProgress = false;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			foreach (IMod modMod in m_hashMods)
			{
				ModInfo mifUpdatedMod = new ModInfo(modMod);
				if (m_booEnable == null)
					mifUpdatedMod.UpdateWarningEnabled = !modMod.UpdateWarningEnabled;
				else
				{
					if (modMod.UpdateWarningEnabled == m_booEnable.Value)
						continue;
					else
						mifUpdatedMod.UpdateWarningEnabled = m_booEnable.Value;
				}
				modMod.UpdateInfo((IModInfo)mifUpdatedMod, false);

				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (m_booCancel)
					break;
			}

			return null;
		}
	}
}
