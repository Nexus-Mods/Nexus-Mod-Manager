using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Collections;
using System.Drawing;
using System.Drawing.Design;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A tab control whose tabs or vertically stacked on the left.
	/// </summary>
	[DefaultProperty("SelectedPage")]
	[DefaultEvent("SelectedTabPageChanged")]
	[Designer(typeof(VerticalTabControlDesigner))]
	public class VerticalTabControl : ScrollableControl
	{
		/// <summary>
		/// Raised when the selected tab page has changed.
		/// </summary>
		[Category("Action")]
		public event EventHandler<TabPageEventArgs> SelectedTabPageChanged;

		/// <summary>
		/// The event arguments for when a tab page is added or removed from the control.
		/// </summary>
		public class TabPageEventArgs : EventArgs
		{
			private VerticalTabPage m_tpgPage = null;

			#region Properties

			/// <summary>
			/// Gets the tab page that was affected by the event.
			/// </summary>
			/// <value>The tab page that was affected by the event.</value>
			public VerticalTabPage TabPage
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
			public TabPageEventArgs(VerticalTabPage p_tpgPage)
			{
				m_tpgPage = p_tpgPage;
			}

			#endregion
		}

		/// <summary>
		/// A collection of <see cref="VerticalTabPage"/>s.
		/// </summary>
		public class TabPageCollection : IList<VerticalTabPage>, ICollection<VerticalTabPage>, IEnumerable<VerticalTabPage>, ICollection, IList
		{
			#region Events

			/// <summary>
			/// Raised when a <see cref="VerticalTabPage"/> is added to the collection.
			/// </summary>
			public event EventHandler<TabPageEventArgs> TabPageAdded;

			/// <summary>
			/// Raised when a <see cref="VerticalTabPage"/> is removed from the collection.
			/// </summary>
			public event EventHandler<TabPageEventArgs> TabPageRemoved;

			#endregion

			private List<VerticalTabPage> m_lstPages = new List<VerticalTabPage>();

			#region Event Raising

			/// <summary>
			/// Raises the <see cref="TabPageAdded"/> event.
			/// </summary>
			/// <param name="p_tpgPage">The <see cref="VerticalTabPage"/> that was added.</param>
			protected void OnTabPageAdded(VerticalTabPage p_tpgPage)
			{
				if (TabPageAdded != null)
					TabPageAdded(this, new TabPageEventArgs(p_tpgPage));
			}

			/// <summary>
			/// Raises the <see cref="TabPageRemoved"/> event.
			/// </summary>
			/// <param name="p_tpgPage">The <see cref="VerticalTabPage"/> that was removed.</param>
			protected void OnTabPageRemoved(VerticalTabPage p_tpgPage)
			{
				if (TabPageRemoved != null)
					TabPageRemoved(this, new TabPageEventArgs(p_tpgPage));
			}

			#endregion

			#region IList<VerticalTabPage> Members

			/// <seealso cref="IList{VerticalTabPage}.IndexOf"/>
			public int IndexOf(VerticalTabPage item)
			{
				return m_lstPages.IndexOf(item);
			}

			/// <seealso cref="IList{VerticalTabPage}.Insert"/>
			public void Insert(int index, VerticalTabPage item)
			{
				m_lstPages.Insert(index, item);
				OnTabPageAdded(item);
			}

			/// <seealso cref="IList{VerticalTabPage}.RemoveAt"/>
			public void RemoveAt(int index)
			{
				VerticalTabPage tpgPage = m_lstPages[index];
				m_lstPages.RemoveAt(index);
				OnTabPageRemoved(tpgPage);
			}

			/// <seealso cref="IList{VerticalTabPage}.this"/>
			public VerticalTabPage this[int index]
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

			#region ICollection<VerticalTabPage> Members

			/// <seealso cref="ICollection{VerticalTabPage}.Add"/>
			public void Add(VerticalTabPage item)
			{
				m_lstPages.Add(item);
				OnTabPageAdded(item);
			}

			/// <seealso cref="ICollection{VerticalTabPage}.Clear"/>
			public void Clear()
			{
				VerticalTabPage tpgPage = null;
				for (Int32 i = m_lstPages.Count - 1; i >= 0; i--)
				{
					tpgPage = m_lstPages[i];
					RemoveAt(i);
				}
			}

			/// <seealso cref="ICollection{VerticalTabPage}.Contains"/>
			public bool Contains(VerticalTabPage item)
			{
				return m_lstPages.Contains(item);
			}

			/// <seealso cref="ICollection{VerticalTabPage}.CopyTo"/>
			public void CopyTo(VerticalTabPage[] array, int arrayIndex)
			{
				m_lstPages.CopyTo(array, arrayIndex);
			}

			/// <seealso cref="ICollection{VerticalTabPage}.Count"/>
			public int Count
			{
				get
				{
					return m_lstPages.Count;
				}
			}

			/// <seealso cref="ICollection{VerticalTabPage}.IsReadOnly"/>
			public bool IsReadOnly
			{
				get
				{
					return false;
				}
			}

			/// <seealso cref="ICollection{VerticalTabPage}.Remove"/>
			public bool Remove(VerticalTabPage item)
			{
				if (m_lstPages.Remove(item))
				{
					OnTabPageRemoved(item);
					return true;
				}
				return false;
			}

			#endregion

			#region IEnumerable<VerticalTabPage> Members

			/// <seealso cref="IEnumerable{VerticalTabPage}.GetEnumerator()"/>
			public IEnumerator<VerticalTabPage> GetEnumerator()
			{
				return m_lstPages.GetEnumerator();
			}

			#endregion

			#region IEnumerable Members

			/// <seealso cref="IEnumerable.GetEnumerator()"/>
			IEnumerator IEnumerable.GetEnumerator()
			{
				return m_lstPages.GetEnumerator();
			}

			#endregion

			#region ICollection Members

			/// <seealso cref="ICollection.CopyTo"/>
			public void CopyTo(Array array, int index)
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

			/// <seealso cref="ICollection.IsSynchronized"/>
			public bool IsSynchronized
			{
				get
				{
					return false;
				}
			}

			/// <seealso cref="ICollection.SyncRoot"/>
			public object SyncRoot
			{
				get
				{
					return this;
				}
			}

			#endregion

			#region IList Members

			/// <seealso cref="IList.Add"/>
			public int Add(object value)
			{
				VerticalTabPage vtpPage = value as VerticalTabPage;
				if (vtpPage == null)
					throw new ArgumentException(String.Format("Cannot add item of type '{0}'. Expecting '{1}'.", value.GetType(), typeof(VerticalTabPage)), "value");
				Add(vtpPage);
				return Count - 1;
			}

			/// <seealso cref="IList.Contains"/>
			public bool Contains(object value)
			{
				VerticalTabPage vtpPage = value as VerticalTabPage;
				if (vtpPage == null)
					return false;
				return Contains(vtpPage);
			}

			/// <seealso cref="IList.IndexOf"/>
			public int IndexOf(object value)
			{
				VerticalTabPage vtpPage = value as VerticalTabPage;
				if (vtpPage == null)
					return -1;
				return IndexOf(vtpPage);
			}

			/// <seealso cref="IList.Insert"/>
			public void Insert(int index, object value)
			{
				VerticalTabPage vtpPage = value as VerticalTabPage;
				if (vtpPage == null)
					throw new ArgumentException(String.Format("Cannot insert item of type '{0}'. Expecting '{1}'.", value.GetType(), typeof(VerticalTabPage)), "value");
				Insert(index, vtpPage);
			}

			/// <seealso cref="IList.IsFixedSize"/>
			public bool IsFixedSize
			{
				get
				{
					return false;
				}
			}

			/// <seealso cref="IList.Remove"/>
			public void Remove(object value)
			{
				VerticalTabPage vtpPage = value as VerticalTabPage;
				if (vtpPage != null)
					Remove(vtpPage);
			}

			/// <seealso cref="IList.this"/>
			object IList.this[int index]
			{
				get
				{
					return this[index];
				}
				set
				{
					VerticalTabPage vtpPage = value as VerticalTabPage;
					if (vtpPage == null)
						throw new ArgumentException(String.Format("Cannot set item of type '{0}'. Expecting '{1}'.", value.GetType(), typeof(VerticalTabPage)), "value");
					this[index] = vtpPage;
				}
			}

			#endregion
		}

		private PanelToolStrip m_ptsTabContainer = null;
		private TabPageCollection m_tpcPages = null;
		private VerticalTabPage m_tpgSelected = null;

		#region Properties

		/// <summary>
		/// Gets the tab pages of this control.
		/// </summary>
		/// <value>The tab pages of this control.</value>
		[Editor(typeof(VerticalTabPageCollectionEditor), typeof(UITypeEditor))]
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
		[TypeConverter(typeof(SelectedVerticalTabPageConverter))]
		public VerticalTabPage SelectedTabPage
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
					m_tpgSelected.TabButton.SetSelected();
				}
				if (SelectedTabPageChanged != null)
					SelectedTabPageChanged(this, new TabPageEventArgs(m_tpgSelected));
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
				return m_ptsTabContainer.Width;
			}
			set
			{
				m_ptsTabContainer.Width = value;
			}
		}

		/// <summary>
		/// Gets or sets whether the tabs are visible.
		/// </summary>
		/// <value>Whether the tabs are visible.</value>
		[Category("Appearance")]
		[DefaultValue(true)]
		public virtual bool TabsVisible
		{
			get
			{
				return m_ptsTabContainer.Visible;
			}
			set
			{
				m_ptsTabContainer.Visible = value;
			}
		}

		/// <summary>
		/// Gets or sets the back colour if the control.
		/// </summary>
		/// <value>The back colour if the control.</value>
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

		/// <summary>
		/// Gets or sets the direction the tool strip is oriented.
		/// </summary>
		/// <value>
		/// The direction the tool strip is oriented.
		/// </value>
		[Category("Appearance")]
		[DefaultValue(Orientation.Vertical)]
		public Orientation Direction
		{
			get
			{
				return m_ptsTabContainer.Direction;
			}
			set
			{
				m_ptsTabContainer.Direction = value;

				if (value == Orientation.Horizontal)
				{
					m_ptsTabContainer.Dock = DockStyle.Top;
					m_ptsTabContainer.Height = 23;
				}
				else
				{
					m_ptsTabContainer.Dock = DockStyle.Left;
					m_ptsTabContainer.Width = 150;
				}
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public VerticalTabControl()
		{
			BackColor = Color.FromKnownColor(KnownColor.Window);
			m_tpcPages = new TabPageCollection();
			m_tpcPages.TabPageAdded += new EventHandler<TabPageEventArgs>(AddTabPage);
			m_tpcPages.TabPageRemoved += new EventHandler<TabPageEventArgs>(RemoveTabPage);

			m_ptsTabContainer = new PanelToolStrip();
			m_ptsTabContainer.BorderStyle = BorderStyle.Fixed3D;
			m_ptsTabContainer.Dock = DockStyle.Left;
			m_ptsTabContainer.Width = 150;
			m_ptsTabContainer.DataBindings.Add("BackColor", this, "BackColor");

			Controls.Add(m_ptsTabContainer);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="PanelToolStripItem.Selected"/> event of the tabs.
		/// </summary>
		/// <remarks>
		/// This sets the <see cref="VerticalTabButton"/> associated with the tab
		/// that was clicked as the <see cref="SelectedTabPage"/>.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected void TabSelected(object sender, EventArgs e)
		{
			SelectedTabPage = ((VerticalTabButton)sender).TabPage;
		}

		/// <summary>
		/// Handles the <see cref="TabPageCollection.TabPageAdded"/> event of this
		/// control's collection of <see cref="VerticalTabPage"/>s.
		/// </summary>
		/// <remarks>
		/// This wires the added tab page into the control, and adds it to the <see cref="UI.Controls"/>
		/// collection.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="VerticalTabControl.TabPageEventArgs"/> describing the event arguments.</param>
		private void AddTabPage(object sender, VerticalTabControl.TabPageEventArgs e)
		{
			VerticalTabPage ctlPage = e.TabPage;
			if (ctlPage.PageIndex == -1)
				ctlPage.PageIndex = m_tpcPages.Count - 1;
			if (!m_tpcPages.Contains(ctlPage))
				m_tpcPages.Add(ctlPage);
			ctlPage.TabButton.Selected += TabSelected;
			m_ptsTabContainer.addToolStripItem(ctlPage.TabButton);
			ctlPage.Dock = DockStyle.Fill;
			SelectedTabPage = ctlPage;
			Controls.Add(e.TabPage);
		}

		/// <summary>
		/// Handles the <see cref="TabPageCollection.TabPageRemoved"/> event of this
		/// control's collection of <see cref="VerticalTabPage"/>s.
		/// </summary>
		/// <remarks>
		/// This unwires the tab page from the control, and removes it to the <see cref="UI.Controls"/>
		/// collection.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="VerticalTabControl.TabPageEventArgs"/> describing the event arguments.</param>
		private void RemoveTabPage(object sender, VerticalTabControl.TabPageEventArgs e)
		{
			VerticalTabPage ctlPage = e.TabPage;
			ctlPage.TabButton.Selected -= TabSelected;
			for (Int32 i = 0; i < m_tpcPages.Count; i++)
				if (m_tpcPages[i].PageIndex > ctlPage.PageIndex)
					m_tpcPages[i].PageIndex--;
			m_ptsTabContainer.removeToolStripItem(ctlPage.TabButton);
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
		/// This ensures that any <see cref="VerticalTabPage"/>s added to this control are added
		/// from the <see cref="TabPages"/> collection.
		/// </remarks>
		/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
		protected override void OnControlAdded(ControlEventArgs e)
		{
			base.OnControlAdded(e);
			if (e.Control is VerticalTabPage)
			{
				VerticalTabPage ctlPage = (VerticalTabPage)e.Control;
				if (!m_tpcPages.Contains(ctlPage))
					m_tpcPages.Add(ctlPage);
			}
		}

		/// <summary>
		/// Raises the <see cref="Control.ControlAdded"/> event.
		/// </summary>
		/// <remarks>
		/// This ensures that any <see cref="VerticalTabPage"/>s removed from this control are removed
		/// from the <see cref="TabPages"/> collection.
		/// </remarks>
		/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
		protected override void OnControlRemoved(ControlEventArgs e)
		{
			base.OnControlRemoved(e);
			if (e.Control is VerticalTabPage)
			{
				VerticalTabPage ctlPage = (VerticalTabPage)e.Control;
				m_tpcPages.Remove(ctlPage);
			}
		}
	}
}
