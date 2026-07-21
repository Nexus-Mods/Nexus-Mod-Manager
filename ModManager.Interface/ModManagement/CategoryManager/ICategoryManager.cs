using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Describes the properties and methods of a category manager.
	/// </summary>
	/// <remarks>
	/// A category manager.
	/// </remarks>
	public interface ICategoryManager
	{
		#region Properties

		/// <summary>
		/// Gets the list of categories.
		/// </summary>
		/// <value>The list of categories.</value>
		ThreadSafeObservableList<IModCategory> Categories { get; }

		#endregion

		#region Singleton

		/// <summary>
		/// Releases the manager's hold on physical resources.
		/// </summary>
		void Release();

		#endregion

		#region Category Management

		/// <summary>
		/// Adds a category to the category manager.
		/// </summary>
		/// <remarks>
		/// Adding a category to the category manager assigns it a unique key.
		/// </remarks>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> being added.</param>
		IModCategory AddCategory(IModCategory p_mctCategory);

		/// <summary>
		/// Updates the category file.
		/// </summary>
		void UpdateCategoryFile();

		/// <summary>
		/// Removes the category from the category manager.
		/// </summary>
		/// <param name="p_mctUninstaller">The category to remove.</param>
		void RemoveCategory(IModCategory p_mctUninstaller);

		#endregion

		/// <summary>
		/// This backsup the category manager.
		/// </summary>
		void Backup();
	}
}
