using System;
using System.Windows.Forms;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// A form that prompt the users to overwrite an item.
	/// </summary>
	public partial class OverwriteForm : Form
	{
		#region Properties

		/// <summary>
		/// The default constructor.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_booAllowGroup">Whether to display the
		/// "Yes to all in Group" and "No to all in Group" buttons.</param>
		private OverwriteForm(string p_strMessage, bool p_booAllowGroup)
		{
			InitializeComponent();
			lblMessage.Text = p_strMessage;
			if (!p_booAllowGroup)
			{
				butYesToGroup.Enabled = false;
				butNoToGroup.Enabled = false;
			}

			butNo.Tag = OverwriteResult.No;
			butNoToAll.Tag = OverwriteResult.NoToAll;
			butNoToGroup.Tag = OverwriteResult.NoToGroup;
			butYes.Tag = OverwriteResult.Yes;
			butYesToAll.Tag = OverwriteResult.YesToAll;
			butYesToGroup.Tag = OverwriteResult.YesToGroup;

		}

		#endregion

		private OverwriteResult m_owrResult;

		/// <summary>
		/// Displays the overwrite form.
		/// </summary>
		/// <param name="p_winOwner">The window to use as the owner of the form.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_booAllowGroup">Whether to display the
		/// "Yes to all in Group" and "No to all in Group" buttons.</param>
		/// <returns>The selected result.</returns>
		public static OverwriteResult ShowDialog(IWin32Window p_winOwner, string p_strMessage, bool p_booAllowGroup)
		{
			OverwriteForm of = new OverwriteForm(p_strMessage, p_booAllowGroup);
			of.ShowDialog(p_winOwner);
			return of.m_owrResult;
		}

		/// <summary>
		/// Displays the overwrite form.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_booAllowGroup">Whether to display the
		/// "Yes to all in Group" and "No to all in Group" buttons.</param>
		/// <returns>The selected result.</returns>
		public static OverwriteResult ShowDialog(string p_strMessage, bool p_booAllowGroup)
		{
			OverwriteForm of = new OverwriteForm(p_strMessage, p_booAllowGroup);
			of.ShowDialog();
			return of.m_owrResult;
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> events of the buttons.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Button_Click(object sender, EventArgs e)
		{
			m_owrResult = (OverwriteResult)((Button)sender).Tag;
			Close();
		}
	}
}
