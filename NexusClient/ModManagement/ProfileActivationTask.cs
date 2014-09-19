using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	public class ProfileActivationTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Properties

		/// <summary>
		/// Gets the list of mods to edit.
		/// </summary>
		/// <value>The list of mods to edit.</value>
		protected IList<IVirtualModLink> ModLinks { get; private set; }

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected ModManager ModManager { get; private set; }


		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		/// <param name="p_lstMods">The mod list.</param>
		/// <param name="p_intNewValue">The new category id.</param>
		public ProfileActivationTask(ModManager p_mmgModManager, IList<IVirtualModLink> p_lstModLinks)
		{
			ModManager = p_mmgModManager;
			ModLinks = p_lstModLinks;
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
			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			OverallMessage = "Switching Mod Profile...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			ItemProgress = 0;
			ItemProgressMaximum = 1;
			OverallProgressMaximum = ModLinks.Count;

			ModManager.VirtualModActivator.PurgeLinks();

			foreach (IVirtualModLink vmlModLink in ModLinks)
			{
				if (m_booCancel)
					break;
				ItemProgress = 0;
				ItemMessage = "Activating new profile: " + vmlModLink.ModName;
				IMod modMod = ModManager.ManagedMods.FirstOrDefault(x => x.Filename == vmlModLink.ModFileName);
				if (modMod != null)
				{
					if (vmlModLink.Active)
						ModManager.VirtualModActivator.AddFileLink(modMod, vmlModLink.VirtualModPath, true, false, vmlModLink.Priority);
					else
						ModManager.VirtualModActivator.AddInactiveLink(modMod, vmlModLink.VirtualModPath, vmlModLink.Priority);
				}
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				StepItemProgress();
			}

			ModManager.VirtualModActivator.SaveList();
			return null;
		}
	}
}
