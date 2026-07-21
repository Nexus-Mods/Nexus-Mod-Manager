using System;
using System.ComponentModel.Design;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Handles the editing of the <see cref="VerticalTabControl.TabPages"/> collection
	/// is the designer.
	/// </summary>
	public class VerticalTabPageCollectionEditor : CollectionEditor
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_tpeType">The type of the ovjects in the collection being edited.</param>
		public VerticalTabPageCollectionEditor(Type p_tpeType)
			: base(p_tpeType)
		{
		}

		#endregion

		/// <summary>
		/// Gets the type of the item in the collection being edited.
		/// </summary>
		/// <returns>The type of the item in the collection being edited.</returns>
		protected override Type CreateCollectionItemType()
		{
			return typeof(VerticalTabPage);
		}

		/// <summary>
		/// Sets the collection to the set of given items.
		/// </summary>
		/// <param name="p_objTabPages">The collection being edited.</param>
		/// <param name="p_objValues">The array of items to which to set the collection being edited.</param>
		/// <returns>The resulting collection with the new items.</returns>
		protected override object SetItems(object p_objTabPages, object[] p_objValues)
		{
			VerticalTabControl.TabPageCollection tpcPages = (VerticalTabControl.TabPageCollection)base.SetItems(p_objTabPages, p_objValues);
			for (Int32 i = 0; i < tpcPages.Count; i++)
				tpcPages[i].PageIndex = i;
			return tpcPages;
		}

		/// <summary>
		/// Creates an instance of an item for use in the collection being edited.
		/// </summary>
		/// <remarks>
		/// This sets the text of the created <see cref="VerticalTabPage"/> to its
		/// name.
		/// </remarks>
		/// <param name="p_tpeItemType">The type of the item to be created.</param>
		/// <returns>A new object of the given type.</returns>
		protected override object CreateInstance(Type p_tpeItemType)
		{
			object objNew = base.CreateInstance(p_tpeItemType);
			VerticalTabPage vtpPage = objNew as VerticalTabPage;
			if (vtpPage != null)
				vtpPage.Text = vtpPage.Name;
			return objNew;
		}
	}
}
