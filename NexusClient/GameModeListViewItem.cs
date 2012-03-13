using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nexus.Client.Games;

namespace Nexus.Client
{
	/// <summary>
	/// A list view item that displays info about a game mode.
	/// </summary>
	public partial class GameModeListViewItem : UserControl, IGameModeListViewItem
	{
		[DllImport("gdi32.dll")]
		private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

		private PrivateFontCollection pfcFonts = new PrivateFontCollection();
		private bool m_booIsSelected = false;

		#region Properties

		/// <summary>
		/// Gets or sets whether the item is selected in the list view.
		/// </summary>
		/// <value>Whether the item is selected in the list view.</value>
		public bool Selected
		{
			get
			{
				return m_booIsSelected;
			}
			set
			{
				m_booIsSelected = value;
				if (Parent is GameModeListView)
					((GameModeListView)Parent).SelectedItem = this;
				//BackColor = value ? SystemColors.ControlDark : SystemColors.Control;
				BorderStyle = value ? BorderStyle.FixedSingle : BorderStyle.None;
			}
		}

		/// <summary>
		/// Gets the descriptor for the game modewhose install path is to be found.
		/// </summary>
		/// <value>The descriptor for the game modewhose install path is to be found.</value>
		protected IGameModeDescriptor GameMode { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameModeInfo">The descriptor for the game modewhose install path is to be found.</param>
		public GameModeListViewItem(IGameModeDescriptor p_gmdGameModeInfo)
		{
			GameMode = p_gmdGameModeInfo;
			InitializeComponent();

			IntPtr pbyt = IntPtr.Zero;
			try
			{
				byte[][] rgbyt = new byte[2][];
				rgbyt[0] = Properties.Resources.LinBiolinum_RI;
				rgbyt[1] = Properties.Resources.LinBiolinum_RB;
				for (Int32 i = 0; i <= rgbyt.GetUpperBound(0); i++)
				{
					pbyt = Marshal.AllocCoTaskMem(rgbyt[i].Length);
					Marshal.Copy(rgbyt[i], 0, pbyt, rgbyt[i].Length);
					pfcFonts.AddMemoryFont(pbyt, rgbyt[i].Length);
					uint dummy = 0;
					AddFontMemResourceEx(pbyt, (uint)rgbyt[i].Length, IntPtr.Zero, ref dummy);
				}
			}
			finally
			{
				Marshal.FreeCoTaskMem(pbyt);
			}
			//these lines are an alternate method to load the font, but require
			// the fonts to be files, instead of embedded resources
			//pfcFonts.AddFontFile(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), @"data\fonts\LinBiolinum_RI.ttf"));
			//pfcFonts.AddFontFile(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), @"data\fonts\LinBiolinum_RB.ttf"));

			lblGameModeName.Text = GameMode.Name;
			lblGameModeName.ForeColor = GameMode.ModeTheme.PrimaryColour;
			pbxGameLogo.Image = new Icon(GameMode.ModeTheme.Icon, 48, 48).ToBitmap();
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="Control.ParentChanged"/> event.
		/// </summary>
		/// <remarks>
		/// This sizes the control to the list view it has been added to.
		/// </remarks>
		/// <param name="e">The <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnParentChanged(EventArgs e)
		{
			if (Parent is GameModeListView)
			{
				AutoSize = false;
				Dock = DockStyle.Fill;
				MinimumSize = PreferredSize;
			}
			base.OnParentChanged(e);
		}

		/// <summary>
		/// Raises the <see cref="Control.Click"/> event.
		/// </summary>
		/// <remarks>
		/// This tells the containing list veiw to select this item.
		/// </remarks>
		/// <param name="e">The <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnClick(EventArgs e)
		{
			if (Parent is GameModeListView)
				((GameModeListView)Parent).SelectedItem = this;
			base.OnClick(e);
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of all contained controls.
		/// </summary>
		/// <remarks>
		/// This passes the click event to the control.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Control_Click(object sender, EventArgs e)
		{
			OnClick(e);
		}
	}
}
