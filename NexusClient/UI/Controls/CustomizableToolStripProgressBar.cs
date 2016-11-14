using System;
using System.Windows.Forms;
using System.Drawing;

namespace Nexus.Client.UI.Controls
{
	public partial class CustomizableToolStripProgressBar : ToolStripControlHost
	{
		#region Properties

		/// <summary>
		/// Gets the progress bar control.
		/// </summary>
		/// <value>The progress bar control.</value>
		private ProgressLabel ProgressBarControl 
		{ 
			get 
			{
				return Control as ProgressLabel; 
			} 
		}

		/// <summary>
		/// Gets or sets the progress bar control current value.
		/// </summary>
		/// <value>The current value.</value>
		public int Value
		{
			get
			{
				return ProgressBarControl.Value;
			}
			set 
			{
				int NewValue = value >= 0 ? value : 0;
				ProgressBarControl.Value = NewValue > ProgressBarControl.Maximum ? ProgressBarControl.Maximum : NewValue; 
			}
		}

		/// <summary>
		/// Gets or sets the progress bar control maximum value.
		/// </summary>
		/// <value>The maximum value.</value>
		public int Maximum
		{
			get
			{
				return ProgressBarControl.Maximum;
			}
			set 
			{
				ProgressBarControl.Maximum = value; 
			}
		}

		/// <summary>
		/// Gets or sets the progress bar control style.
		/// </summary>
		/// <value>The ProgressBarStyle.</value>
		public ProgressBarStyle Style
		{
			get
			{
				return ProgressBarControl.Style;
			}
			set
			{
				ProgressBarControl.Style = value;
			}
		}

		/// <summary>
		/// Gets or sets the progress bar control fill type.
		/// </summary>
		/// <value>The FillType.</value>
		public ProgressLabel.FillType ColorFillMode
		{
			get
			{
				return ProgressBarControl.ColorFillMode;
			}
			set
			{
				ProgressBarControl.ColorFillMode = value;
			}
		}

		/// <summary>
		/// Gets or sets the progress bar base color.
		/// </summary>
		/// <value>The base Color.</value>
		public Color BaseColor
		{
			get 
			{
				return ProgressBarControl.BarColor;
			}
			set 
			{
				ProgressBarControl.BarColor = value;
			}
		}

		/// <summary>
		/// Gets or sets whether the progress bar should show additional info.
		/// </summary>
		/// <value>True or False.</value>
		public bool ShowOptionalProgress
		{
			get
			{
				return ProgressBarControl.ShowOptionalProgress;
			}
			set
			{
				ProgressBarControl.ShowOptionalProgress = value;
			}
		}

		/// <summary>
		/// Gets whether the control is properly set.
		/// </summary>
		/// <value>True or False.</value>
		public bool IsValid
		{
			get
			{
				return (ProgressBarControl != null);
			}
		}

		/// <summary>
		/// Gets or sets the progress bar additional info to show.
		/// </summary>
		/// <value>The optional value.</value>
		public Int32 OptionalValue
		{
			get
			{
				return ProgressBarControl.OptionalValue;
			}
			set
			{
				ProgressBarControl.OptionalValue = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public CustomizableToolStripProgressBar()
			: base(new ProgressLabel())
		{
		}

		/// <summary>
		/// This contructor will initialize the progress bar with a custom color.
		/// </summary>
		/// <param name="p_color">The new base color.</param>
		public CustomizableToolStripProgressBar(Color p_color)
			: base(new ProgressLabel())
		{
			ProgressBarControl.BarColor = p_color;
		}

		#endregion
	}

	/// <summary>
	/// A custom label to show info on a ProgressBar.
	/// </summary>
	public class ProgressLabel : ProgressBar
	{
		private SolidBrush brush = null;
		private Color m_BarColor = Color.Green;
		private FillType m_Fill = FillType.Fixed;
		private bool m_booOptionalProgress = true;
		private Int32 m_intOptionalValue = 0;
		private Color textColor = DefaultTextColor;
		private string progressString;

		#region Properties

		/// <summary>
		/// Gets or sets the progress bar color.
		/// </summary>
		/// <value>The Color.</value>
		public Color BarColor
		{
			get
			{
				return m_BarColor;
			}
			set
			{
				m_BarColor = value;
			}
		}

		/// <summary>
		/// Gets or sets the progress bar fill mode.
		/// </summary>
		/// <value>The FillType.</value>
		public FillType ColorFillMode
		{
			get
			{
				return m_Fill;
			}
			set
			{
				m_Fill = value;
			}
		}

		/// <summary>
		/// Gets or sets whether the progress bar should show additional info.
		/// </summary>
		/// <value>True or False.</value>
		public bool ShowOptionalProgress
		{
			get
			{
				return m_booOptionalProgress;
			}
			set
			{
				m_booOptionalProgress = value;
			}
		}

		/// <summary>
		/// Gets or sets the progress bar additional info to show.
		/// </summary>
		/// <value>The optional value.</value>
		public Int32 OptionalValue
		{
			get
			{
				return m_intOptionalValue;
			}
			set
			{
				m_intOptionalValue = value;
			}
		}

		private static StringFormat sfCenter = new StringFormat()
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center
		};

		/// <summary>
		/// Gets or sets whether the progress bar should be visible.
		/// </summary>
		/// <value>True or False.</value>
		public bool HideBar
		{
			get 
			{ 
				return GetStyle(ControlStyles.UserPaint); 
			}
			set 
			{ 
				if (HideBar != value) 
				{ 
					SetStyle(ControlStyles.UserPaint, value); Refresh(); 
				} 
			}
		}

		/// <summary>
		/// Gets the default text color.
		/// </summary>
		/// <value>The text Color.</value>
		public static Color DefaultTextColor
		{
			get 
			{ 
				return SystemColors.ControlText; 
			}
		}

		/// <summary>
		/// Gets or sets the text color.
		/// </summary>
		/// <value>The text Color.</value>
		public Color TextColor
		{
			get 
			{ 
				return textColor; 
			}
			set 
			{ 
				textColor = value; 
			}
		}

		/// <summary>
		/// Gets or sets the label text.
		/// </summary>
		/// <value>The text to be shown on the progress bar.</value>
		public override string Text
		{
			get 
			{ 
				return base.Text; 
			}
			set 
			{ 
				if (value != Text) 
				{ 
					base.Text = value; 
					progressString = null; 
				} 
			}
		}

		/// <summary>
		/// Gets or sets the text font.
		/// </summary>
		/// <value>The text Font.</value>
		public override Font Font
		{
			get 
			{ 
				return base.Font; 
			}
			set 
			{ 
				base.Font = value; 
			}
		}

		#endregion

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ProgressLabel() 
		{ 
			SetStyle(ControlStyles.AllPaintingInWmPaint, true); 
		}

		/// <summary>
		/// Raises the CreateControl method.
		/// </summary>
		protected override void OnCreateControl()
		{
			progressString = null;
			base.OnCreateControl();
		}

		/// <summary>
		/// Processes Windows messages. 
		/// </summary>
		/// <param name="m">The Message.</param>
		protected override void WndProc(ref Message m)
		{
			switch (m.Msg)
			{
				case 15: if (HideBar) base.WndProc(ref m);
					else
					{
						ProgressBarStyle style = Style;
						if (progressString == null)
						{
							progressString = Text;
							if (!HideBar && style != ProgressBarStyle.Marquee)
							{
								int range = Maximum - Minimum;
								int value = Value;

								if (m_booOptionalProgress)
								{
									progressString = String.Format("{0}% ({1}KB/s)", Maximum == 1 ? 0 : value, m_intOptionalValue);
								}
								else
								{
									if (range > 42949672) { value = (int)((uint)value >> 7); range = (int)((uint)range >> 7); }
									if (range > 0)
										progressString = string.Format(progressString.Length == 0 ? "{0}KB" : "{1}: {0}",
											value.ToString() + "KB/" + range.ToString(), progressString);
								}
							}
						}
						if (progressString.Length == 0) base.WndProc(ref m);
						else using (Graphics g = CreateGraphics())
							{
								base.WndProc(ref m);
								OnPaint(new PaintEventArgs(g, ClientRectangle));
							}
					}
					break;
				case 0x402: goto case 0x406;
				case 0x406: progressString = null;
					base.WndProc(ref m);
					break;
				default:
					base.WndProc(ref m);
					break;
			}
		}

		/// <summary>
		/// Raises the Paint event. 
		/// </summary>
		/// <param name="e">The PaintEventArgs.</param>
		protected override void OnPaint(PaintEventArgs e)
		{
			Single percentage = 0;
			if (Maximum > 0)
				percentage = Convert.ToSingle(Value) / Convert.ToSingle(Maximum);

			if (m_Fill == FillType.Ascending)
			{
				if (percentage < 0.25)
					m_BarColor = Color.FromArgb(255, Convert.ToInt32(128 * (percentage * 4)), 0);
				else if (percentage < 0.50)
					m_BarColor = Color.FromArgb(255, Convert.ToInt32(255 * (percentage * 2)), 0);
				else if (percentage <= 1.00)
					m_BarColor = Color.FromArgb(Convert.ToInt32(255 * ((1 - percentage) * 2)), 255, 0);
			}
			else if (m_Fill == FillType.Descending)
			{
				if (percentage < 0.50)
					m_BarColor = Color.FromArgb(Convert.ToInt32(255 * (percentage * 2)), 255, 0);		
				else if (percentage < 0.75)
					m_BarColor = Color.FromArgb(255, Convert.ToInt32(255 * ((1 - percentage) * 2)), 0);
				else if (percentage <= 1.00)
					m_BarColor = Color.FromArgb(255, Convert.ToInt32(128 * ((1 - percentage) * 4)), 0);
			}

			if (brush == null || brush.Color != m_BarColor)
				brush = new SolidBrush(m_BarColor);
			Rectangle rec = new Rectangle(0, 0, this.Width, this.Height);
			if (ProgressBarRenderer.IsSupported)
				ProgressBarRenderer.DrawHorizontalBar(e.Graphics, rec);
			rec.Width = (int)(rec.Width * ((double)Value / Maximum)) - 4;
			rec.Height = rec.Height - 4;
			e.Graphics.FillRectangle(brush, 2, 2, rec.Width, rec.Height);

			Rectangle cr = ClientRectangle;
			RectangleF crF = new RectangleF(cr.Left, cr.Top, cr.Width, cr.Height);
			using (Brush br = new SolidBrush(TextColor))
				e.Graphics.DrawString(progressString, Font, br, crF, sfCenter);
			base.OnPaint(e);
		}

		/// <summary>
		/// The progress bar color fill type. 
		/// </summary>
		public enum FillType
		{
			/// <summary>
			/// Higher is better. 
			/// </summary>
			Ascending,
			/// <summary>
			/// Lower is better. 
			/// </summary>
			Descending,
			/// <summary>
			/// No color transition. 
			/// </summary>
			Fixed
		}
	}
}
