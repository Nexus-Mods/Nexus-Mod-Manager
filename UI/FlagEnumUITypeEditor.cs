using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace Nexus.UI
{
	/// <summary>
	/// An <see cref="UITypeEditor"/> that allows simple editing of flag enums in property grids.
	/// </summary>
	public class FlagEnumUITypeEditor : UITypeEditor
	{
		/// <summary>
		/// A list box that displays flag enums.
		/// </summary>
		private class FlagCheckedListBox : CheckedListBox
		{
			/// <summary>
			/// A list item that represents a flag enum item.
			/// </summary>
			private class FlagCheckedListBoxItem
			{
				#region Properties

				/// <summary>
				/// Gets the value represented by the item.
				/// </summary>
				/// <value>The value represented by the item.</value>
				public Int32 Value { get; private set; }

				/// <summary>
				/// Gets the display name of the item.
				/// </summary>
				/// <value>The display name of the item.</value>
				public string DisplayName { get; private set; }

				/// <summary>
				/// Gets whether the represented value is a flag.
				/// </summary>
				/// <remarks>
				/// A value is a flag if it only has one set bit.
				/// </remarks>
				/// <value>Whether the represented value is a flag.</value>
				public bool IsFlag
				{
					get
					{
						return ((Value & (Value - 1)) == 0);
					}
				}

				#endregion

				#region Constructors

				/// <summary>
				/// A simple constructor that initializes the object with the given values.
				/// </summary>
				/// <param name="p_intValue">The value that item is representing.</param>
				/// <param name="p_strDisplayName">The display name of the item.</param>
				public FlagCheckedListBoxItem(Int32 p_intValue, string p_strDisplayName)
				{
					Value = p_intValue;
					DisplayName = p_strDisplayName;
				}

				#endregion

				/// <summary>
				/// Determines if the represented value's flag is set in the composite flag
				/// represented by the given item.
				/// </summary>
				/// <param name="p_lbiComposite">The item representing the composite flag for which
				/// it is to be determined if it includes this item's represented value.</param>
				/// <returns><c>true</c> if the represented value's flag is set in the composite flag
				/// represented by the given item;
				/// <c>false</c> otherwise.</returns>
				public bool IsMemberFlag(FlagCheckedListBoxItem p_lbiComposite)
				{
					return (IsFlag && ((Value & p_lbiComposite.Value) == Value));
				}

				/// <summary>
				/// Returns the strign representation of the item.
				/// </summary>
				/// <remarks>
				/// This returns the display name of the item.
				/// </remarks>
				/// <returns>The strign representation of the item.</returns>
				public override string ToString()
				{
					return DisplayName;
				}
			}

			private Type m_tpeEnumType = null;

			#region Properties

			/// <summary>
			/// Gets or sets the flag enum value being represented by the list.
			/// </summary>
			/// <value>The flag enum value being represented by the list.</value>
			[DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Hidden)]
			public Enum EnumValue
			{
				get
				{
					object e = Enum.ToObject(m_tpeEnumType, GetCurrentValue());
					return (Enum)e;
				}
				set
				{
					m_tpeEnumType = value.GetType();
					AddListItems(m_tpeEnumType);
					Int32 intValue = (Int32)Convert.ChangeType(value, typeof(Int32));
					UpdateCheckedItems(intValue);
				}
			}

			#endregion

			#region Constructors

			/// <summary>
			/// The default constructor.
			/// </summary>
			public FlagCheckedListBox()
			{
				CheckOnClick = true;
			}

			#endregion

			#region Initialization

			/// <summary>
			/// Files the list with item representing the possible values of the enum.
			/// </summary>
			/// <param name="p_tpEnumType">The type of the enum whose values will be representated by the list.</param>
			private void AddListItems(Type p_tpEnumType)
			{
				Items.Clear();
				foreach (string strName in Enum.GetNames(p_tpEnumType))
				{
					object objValue = Enum.Parse(m_tpeEnumType, strName);
					Int32 intValue = (Int32)Convert.ChangeType(objValue, typeof(Int32));
					Add(intValue, strName);
				}
			}

			#endregion

			#region Item Addition

			/// <summary>
			/// Adds an integer value to the listbox.
			/// </summary>
			/// <param name="p_intValue">The value to add to the list.</param>
			/// <param name="p_strDisplayName">The display name of the value to add.</param>
			/// <returns>The newly added <see cref="FlagCheckedListBoxItem"/> that represents the given value.</returns>
			private FlagCheckedListBoxItem Add(Int32 p_intValue, string p_strDisplayName)
			{
				FlagCheckedListBoxItem lbiEnumItem = new FlagCheckedListBoxItem(p_intValue, p_strDisplayName);
				Items.Add(lbiEnumItem);
				return lbiEnumItem;
			}

			/// <summary>
			/// Adds a <see cref="FlagCheckedListBoxItem"/> to the list.
			/// </summary>
			/// <param name="p_lbiEnumItem">The item to add to the list.</param>
			/// <returns>The added item.</returns>
			private FlagCheckedListBoxItem Add(FlagCheckedListBoxItem p_lbiEnumItem)
			{
				Items.Add(p_lbiEnumItem);
				return p_lbiEnumItem;
			}

			#endregion

			/// <summary>
			/// Sets the check states of each item base on the given composite flag.
			/// </summary>
			/// <param name="p_intCompositeFlag">The composite flag value to represent with the list box.</param>
			protected void UpdateCheckedItems(Int32 p_intCompositeFlag)
			{
				for (Int32 i = 0; i < Items.Count; i++)
				{
					FlagCheckedListBoxItem lbiItem = (FlagCheckedListBoxItem)Items[i];
					if (lbiItem.Value == 0)
						SetItemChecked(i, p_intCompositeFlag == 0);
					else
						SetItemChecked(i, (lbiItem.Value & p_intCompositeFlag) == lbiItem.Value);
				}
			}

			/// <summary>
			/// Gets the value currently represented by the selected list items.
			/// </summary>
			/// <returns>The value currently represented by the selected list items.</returns>
			public Int32 GetCurrentValue()
			{
				Int32 intValue = 0;
				for (Int32 i = 0; i < Items.Count; i++)
				{
					FlagCheckedListBoxItem lbiItem = (FlagCheckedListBoxItem)Items[i];
					if (GetItemChecked(i))
						intValue |= lbiItem.Value;
				}
				return intValue;
			}
		}

		private FlagCheckedListBox m_clbFlagEnum;

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public FlagEnumUITypeEditor()
		{
			m_clbFlagEnum = new FlagCheckedListBox();
			m_clbFlagEnum.BorderStyle = BorderStyle.None;
		}

		#endregion

		/// <summary>
		/// Edits the given value.
		/// </summary>
		/// <param name="context">The context that provides information about the editing.</param>
		/// <param name="provider">The provider to use to get required services.</param>
		/// <param name="value">The value to edit.</param>
		/// <returns>The edited value.</returns>
		public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
		{
			if (context != null && context.Instance != null && provider != null)
			{
				IWindowsFormsEditorService wesEditor = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
				if (wesEditor != null)
				{
					Enum e = (Enum)Convert.ChangeType(value, context.PropertyDescriptor.PropertyType);
					m_clbFlagEnum.EnumValue = e;
					wesEditor.DropDownControl(m_clbFlagEnum);
					return m_clbFlagEnum.EnumValue;
				}
			}
			return null;
		}

		/// <summary>
		/// Gets the style of the editor.
		/// </summary>
		/// <param name="context">The context that provides information about the editing.</param>
		/// <returns>Always <see cref="UITypeEditorEditStyle.DropDown"/>.</returns>
		public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
		{
			return UITypeEditorEditStyle.DropDown;
		}
	}
}
