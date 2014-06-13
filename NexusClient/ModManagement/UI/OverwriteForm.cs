using System;
using System.Windows.Forms;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// A form that prompt the users to overwrite an item.
	/// </summary>
	public partial class OverwriteForm : ManagedFontForm
	{
		#region Properties

		/// <summary>
		/// The default constructor.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_booAllowGroup">Whether to display the
		/// "Yes to all in Group" and "No to all in Group" buttons.</param>
		/// <param name="p_booAllowMod">Whether to display the
		/// "Yes to all in Mod" and "No to all in Mod" buttons.</param>
		private OverwriteForm(string p_strMessage, bool p_booAllowGroup, bool p_booAllowMod)
		{
			InitializeComponent();
			lblMessage.Text = p_strMessage;
			if (!p_booAllowGroup)
			{
				butYesToGroup.Enabled = false;
				butNoToGroup.Enabled = false;
			}
			if (!p_booAllowMod)
			{
				butYesToMod.Enabled = false;
				butNoToMod.Enabled = false;
			}

			butNo.Tag = OverwriteResult.No;
			butNoToAll.Tag = OverwriteResult.NoToAll;
			butNoToGroup.Tag = OverwriteResult.NoToGroup;
			butNoToMod.Tag = OverwriteResult.NoToMod;
			butYes.Tag = OverwriteResult.Yes;
			butYesToAll.Tag = OverwriteResult.YesToAll;
			butYesToGroup.Tag = OverwriteResult.YesToGroup;
			butYesToMod.Tag = OverwriteResult.YesToMod;

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
		/// <param name="p_booAllowMod">Whether to display the
		/// "Yes to all in Mod" and "No to all in Mod" buttons.</param>
		/// <returns>The selected result.</returns>
		public static OverwriteResult ShowDialog(IWin32Window p_winOwner, string p_strMessage, bool p_booAllowGroup, bool p_booAllowMod)
		{
			OverwriteForm of = new OverwriteForm(p_strMessage, p_booAllowGroup, p_booAllowMod);
			string strFont = of.Font.FontFamily.ToString();
			of.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
			of.Font = new System.Drawing.Font(strFont, 10.95F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel, ((byte)(0)));
			of.ShowDialog(p_winOwner);
			return of.m_owrResult;
		}

		/// <summary>
		/// Displays the overwrite form.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_booAllowGroup">Whether to display the
		/// "Yes to all in Group" and "No to all in Group" buttons.</param>
		/// <param name="p_booAllowMod">Whether to display the
		/// "Yes to all in Mod" and "No to all in Mod" buttons.</param>
		/// <returns>The selected result.</returns>
		public static OverwriteResult ShowDialog(string p_strMessage, bool p_booAllowGroup, bool p_booAllowMod)
		{
			OverwriteForm of = new OverwriteForm(p_strMessage, p_booAllowGroup, p_booAllowMod);
			string strFont = of.Font.FontFamily.ToString();
			of.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
			of.Font = new System.Drawing.Font(strFont, 10.95F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel, ((byte)(0)));
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
