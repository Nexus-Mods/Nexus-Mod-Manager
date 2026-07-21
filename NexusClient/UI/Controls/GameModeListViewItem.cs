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

			lblGameModeName.Text = GameMode.Name;
			lblGameModeName.ForeColor = GameMode.ModeTheme.PrimaryColour;
			if (GameMode.ModeTheme.Icon != null)
				pbxGameLogo.Image = new Icon(GameMode.ModeTheme.Icon, 32, 32).ToBitmap();
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

		/// <summary>
		/// Handles the <see cref="Control.MouseDoubleClick"/> event of all contained controls.
		/// </summary>
		/// <remarks>
		/// This passes the click event to the control.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="MouseEventArgs"/> describing the event arguments.</param>
		private void lblGameModeName_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			OnMouseDoubleClick(e);
		}
	}
}
