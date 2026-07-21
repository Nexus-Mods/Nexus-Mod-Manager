using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A list view that allows the association of images and message with sub items.
	/// </summary>
	public class IconListView : DoubleBufferedListView
	{
		private Int32 m_intFocusBoundsX = 0;
		
		#region Properties

		/// <summary>
		/// Gets the messages and images associated with the sub items.
		/// </summary>
		/// <value>The messages and images associated with the sub items.</value>
		protected Dictionary<ListViewItem.ListViewSubItem, KeyValuePair<string, Image>> Messages { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public IconListView()
		{
			Messages = new Dictionary<ListViewItem.ListViewSubItem, KeyValuePair<string, Image>>();
			OwnerDraw = true;
		}

		#endregion

		#region Message Handling

		/// <summary>
		/// Sets the message and image for the given sub item.
		/// </summary>
		/// <param name="p_lsiSubItem">The sub item for which to set the message and image.</param>
		/// <param name="p_strMessage">The message to associate with the sub item.</param>
		/// <param name="p_imgIcon">The image to associate with the subitem.</param>
		public void SetMessage(ListViewItem.ListViewSubItem p_lsiSubItem, string p_strMessage, Image p_imgIcon)
		{
			if (p_imgIcon == null)
				p_imgIcon = new Bitmap(0, 0);
			Messages[p_lsiSubItem] = new KeyValuePair<string, Image>(p_strMessage, p_imgIcon);
			this.Invalidate(p_lsiSubItem.Bounds);
		}

		/// <summary>
		/// Removes any messages and images assocaited with the given sub item.
		/// </summary>
		/// <param name="p_lsiSubItem">The sub item from which to remove any associated messages and images.</param>
		public void ClearMessage(ListViewItem.ListViewSubItem p_lsiSubItem)
		{
			if (Messages.ContainsKey(p_lsiSubItem))
			{
				Messages.Remove(p_lsiSubItem);
				this.Invalidate(p_lsiSubItem.Bounds);
			}
		}

		#endregion

		#region Owner Drawing

		/// <summary>
		/// Raises the <see cref="ListView.DrawColumnHeader"/> event.
		/// </summary>
		/// <remarks>
		/// This is where the owner draws the column headers. We let the default crawing do this.
		/// </remarks>
		/// <param name="e">A <see cref="DrawListViewColumnHeaderEventArgs"/> describing the event arguments.</param>
		protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
		{
			e.DrawDefault = true;
			base.OnDrawColumnHeader(e);
		}

		/// <summary>
		/// Raises the <see cref="ListView.DrawItem"/> event.
		/// </summary>
		/// <remarks>
		/// This is where the owner draws the complete item. We handle this
		/// is the item contains a sub item that has an associated message;
		/// otherwise we let the default drawing take place.
		/// </remarks>
		/// <param name="e">A <see cref="DrawListViewItemEventArgs"/> describing the event arguments.</param>
		protected override void OnDrawItem(DrawListViewItemEventArgs e)
		{
			e.DrawDefault = true;

			Int32 intSubItemsRight = 0;
			for (Int32 i = 0; i < e.Item.SubItems.Count; i++)
			{
				if (Messages.ContainsKey(e.Item.SubItems[i]))
					e.DrawDefault = false;
				if (i > 0 && intSubItemsRight < e.Item.SubItems[i].Bounds.Right)
					intSubItemsRight += e.Item.SubItems[i].Bounds.Right;
			}
			if (!e.DrawDefault)
			{
				if (e.Item.Focused && !CheckBoxes)
					e.DrawFocusRectangle();
				if (e.Item.Selected)
				{
					Color clrBackColor = e.Item.ListView.Focused ? SystemColors.Highlight : SystemColors.Control;
					e.Graphics.FillRectangle(new SolidBrush(clrBackColor), new Rectangle(intSubItemsRight, e.Bounds.Y, e.Item.Bounds.Width - intSubItemsRight, e.Bounds.Height));
				}
			}
			base.OnDrawItem(e);
		}

		/// <summary>
		/// Gets the bounds of our custom message icon.
		/// </summary>
		/// <returns>The bounds of our custom message icon.</returns>
		protected Rectangle GetMessageIconBounds(Rectangle p_rctCellBounds, Image p_imgIcon, bool p_booCentered)
		{
			Int32 intYOffset = (p_rctCellBounds.Height - p_imgIcon.Height) / 2;
			Int32 intXOffset = 0;
			if (p_booCentered)
				intXOffset = p_rctCellBounds.Left + (p_rctCellBounds.Width / 2) - (p_imgIcon.Width / 2);
			else
				intXOffset = p_rctCellBounds.Right - p_imgIcon.Width - intYOffset;
			Rectangle rctIconBounds = new Rectangle(new Point(intXOffset, intYOffset), p_imgIcon.Size);
			return rctIconBounds;
		}

		/// <summary>
		/// Raises the <see cref="ListView.DrawSubItem"/> event.
		/// </summary>
		/// <remarks>
		/// This is where the owner draws the specific sub item. We handle this.
		/// </remarks>
		/// <param name="e">A <see cref="DrawListViewSubItemEventArgs"/> describing the event arguments.</param>
		protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
		{
			//if the item is not in a list view, then don't bother trying to draw it,
			// as it isn't visible anyway
			if (e.Item.ListView == null)
				return;

			base.OnDrawSubItem(e);
			
			e.DrawBackground();
			
			Int32 intBoundsX = e.Bounds.X;
			Int32 intBoundsY = e.Bounds.Y;
			Int32 intBoundsWidth = e.Bounds.Width;
			Int32 intFontX = e.Bounds.X + 3;
			Int32 intFontWidth = e.Bounds.Width - 3;
			if (e.Item.SubItems[0] == e.SubItem)
			{
				intBoundsX += 4;
				intBoundsWidth -= 4;

				if (CheckBoxes)
				{
					CheckBoxState cbsState = e.Item.Checked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal;
					Size szeCheckboxSize = CheckBoxRenderer.GetGlyphSize(e.Graphics, cbsState);
					int intBoxY = intBoundsY + (e.Bounds.Height - szeCheckboxSize.Height) / 2;
					int intBoxX = intBoundsX;
					intBoundsX += 3 + szeCheckboxSize.Width;
					intBoundsWidth -= 3 + szeCheckboxSize.Width;
					intFontX += 3 + szeCheckboxSize.Width;
					intFontWidth -= 3 + szeCheckboxSize.Width;
					CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(intBoxX, intBoxY), cbsState);
				}

				m_intFocusBoundsX = intBoundsX;
			}

			Color clrForeColor = e.SubItem.ForeColor;
			if (e.Item.Selected)
			{
				clrForeColor = e.Item.ListView.Focused ? SystemColors.HighlightText : clrForeColor;
				Color clrBackColor = e.Item.ListView.Focused ? SystemColors.Highlight : SystemColors.Control;
				e.Graphics.FillRectangle(new SolidBrush(clrBackColor), new Rectangle(intBoundsX, intBoundsY, intBoundsWidth, e.Bounds.Height));
			}


			if (Messages.ContainsKey(e.SubItem))
			{
				Image imgIcon = Messages[e.SubItem].Value;
				Rectangle rctIconBounds = GetMessageIconBounds(e.Bounds, imgIcon, String.IsNullOrEmpty(e.SubItem.Text) ? true : false);
				Rectangle rctPaint = new Rectangle(new Point(rctIconBounds.X, intBoundsY + rctIconBounds.Y), rctIconBounds.Size);
				e.Graphics.DrawImage(imgIcon, rctPaint);
				intFontWidth -= rctIconBounds.Width;
			}

			Rectangle rctTextBounds = new Rectangle(intFontX, intBoundsY + 2, intFontWidth, e.Bounds.Height - 4);
			TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.SubItem.Font, rctTextBounds, clrForeColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

			if (e.Item.Focused)
			{
				Pen penFocusRectangle = new Pen(Brushes.Black);
				penFocusRectangle.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
				e.Graphics.DrawRectangle(penFocusRectangle, new Rectangle(m_intFocusBoundsX, intBoundsY, e.Item.Bounds.Width - m_intFocusBoundsX - 1, e.Item.Bounds.Height - 1));
			}
		}

		#endregion
	}
}
