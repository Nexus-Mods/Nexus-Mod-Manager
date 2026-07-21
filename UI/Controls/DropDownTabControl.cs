using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A tab control whose tabs are in a drop down box.
	/// </summary>
	[DefaultProperty("SelectedPage")]
	[DefaultEvent("SelectedIndexChanged")]
	[Designer(typeof(DropDownTabControlDesigner))]
	public class DropDownTabControl : ScrollableControl
	{
		/// <summary>
		/// Raised when the selected tab page index has changed.
		/// </summary>
		[Category("Action")]
		public event EventHandler SelectedIndexChanged
		{
			add
			{
				m_cbxSelector.SelectedIndexChanged += value;
			}
			remove
			{
				m_cbxSelector.SelectedIndexChanged -= value;
			}
		}

		/// <summary>
		/// The event arguments for when a tab page is added or removed from the control.
		/// </summary>
		public class TabPageEventArgs : EventArgs
		{
			private DropDownTabPage m_tpgPage = null;

			#region Properties

			/// <summary>
			/// Gets the tab page that was affected by the event.
			/// </summary>
			/// <value>The tab page that was affected by the event.</value>
			public DropDownTabPage TabPage
			{
				get
				{
					return m_tpgPage;
				}
			}

			#endregion

			#region Constructors

			/// <summary>
			/// A simple consturctor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_tpgPage">The tab page that was affected by the event.</param>
			public TabPageEventArgs(DropDownTabPage p_tpgPage)
			{
				m_tpgPage = p_tpgPage;
			}

			#endregion
		}

		/// <summary>
		/// A collection of <see cref="DropDownTabPage"/>s.
		/// </summary>
		public class TabPageCollection : IList<DropDownTabPage>, ICollection<DropDownTabPage>, IEnumerable<DropDownTabPage>, ICollection, IList
		{
			#region Events

			/// <summary>
			/// Raised when a <see cref="DropDownTabPage"/> is added to the collection.
			/// </summary>
			public event EventHandler<TabPageEventArgs> TabPageAdded;

			/// <summary>
			/// Raised when a <see cref="DropDownTabPage"/> is removed from the collection.
			/// </summary>
			public event EventHandler<TabPageEventArgs> TabPageRemoved;

			#endregion

			private List<DropDownTabPage> m_lstPages = new List<DropDownTabPage>();

			#region Event Raising

			/// <summary>
			/// Raises the <see cref="TabPageAdded"/> event.
			/// </summary>
			/// <param name="p_tpgPage">The <see cref="DropDownTabPage"/> that was added.</param>
			protected void OnTabPageAdded(DropDownTabPage p_tpgPage)
			{
				if (TabPageAdded != null)
					TabPageAdded(this, new TabPageEventArgs(p_tpgPage));
			}

			/// <summary>
			/// Raises the <see cref="TabPageRemoved"/> event.
			/// </summary>
			/// <param name="p_tpgPage">The <see cref="DropDownTabPage"/> that was removed.</param>
			protected void OnTabPageRemoved(DropDownTabPage p_tpgPage)
			{
				if (TabPageRemoved != null)
					TabPageRemoved(this, new TabPageEventArgs(p_tpgPage));
			}

			#endregion

			#region IList<DropDownTabPage> Members

			/// <summary>
			/// Gets the index of the given item in the collection.
			/// </summary>
			/// <param name="item">The item whose index is to be determined.</param>
			/// <returns>The index of the given item in the collection if it is in the collection;
			/// -1 otherwise.</returns>
			public int IndexOf(DropDownTabPage item)
			{
				return m_lstPages.IndexOf(item);
			}

			/// <summary>
			/// Inserts the given item into the collection at the given index.
			/// </summary>
			/// <param name="index">The index at which to insert the item.</param>
			/// <param name="item">The item to insert.</param>
			/// <exception cref="IndexOutOfRangeException">Thrown if the given index is less than 0
			/// or greater than <see cref="Count"/>.</exception>
			public void Insert(int index, DropDownTabPage item)
			{
				m_lstPages.Insert(index, item);
				OnTabPageAdded(item);
			}

			/// <summary>
			/// Removes the item at the given index.
			/// </summary>
			/// <param name="index">The index of the item to remove.</param>
			/// <exception cref="IndexOutOfRangeException">Thrown if the given index is less than 0
			/// or greater than or equal to <see cref="Count"/>.</exception>
			public void RemoveAt(int index)
			{
				DropDownTabPage tpgPage = m_lstPages[index];
				m_lstPages.RemoveAt(index);
				OnTabPageRemoved(tpgPage);
			}

			/// <summary>
			/// Gets or sets the item at the given index.
			/// </summary>
			/// <param name="index">The index of the item to get or set.</param>
			/// <returns>The index of the item at the specified index.</returns>
			/// <exception cref="IndexOutOfRangeException">Thrown if the given index is less than 0
			/// or greater than or equal to <see cref="Count"/>.</exception>
			public DropDownTabPage this[int index]
			{
				get
				{
					return m_lstPages[index];
				}
				set
				{
					m_lstPages[index] = value;
				}
			}

			#endregion

			#region ICollection<DropDownTabPage> Members

			/// <summary>
			/// Adds the given item to the end of the collection.
			/// </summary>
			/// <param name="item">The item to add.</param>
			public void Add(DropDownTabPage item)
			{
				m_lstPages.Add(item);
				OnTabPageAdded(item);
			}

			/// <summary>
			/// Empties the collection.
			/// </summary>
			public void Clear()
			{
				DropDownTabPage tpgPage = null;
				for (Int32 i = m_lstPages.Count - 1; i >= 0; i--)
				{
					tpgPage = m_lstPages[i];
					RemoveAt(i);
				}
			}

			/// <summary>
			/// Determines if the given item is in the collection.
			/// </summary>
			/// <param name="item">The item whose presence in the collection is to be determined.</param>
			/// <returns><c>true</c> if the item is in the collection;
			/// <c>false</c> otherwise.</returns>
			public bool Contains(DropDownTabPage item)
			{
				return m_lstPages.Contains(item);
			}

			/// <summary>
			/// Copies the contents of this collection to the given array starting at the specified index.
			/// </summary>
			/// <param name="array">The array into which to copy the contents of this collection.</param>
			/// <param name="arrayIndex">The index in the given array at which to begin copying.</param>
			/// <exception cref="ArgumentException">Thrown if the number of elements in the collection
			/// is greater than the available space from <paramref name="arrayIndex"/> to the end of
			/// the destination array.</exception>
			public void CopyTo(DropDownTabPage[] array, int arrayIndex)
			{
				m_lstPages.CopyTo(array, arrayIndex);
			}

			/// <summary>
			/// Gets the number of items in the collection.
			/// </summary>
			/// <value>The number of items in the collection.</value>
			public int Count
			{
				get
				{
					return m_lstPages.Count;
				}
			}

			/// <summary>
			/// Gets whether the collection is readonly.
			/// </summary>
			/// <value>Whether the collection is readonly.</value>
			public bool IsReadOnly
			{
				get
				{
					return false;
				}
			}

			/// <summary>
			/// Removes the given item from the collection.
			/// </summary>
			/// <param name="item">The item to remove from the collection.</param>
			/// <returns><c>true</c> if the item was removed from the collection;
			/// <c>false</c> if the item couldn't be removed because it was not in the collection.</returns>
			public bool Remove(DropDownTabPage item)
			{
				if (m_lstPages.Remove(item))
				{
					OnTabPageRemoved(item);
					return true;
				}
				return false;
			}

			#endregion

			#region IEnumerable<DropDownTabPage> Members

			/// <summary>
			/// Gets an enumerator over the nodes in the view.
			/// </summary>
			/// <returns>An enumerator over the nodes in the view.</returns>
			public IEnumerator<DropDownTabPage> GetEnumerator()
			{
				return m_lstPages.GetEnumerator();
			}

			#endregion

			#region IEnumerable Members

			/// <summary>
			/// Gets an enumerator over the nodes in the view.
			/// </summary>
			/// <returns>An enumerator over the nodes in the view.</returns>
			IEnumerator IEnumerable.GetEnumerator()
			{
				return m_lstPages.GetEnumerator();
			}

			#endregion

			#region ICollection Members

			/// <summary>
			/// Copies the contents of the set to the given array, starting at the given index.
			/// </summary>
			/// <param name="array">The array into which to copy the items.</param>
			/// <param name="index">The index in the array at which to start copying the items.</param>
			void ICollection.CopyTo(Array array, int index)
			{
				if (array == null)
					throw new ArgumentNullException("array is null.");
				if (index < 0)
					throw new ArgumentOutOfRangeException("index", index, "index is less than 0.");
				if (array.Length - index < m_lstPages.Count)
					throw new ArgumentException("Insufficient space in target array.");
				for (Int32 i = index; i < m_lstPages.Count + index; i++)
					array.SetValue(m_lstPages[i - index], i);
			}

			/// <summary>
			/// Gets whether the set is synchronized.
			/// </summary>
			/// <value>Whether the set is synchronized.</value>
			bool ICollection.IsSynchronized
			{
				get
				{
					return false;
				}
			}

			/// <summary>
			/// Gets the sync root of the set.
			/// </summary>
			/// <value>The sync root of the set.</value>
			object ICollection.SyncRoot
			{
				get
				{
					return this;
				}
			}

			#endregion

			#region IList Members

			/// <summary>
			/// Adds the given item to the set.
			/// </summary>
			/// <param name="value">The item to add.</param>
			int IList.Add(object value)
			{
				DropDownTabPage vtpPage = value as DropDownTabPage;
				if (vtpPage == null)
					throw new ArgumentException(String.Format("Cannot add item of type '{0}'. Expecting '{1}'.", value.GetType(), typeof(DropDownTabPage)), "value");
				Add(vtpPage);
				return Count - 1;
			}

			/// <summary>
			/// Determines if the given item is in the set.
			/// </summary>
			/// <param name="value">The item to look for in the set.</param>
			/// <returns><c>true</c> if the item is in the set;
			/// <c>false</c> otherwise.</returns>
			bool IList.Contains(object value)
			{
				DropDownTabPage vtpPage = value as DropDownTabPage;
				if (vtpPage == null)
					return false;
				return Contains(vtpPage);
			}

			/// <summary>
			/// Determines the first index of the specified item.
			/// </summary>
			/// <param name="value">The item whose index in the set is to be found.</param>
			/// <returns>The first index of the specified item, or -1 if the item is not in the set.</returns>
			int IList.IndexOf(object value)
			{
				DropDownTabPage vtpPage = value as DropDownTabPage;
				if (vtpPage == null)
					return -1;
				return IndexOf(vtpPage);
			}

			/// <summary>
			/// Inserts the given item at the specifed index. Cannot be used with a set.
			/// </summary>
			/// <param name="index">The index at which to insert the item.</param>
			/// <param name="value">The item to insert.</param>
			/// <exception cref="InvalidOperationException">Thrown always.</exception>
			void IList.Insert(int index, object value)
			{
				DropDownTabPage vtpPage = value as DropDownTabPage;
				if (vtpPage == null)
					throw new ArgumentException(String.Format("Cannot insert item of type '{0}'. Expecting '{1}'.", value.GetType(), typeof(DropDownTabPage)), "value");
				Insert(index, vtpPage);
			}

			/// <summary>
			/// Gets whether the set is fixed size. Always <c>false</c>.
			/// </summary>
			/// <value>Whether the set is fixed size. Always <c>false</c>.</value>
			bool IList.IsFixedSize
			{
				get
				{
					return false;
				}
			}

			/// <summary>
			/// Removes the given item from the set.
			/// </summary>
			/// <param name="value">The item to remove</param>
			void IList.Remove(object value)
			{
				DropDownTabPage vtpPage = value as DropDownTabPage;
				if (vtpPage != null)
					Remove(vtpPage);
			}

			/// <summary>
			/// Gets or sets the item ate the specified index. Indexed setting cannot be used with a set.
			/// </summary>
			/// <param name="index">The index of the item to get or set.</param>
			/// <returns>Nothing.</returns>
			object IList.this[int index]
			{
				get
				{
					return this[index];
				}
				set
				{
					DropDownTabPage vtpPage = value as DropDownTabPage;
					if (vtpPage == null)
						throw new ArgumentException(String.Format("Cannot set item of type '{0}'. Expecting '{1}'.", value.GetType(), typeof(DropDownTabPage)), "value");
					this[index] = vtpPage;
				}
			}

			#endregion
		}

		private Panel m_pnlDropDownPanel = null;
		private ComboBox m_cbxSelector = null;
		private Label m_lblLabel = null;
		private TabPageCollection m_tpcPages = null;
		private DropDownTabPage m_tpgSelected = null;

		#region Properties

		/// <summary>
		/// Gets the tab selector combobox.
		/// </summary>
		/// <value>The tab selector combobox.</value>
		internal ComboBox TabSelector
		{
			get
			{
				return m_cbxSelector;
			}
		}

		/// <summary>
		/// Gets or sets the text of the tab selector.
		/// </summary>
		public override string Text
		{
			get
			{
				return m_lblLabel.Text;
			}
			set
			{
				m_lblLabel.Text = value;
			}
		}

		/// <summary>
		/// Gets the tab pages of this control.
		/// </summary>
		/// <value>The tab pages of this control.</value>
		[Editor(typeof(DropDownTabPageCollectionEditor), typeof(UITypeEditor))]
		public TabPageCollection TabPages
		{
			get
			{
				return m_tpcPages;
			}
		}

		/// <summary>
		/// Gets or sets the currently selected tab page.
		/// </summary>
		/// <value>The currently selected tab page.</value>
		[TypeConverter(typeof(SelectedDropDownTabPageConverter))]
		public DropDownTabPage SelectedTabPage
		{
			get
			{
				return m_tpgSelected;
			}
			set
			{
				if (m_tpgSelected == value)
					return;
				m_tpgSelected = value;
				if (m_tpgSelected != null)
				{
					m_tpgSelected.BringToFront();
					m_cbxSelector.SelectedItem = m_tpgSelected;
				}
			}
		}

		/// <summary>
		/// Gets or sets the index of the currently selected tab page.
		/// </summary>
		/// <value>The index of the currently selected tab page.</value>
		[Browsable(false)]
		public Int32 SelectedIndex
		{
			get
			{
				return this.TabPages.IndexOf(SelectedTabPage);
			}
			set
			{
				if (value == -1)
					SelectedTabPage = null;
				else
					SelectedTabPage = TabPages[value];
			}
		}

		/// <summary>
		/// Gets or sets the width of the tabs.
		/// </summary>
		/// <value>The width of the tabs.</value>
		[Category("Appearance")]
		[DefaultValue(150)]
		public Int32 TabWidth
		{
			get
			{
				return m_cbxSelector.Width;
			}
			set
			{
				m_cbxSelector.Width = value;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		[DefaultValue(KnownColor.Window)]
		public override Color BackColor
		{
			get
			{
				return base.BackColor;
			}
			set
			{
				base.BackColor = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public DropDownTabControl()
		{
			m_tpcPages = new TabPageCollection();
			m_tpcPages.TabPageAdded += new EventHandler<TabPageEventArgs>(AddTabPage);
			m_tpcPages.TabPageRemoved += new EventHandler<TabPageEventArgs>(RemoveTabPage);

			m_pnlDropDownPanel = new Panel();
			m_pnlDropDownPanel.Dock = DockStyle.Top;
			m_pnlDropDownPanel.DataBindings.Add("BackColor", this, "BackColor");

			m_lblLabel = new Label();
			m_lblLabel.AutoSize = true;
			m_lblLabel.Text = Name;
			m_lblLabel.Location = new Point(3, 3);

			m_cbxSelector = new ComboBox();
			m_cbxSelector.Location = new Point(13, m_lblLabel.Top + 13 + 4);
			m_cbxSelector.DisplayMember = "Text";
			m_cbxSelector.SelectedIndexChanged += new EventHandler(TabSelected);

			m_pnlDropDownPanel.Height = m_cbxSelector.Location.Y + m_cbxSelector.Height + 4;
			m_pnlDropDownPanel.Controls.Add(m_lblLabel);
			m_pnlDropDownPanel.Controls.Add(m_cbxSelector);
			Controls.Add(m_pnlDropDownPanel);
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="Control.CreateControl"/> event.
		/// </summary>
		/// <remarks>
		/// I can't get the <see cref="ComboBox"/> to be interactive in design mode when its
		/// style is set to <see cref="ComboBoxStyle.DropDownList"/>, so only set the style
		/// if we aren't designing.
		/// </remarks>
		protected override void OnCreateControl()
		{
			base.OnCreateControl();
			if (!DesignMode)
				m_cbxSelector.DropDownStyle = ComboBoxStyle.DropDownList;
		}


		/// <summary>
		/// Handles the <see cref="ComboBox.SelectedIndexChanged"/> event of the tabs.
		/// </summary>
		/// <remarks>
		/// This sets the <see cref="DropDownTabPage"/> associated with the tab
		/// that was clicked as the <see cref="SelectedTabPage"/>.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected void TabSelected(object sender, EventArgs e)
		{
			SelectedTabPage = (DropDownTabPage)m_cbxSelector.SelectedItem;
		}

		/// <summary>
		/// Handles the <see cref="TabPageCollection.TabPageAdded"/> event of this
		/// control's collection of <see cref="DropDownTabPage"/>s.
		/// </summary>
		/// <remarks>
		/// This wires the added tab page into the control, and adds it to the <see cref="UI.Controls"/>
		/// collection.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="DropDownTabControl.TabPageEventArgs"/> describing the event arguments.</param>
		private void AddTabPage(object sender, DropDownTabControl.TabPageEventArgs e)
		{
			DropDownTabPage ctlPage = e.TabPage;
			if (ctlPage.PageIndex == -1)
				ctlPage.PageIndex = m_tpcPages.Count - 1;
			if (!m_tpcPages.Contains(ctlPage))
				m_tpcPages.Add(ctlPage);
			ctlPage.PageIndexChanged += new EventHandler(PageIndexChanged);
			ctlPage.TextChanged += new EventHandler(PageTextChanged);
			InsertTabPageInSelector(ctlPage);
			ctlPage.Dock = DockStyle.Fill;
			Controls.Add(e.TabPage);
			SelectedTabPage = ctlPage;
		}

		/// <summary>
		/// Handles the <see cref="TabPageCollection.TabPageRemoved"/> event of this
		/// control's collection of <see cref="DropDownTabPage"/>s.
		/// </summary>
		/// <remarks>
		/// This unwires the tab page from the control, and removes it to the <see cref="UI.Controls"/>
		/// collection.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="DropDownTabControl.TabPageEventArgs"/> describing the event arguments.</param>
		private void RemoveTabPage(object sender, DropDownTabControl.TabPageEventArgs e)
		{
			DropDownTabPage ctlPage = e.TabPage;
			ctlPage.PageIndexChanged -= new EventHandler(PageIndexChanged);
			ctlPage.TextChanged -= new EventHandler(PageTextChanged);
			m_cbxSelector.Items.Remove(ctlPage);
			for (Int32 i = 0; i < m_tpcPages.Count; i++)
				if (m_tpcPages[i].PageIndex > ctlPage.PageIndex)
					m_tpcPages[i].PageIndex--;
			if (SelectedTabPage == ctlPage)
			{
				if (m_tpcPages.Count == 0)
					SelectedTabPage = null;
				else if (SelectedIndex == m_tpcPages.Count)
					SelectedIndex--;
				else
					SelectedIndex++;
			}
			Controls.Remove(e.TabPage);
		}

		/// <summary>
		/// Raises the <see cref="Control.ControlAdded"/> event.
		/// </summary>
		/// <remarks>
		/// This ensures that any <see cref="DropDownTabPage"/>s added to this control are added
		/// from the <see cref="TabPages"/> collection.
		/// </remarks>
		/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
		protected override void OnControlAdded(ControlEventArgs e)
		{
			base.OnControlAdded(e);
			if (e.Control is DropDownTabPage)
			{
				DropDownTabPage ctlPage = (DropDownTabPage)e.Control;
				if (!m_tpcPages.Contains(ctlPage))
					m_tpcPages.Add(ctlPage);
			}
		}

		/// <summary>
		/// Raises the <see cref="Control.ControlAdded"/> event.
		/// </summary>
		/// <remarks>
		/// This ensures that any <see cref="DropDownTabPage"/>s removed from this control are removed
		/// from the <see cref="TabPages"/> collection.
		/// </remarks>
		/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
		protected override void OnControlRemoved(ControlEventArgs e)
		{
			base.OnControlRemoved(e);
			if (e.Control is DropDownTabPage)
			{
				DropDownTabPage ctlPage = (DropDownTabPage)e.Control;
				m_tpcPages.Remove(ctlPage);
			}
		}

		/// <summary>
		/// Inserts the given <see cref="DropDownTabPage"/> into the selector drop box at the correct index based on the
		/// tab's <see cref="DropDownTabPage.PageIndex"/>.
		/// </summary>
		/// <param name="p_ddpPage">The <see cref="DropDownTabPage"/> to insert.</param>
		protected void InsertTabPageInSelector(DropDownTabPage p_ddpPage)
		{
			DropDownTabPage ddpCurrent = null;
			for (Int32 i = 0; i < m_cbxSelector.Items.Count; i++)
			{
				ddpCurrent = (DropDownTabPage)m_cbxSelector.Items[i];
				if (ddpCurrent.PageIndex > p_ddpPage.PageIndex)
				{
					m_cbxSelector.Items.Insert(i, p_ddpPage);
					return;
				}
			}
			m_cbxSelector.Items.Add(p_ddpPage);
		}

		/// <summary>
		/// This updates the items int he selector combo box to reflect changes in tab page
		/// properties.
		/// </summary>
		protected void UpdateSelector()
		{
			m_cbxSelector.BeginUpdate();
			m_cbxSelector.Items.Clear();
			foreach (DropDownTabPage ddpPage in m_tpcPages)
				InsertTabPageInSelector(ddpPage);
			m_cbxSelector.SelectedItem = SelectedTabPage;
			m_cbxSelector.EndUpdate();
		}

		/// <summary>
		/// Handles the <see cref="DropDownTabPage.PageIndexChanged"/>
		/// </summary>
		/// <remarks>
		/// This reorders the items in the selector combo box to match the new page order.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void PageIndexChanged(object sender, EventArgs e)
		{
			UpdateSelector();
		}

		/// <summary>
		/// Handles the <see cref="DropDownTabPage.PageIndexChanged"/>
		/// </summary>
		/// <remarks>
		/// This reorders the items in the selector combo box to match the new page order.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void PageTextChanged(object sender, EventArgs e)
		{
			UpdateSelector();
		}
	}
}
