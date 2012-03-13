using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client
{
	/// <summary>
	/// Selects the game for which mods will be managed.
	/// </summary>
	public partial class GameModeSelectionForm : Form
	{
		#region Properties

		/// <summary>
		/// Gets the id of the selected game mode.
		/// </summary>
		/// <value>The id of the selected game mode.</value>
		public string SelectedGameModeId
		{
			get
			{

				throw new NotImplementedException();
			}
		}

		/// <summary>
		/// Gets or sets the application's user settings.
		/// </summary>
		/// <value>The application's user settings.</value>
		protected ISettings Settings { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		/// <param name="p_lstGameModes">The list of avaliable game modes.</param>
		/// <param name="p_setSettings">The application settings.</param>
		public GameModeSelectionForm(IList<IGameModeDescriptor> p_lstGameModes, ISettings p_setSettings)
		{
			Settings = p_setSettings;
			InitializeComponent();
			Icon = Properties.Resources.DefaultIcon;
			List<IGameModeDescriptor> lstSortedModes = new List<IGameModeDescriptor>(p_lstGameModes);
			lstSortedModes.Sort((x, y) => x.Name.CompareTo(y.Name));
			foreach (IGameModeDescriptor gmdInfo in lstSortedModes)
			{
				GameModeListViewItem gliGameModeItem = new GameModeListViewItem(gmdInfo);
				gameModeListView1.Controls.Add(gliGameModeItem);
			}
			
			IGameModeDescriptor gmdDefault = p_lstGameModes.Find(x => x.ModeId.Equals(p_setSettings.RememberedGameMode));
			/*if (gmdDefault != null)
				cbxGameMode.SelectedValue = gmdDefault.ModeId;*/
			cbxRemember.Checked = Settings.RememberGameMode;
		}

		#endregion

		/// <summary>
		/// Hanldes the <see cref="Control.Click"/> event of the OK button.
		/// </summary>
		/// <remarks>
		/// This makes the mod manager remember the selected game, if requested.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butOK_Click(object sender, EventArgs e)
		{
			Settings.RememberGameMode = cbxRemember.Checked;
			Settings.RememberedGameMode = SelectedGameModeId;
			Settings.Save();
			DialogResult = DialogResult.OK;
		}

		/// <summary>
		/// /// Hanldes the <see cref="ComboBox.SelectedIndexChanged"/> event of the game mode
		/// selection box.
		/// </summary>
		/// <remarks>
		/// This changes the icon to refect the currently selected game mode.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void cbxGameMode_SelectedIndexChanged(object sender, EventArgs e)
		{
			throw new NotImplementedException();
			//Icon = ((IGameModeDescriptor)cbxGameMode.SelectedItem).ModeTheme.Icon;
		}
	}
}
