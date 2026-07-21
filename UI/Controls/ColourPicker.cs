using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A control that allows the user to pick a colour.
	/// </summary>
	[DefaultBindingProperty("Colour")]
	public partial class ColourPicker : UserControl, INotifyPropertyChanged
	{
		#region Properties

		/// <summary>
		/// Gets or sets the selected colour.
		/// </summary>
		/// <value>The selected colour.</value>
		[Bindable(true)]
		public Color Colour
		{
			get
			{
				return lblColour.BackColor;
			}
			set
			{
				SetColour(value);
			}
		}

		#endregion

		#region Construtors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ColourPicker()
		{
			InitializeComponent();
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the
		/// select colour button.
		/// </summary>
		/// <remarks>
		/// This displays the colour picker dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectColour_Click(object sender, EventArgs e)
		{
			if (cldColourPicker.ShowDialog(this) == DialogResult.OK)
				SetColour(cldColourPicker.Color);
		}

		/// <summary>
		/// Sets the selected colour.
		/// </summary>
		/// <param name="p_clrColor">The <see cref="Colour"/> to which to set the selected colour</param>
		protected void SetColour(Color p_clrColor)
		{
			if (lblColour.BackColor != p_clrColor)
			{
				lblColour.BackColor = p_clrColor;
				PropertyChanged(this, new PropertyChangedEventArgs("Colour"));
			}
		}

		#region INotifyPropertyChanged Members

		/// <summary>
		/// Raised whenever a property of the class changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged = delegate { };

		#endregion
	}
}
