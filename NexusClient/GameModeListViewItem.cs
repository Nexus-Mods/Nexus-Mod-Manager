using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Nexus.Client.Games;

namespace Nexus.Client
{
	public partial class GameModeListViewItem : UserControl, IGameModeListViewItem
	{
		[DllImport("gdi32.dll")]
		private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

		private PrivateFontCollection pfcFonts = new PrivateFontCollection();

		#region Properties

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

		protected override void OnClick(EventArgs e)
		{
			if (Parent is GameModeListView)
				((GameModeListView)Parent).Select(this);
			base.OnClick(e);
		}

		public void SetSelected(bool p_booIsSelected)
		{
			if (Parent is GameModeListView)
				((GameModeListView)Parent).Select(this);
			//BackColor = p_booIsSelected ? SystemColors.ControlDark : SystemColors.Control;
			BorderStyle = p_booIsSelected ? BorderStyle.FixedSingle : BorderStyle.None;
		}

		private void Control_Click(object sender, EventArgs e)
		{
			OnClick(e);
		}
	}
}
