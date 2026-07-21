using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A page in a <see cref="DropDownTabControl"/>.
	/// </summary>
	public class DropDownTabPage : Panel
	{
		/// <summary>
		/// Raised when the selected tab page index has changed.
		/// </summary>
		public event EventHandler PageIndexChanged;

		private Int32 m_intIndex = -1;

		#region Properties

		/// <summary>
		/// Gets or sets the index of this page in the <see cref="DropDownTabControl"/>.
		/// </summary>
		/// <value>The index of this page in the <see cref="DropDownTabControl"/>.</value>
		[Category("Behavior")]
		public Int32 PageIndex
		{
			get
			{
				return m_intIndex;
			}
			set
			{
				if (value != m_intIndex)
				{
					m_intIndex = value;
					if (PageIndexChanged != null)
						PageIndexChanged(this, new EventArgs());
				}
			}
		}

		/// <summary>
		/// Gets or sets the text that appears in this page's tab.
		/// </summary>
		/// <value>The text that appears in this page's tab.</value>
		[Browsable(true)]
		[Category("Appearance")]
		public override string Text
		{
			get
			{
				return base.Text;
			}
			set
			{
				base.Text = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public DropDownTabPage()
		{
		}

		#endregion
	}
}
