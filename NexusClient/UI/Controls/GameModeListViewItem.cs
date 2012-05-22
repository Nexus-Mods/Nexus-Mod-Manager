using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nexus.Client.Games;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// A list view item that displays info about a game mode.
	/// </summary>
	public partial class GameModeListViewItem : GameModeListViewItemBase
	{
		[DllImport("gdi32.dll")]
		private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

		private PrivateFontCollection pfcFonts = new PrivateFontCollection();

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameModeInfo">The descriptor for the game mode whose install path is to be found.</param>
		public GameModeListViewItem(IGameModeDescriptor p_gmdGameModeInfo)
			: base(p_gmdGameModeInfo)
		{
			InitializeComponent();
			TabStop = false;

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
			if (GameMode.ModeTheme.Icon != null)
				pbxGameLogo.Image = new Icon(GameMode.ModeTheme.Icon, 48, 48).ToBitmap();
		}

		#endregion

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
