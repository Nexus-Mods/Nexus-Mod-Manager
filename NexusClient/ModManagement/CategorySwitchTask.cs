using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	public class CategorySwitchTask : ThreadedBackgroundTask 
	{
		bool m_booCancel = false;

		#region Properties

		/// <summary>
		/// Gets the list of mods to edit.
		/// </summary>
		/// <value>The list of mods to edit.</value>
		protected IList<IMod> ModList { get; private set; }

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets the CategoryId to switch to.
		/// </summary>
		/// <value>The CategoryId to switch to.</value>
		protected Int32 CategoryId { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		/// <param name="p_lstMods">The mod list.</param>
		/// <param name="p_intNewValue">The new category id.</param>
		public CategorySwitchTask(ModManager p_ModManager, IList<IMod> p_lstMods, Int32 p_intNewValue)
		{
			ModManager = p_ModManager;
			ModList = p_lstMods;
			CategoryId = p_intNewValue;
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
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];

			OverallMessage = "Updating mod categories...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			ItemProgressMaximum = 1;
			OverallProgressMaximum = ModList.Count;

			foreach (IMod modMod in ModList)
			{
				if (m_booCancel)
					break;
				ItemMessage = modMod.ModName;
				ItemProgress = 1;
				ModManager.SwitchModCategory(modMod, CategoryId);
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				ItemProgress = 0;
			}
			return null;
		}
	}
}
