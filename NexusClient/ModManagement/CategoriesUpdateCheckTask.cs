using System;
using System.Collections.Generic;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Retrieves repository categories and merges them without overwriting user-owned categories.
	/// </summary>
	public class CategoriesUpdateCheckTask : ThreadedBackgroundTask
	{
		#region Properties

		/// <summary>
		/// Gets the mod manager used to remap affected mod assignments.
		/// </summary>
		protected ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets the current mod repository.
		/// </summary>
		protected IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the category manager being updated.
		/// </summary>
		protected CategoryManager CategoryManager { get; private set; }

		/// <summary>
		/// Gets whether every mod must be reassigned to its Nexus category after the update completes.
		/// </summary>
		public bool ResetCategoryAssignmentsAfterUpdate { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes the category update task with its dependencies.
		/// </summary>
		/// <param name="p_modManager">The current mod manager.</param>
		/// <param name="p_cmCategoryManager">The category manager to update.</param>
		/// <param name="p_modRepository">The current mod repository.</param>
		/// <param name="p_booResetCategoryAssignmentsAfterUpdate">Whether every mod must be reassigned to its Nexus category after the update.</param>
		public CategoriesUpdateCheckTask(ModManager p_modManager, CategoryManager p_cmCategoryManager, IModRepository p_modRepository, bool p_booResetCategoryAssignmentsAfterUpdate)
		{
			ModManager = p_modManager;
			CategoryManager = p_cmCategoryManager;
			ModRepository = p_modRepository;
			ResetCategoryAssignmentsAfterUpdate = p_booResetCategoryAssignmentsAfterUpdate;
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
		/// Retrieves and merges the repository category list.
		/// </summary>
		/// <param name="args">Arguments supplied to the task.</param>
		/// <returns><c>null</c> on success, or an error message on failure.</returns>
		protected override object DoWork(object[] args)
		{
			OverallMessage = LanguageManager.Get("Tasks.Categories.SetupSearch", "Updating categories info: setup search...");
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = false;
			OverallProgressMaximum = 2;

			OverallMessage = LanguageManager.Get("Tasks.Categories.Retrieving", "Retrieving the categories list... 1/2");
			StepOverallProgress();

			try
			{
				List<CategoriesInfo> categories = ModRepository.GetCategories(ModRepository.GameDomainName);
				if (categories.Count > 0)
				{
					List<IModCategory> repositoryCategories = new List<IModCategory>(categories.Count);
					foreach (CategoriesInfo category in categories)
						repositoryCategories.Add(new ModCategory(category.Id, category.Name, category.Name));

					OverallMessage = LanguageManager.Get("Tasks.Categories.Saving", "Saving the categories list... 2/2");
					CategoryManager.MergeRepositoryCategories(repositoryCategories, ModManager.RemapCategoryAssignments);
					StepOverallProgress();
				}
			}
			catch (Exception ex)
			{
				return ex.Message;
			}

			return null;
		}
	}
}
