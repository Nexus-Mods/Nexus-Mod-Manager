using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// An event arguments class that describes items that have moved in a reorderable list view.
	/// </summary>
	public class ReorderedItemsEventArgs : EventArgs
	{
		/// <summary>
		/// Describes an item that has moved.
		/// </summary>
		public class ReorderedListViewItem
		{
			#region Properties

			/// <summary>
			/// Gets the item that has moved.
			/// </summary>
			/// <value>The item that has moved.</value>
			public ListViewItem Item { get; private set; }

			/// <summary>
			/// Gets the old index of the moved item.
			/// </summary>
			/// <value>The old index of the moved item.</value>
			public Int32 OldIndex { get; private set; }

			/// <summary>
			/// Gets the new index of the moved item.
			/// </summary>
			/// <value>The new index of the moved item.</value>
			public Int32 NewIndex { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_lviItem">The item that has moved.</param>
			/// <param name="p_intOldIndex">The old index of the moved item.</param>
			/// <param name="p_intNewIndex">The new index of the moved item.</param>
			public ReorderedListViewItem(ListViewItem p_lviItem, Int32 p_intOldIndex, Int32 p_intNewIndex)
			{
				Item = p_lviItem;
				OldIndex = p_intOldIndex;
				NewIndex = p_intNewIndex;
			}

			#endregion
		}

		#region Properties

		/// <summary>
		/// Gets the list of items that have moved.
		/// </summary>
		/// <value>The list of items that have moved.</value>
		public IList<ReorderedListViewItem> ReorderedListViewItems { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ReorderedItemsEventArgs()
		{
			ReorderedListViewItems = new List<ReorderedListViewItem>();
		}

		#endregion
	}

	/// <summary>
	/// An event arguments class that describes items that are being moved in a reorderable list view.
	/// </summary>
	public class ReorderingItemsEventArgs : CancelEventArgs
	{
		#region Properties

		/// <summary>
		/// Gets the list of items that are being moved.
		/// </summary>
		/// <value>The list of items that are being moved.</value>
		public IList<ReorderedItemsEventArgs.ReorderedListViewItem> ReorderedListViewItems { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ReorderingItemsEventArgs()
		{
			ReorderedListViewItems = new List<ReorderedItemsEventArgs.ReorderedListViewItem>();
		}
		#endregion
	}

	/// <summary>
	/// A <see cref="ListView"/> that allows reordering of the list items.
	/// </summary>
	/// <remarks>
	/// This items in the view can be reordered by dragging.
	/// </remarks>
	public class ReorderableListView : IconListView
	{
		#region Events

		/// <summary>
		/// Raised when the items in the list veiw are reordered.
		/// </summary>
		[Category("Behavior")]
		public event EventHandler<ReorderedItemsEventArgs> ItemsReordered = delegate { };

		/// <summary>
		/// Raised when the items in the list veiw are reordered.
		/// </summary>
		[Category("Behavior")]
		public event EventHandler<ReorderingItemsEventArgs> ItemsReordering = delegate { };

		#endregion

		/// <summary>
		/// The value of the Windows paint message.
		/// </summary>
		private const int WM_PAINT = 0x000F;

		private Int32 m_intInsertIndex = -1;
		private bool m_booIsDropping = false;
		private Queue<Exception> m_queDragDropExceptions = new Queue<Exception>();

		#region Properties

		/// <summary>
		/// Gets or sets whether drag/drop operations are allowed.
		/// </summary>
		/// <value>Whether drag/drop operations are allowed.</value>
		[DefaultValue(true)]
		public override bool AllowDrop
		{
			get
			{
				return base.AllowDrop;
			}
			set
			{
				base.AllowDrop = value;
			}
		}

		/// <summary>
		/// Gets or sets whether the entire row can be used to select items.
		/// </summary>
		/// <value>Whether the entire row can be used to select items.</value>
		[DefaultValue(true)]
		public new bool FullRowSelect
		{
			get
			{
				return base.FullRowSelect;
			}
			set
			{
				base.FullRowSelect = value;
			}
		}

		/// <summary>
		/// Gets or sets the view mode of the <see cref="ReorderableListView"/>.
		/// </summary>
		/// <value>The view mode of the <see cref="ReorderableListView"/>.</value>
		[DefaultValue(View.Details)]
		public new View View
		{
			get
			{
				return base.View;
			}
			set
			{
				base.View = value;
			}
		}

		/// <summary>
		/// Gets or sets the colour of the insertion indicator line.
		/// </summary>
		/// <value>The colour of the insertion indicator line.</value>
		[Category("Appearance")]
		[DefaultValue(typeof(Color), "Black")]
		public Color InsertionIndicatorColour { get; set; }

		#endregion

		#region Constructor

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ReorderableListView()
			: base()
		{
			AllowDrop = true;
			FullRowSelect = true;
			View = View.Details;
			InsertionIndicatorColour = Color.Black;
		}

		#endregion

		#region Item Dragging

		/// <summary>
		/// Raises the <see cref="ListView.ItemDrag"/> event of the list view.
		/// </summary>
		/// <remarks>
		/// This starts the drag/drop reorder operation if at least one item is selected.
		/// </remarks>
		/// <param name="e">An <see cref="ItemDragEventArgs"/> describing the event arguments.</param>
		protected override void OnItemDrag(ItemDragEventArgs e)
		{
			base.OnItemDrag(e);
			if (SelectedItems.Count == 0)
				return;
			DoDragDrop(new List<ListViewItem>(SelectedItems.Cast<ListViewItem>()), DragDropEffects.Move);
			while (m_queDragDropExceptions.Count > 0)
				throw new Exception("Exception thrown during drag-drop operation. See inner exception for the exception that was thrown.", m_queDragDropExceptions.Dequeue());
		}

		/// <summary>
		/// Raises the <see cref="Control.DragOver"/> event of the list view.
		/// </summary>
		/// <remarks>
		/// This determines the current insertion point of the items being moved. This also
		/// causes the insertion line to be drawn.
		/// </remarks>
		/// <param name="drgevent">A <see cref="DragEventArgs"/> describing the event arguments.</param>
		protected override void OnDragOver(DragEventArgs drgevent)
		{
			base.OnDragOver(drgevent);
			if (drgevent.Data.GetDataPresent(typeof(List<ListViewItem>)))
			{
				Point point = PointToClient(new Point(drgevent.X, drgevent.Y));
				ListViewItem lviIndex = GetItemAt(point.X, point.Y);
				Int32 intNewIndex = Items.Count - 1;
				if (lviIndex != null)
				{
					lviIndex.EnsureVisible();
					intNewIndex = lviIndex.Index;
				}
				if (intNewIndex != m_intInsertIndex)
				{
					m_intInsertIndex = intNewIndex;
					Invalidate();
				}
				drgevent.Effect = DragDropEffects.Move;

				List<ListViewItem> data = (List<ListViewItem>)drgevent.Data.GetData(typeof(List<ListViewItem>));
				ReorderingItemsEventArgs riaEventArgs = new ReorderingItemsEventArgs();
				for (Int32 i = data.Count - 1; i >= 0; i--)
					riaEventArgs.ReorderedListViewItems.Insert(0, new ReorderedItemsEventArgs.ReorderedListViewItem(data[i], data[i].Index, m_intInsertIndex + i));
				OnItemsReordering(riaEventArgs);
				if (riaEventArgs.Cancel)
					drgevent.Effect = DragDropEffects.None;
			}
		}

		/// <summary>
		/// Raises the <see cref="Control.DragDrop"/> event of the list view.
		/// </summary>
		/// <remarks>
		/// This inserts the items being reordered.
		/// </remarks>
		/// <param name="drgevent">A <see cref="DragEventArgs"/> describing the event arguments.</param>
		protected override void OnDragDrop(DragEventArgs drgevent)
		{
			base.OnDragDrop(drgevent);
			if (drgevent.Effect == DragDropEffects.Move)
			{
				List<ListViewItem> data = (List<ListViewItem>)drgevent.Data.GetData(typeof(List<ListViewItem>));

				ReorderedItemsEventArgs riaEventArgs = new ReorderedItemsEventArgs();
				m_booIsDropping = true;
				for (Int32 i = data.Count - 1; i >= 0; i--)
				{
					riaEventArgs.ReorderedListViewItems.Insert(0, new ReorderedItemsEventArgs.ReorderedListViewItem(data[i], data[i].Index, m_intInsertIndex + i));
					Items.Remove(data[i]);
				}
				for (Int32 i = data.Count - 1; i >= 0; i--)
					Items.Insert(m_intInsertIndex, data[i]).EnsureVisible();
				m_intInsertIndex = -1;
				m_booIsDropping = false;
				try
				{
					OnItemsReordered(riaEventArgs);
				}
				catch (Exception e)
				{
					//this is required as drag/drop operations, even when performed within the same
					// application, are handled by OLE, which doesn't differentiate between an external
					// an internal drag/drop source. furhter, OLE doesn't understand exceptions, and so
					// doesn't pass them on. even if it could, in general, the source of a drag/drop
					// operation wouldn't care if the target was having an issue. we only care
					// because the source and target are the same application, and the source
					// needs to rethrow the exception, or else it will be lost in the ether
					m_queDragDropExceptions.Enqueue(e);
				}
			}
		}

		#endregion

		#region Painting

		/// <summary>
		/// This handles the windows messages.
		/// </summary>
		/// <remarks>
		/// This intercepts the windows paint message so that we can do our custom painting.
		/// </remarks>
		/// <param name="m">The <see cref="Message"/> to process.</param>
		protected override void WndProc(ref Message m)
		{
			try
			{
				base.WndProc(ref m);

				//the listview control doesn't use the Paint event, so we have to watch for the
				// paint windows message
				if ((m.Msg == WM_PAINT) && (m_intInsertIndex != -1) && (!m_booIsDropping))
				{
					Rectangle rctItem = GetItemRect(m_intInsertIndex);
					//the items will be inserted below
					if (m_intInsertIndex > SelectedIndices[0])
						DrawInsertionIndicator(rctItem.Bottom);
					else	//the items will be inserted above
						DrawInsertionIndicator(rctItem.Top);
				}
			}
			catch { }
		}

		/// <summary>
		/// This draws the insertion line at the given point.
		/// </summary>
		/// <param name="p_intY">Where in the list to draw the line.</param>
		private void DrawInsertionIndicator(Int32 p_intY)
		{
			Int32 intXStart = 0;
			Int32 intXEnd = ClientRectangle.Width;

			using (Graphics gphGraphics = CreateGraphics())
				gphGraphics.DrawLine(new Pen(InsertionIndicatorColour), intXStart, p_intY, intXEnd, p_intY);
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="ItemsReordered"/> event.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected virtual void OnItemsReordered(ReorderedItemsEventArgs e)
		{
			ItemsReordered(this, e);
		}

		/// <summary>
		/// Raises the <see cref="ItemsReordering"/> event.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected virtual void OnItemsReordering(ReorderingItemsEventArgs e)
		{
			ItemsReordering(this, e);
		}
	}
}
