using System;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// A form that prompt the users to overwrite an item.
	/// </summary>
	public partial class OverwriteForm : ManagedFontXtraForm
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
			ApplyLocalization();
			NmmIconProvider.BindDialogButton(butYes, NmmIconAction.Apply);
			NmmIconProvider.BindDialogButton(butYesToAll, NmmIconAction.Apply);
			NmmIconProvider.BindDialogButton(butYesToGroup, NmmIconAction.Apply);
			NmmIconProvider.BindDialogButton(butYesToMod, NmmIconAction.Apply);
			NmmIconProvider.BindDialogButton(butNo, NmmIconAction.Cancel);
			NmmIconProvider.BindDialogButton(butNoToAll, NmmIconAction.Cancel);
			NmmIconProvider.BindDialogButton(butNoToGroup, NmmIconAction.Cancel);
			NmmIconProvider.BindDialogButton(butNoToMod, NmmIconAction.Cancel);
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

			LayoutActionButtons();
		}

		#endregion

		private void ApplyLocalization()
		{
			Text = LanguageManager.Get("Overwrite.Title", "Confirm Overwrite");
			butYesToAll.Text = LanguageManager.Get("Overwrite.YesToAll", "Yes to all");
			butYesToGroup.Text = LanguageManager.Get("Overwrite.YesToFolder", "Yes to folder");
			butYesToMod.Text = LanguageManager.Get("Overwrite.YesToMod", "Yes to Mod");
			butYes.Text = LanguageManager.Get("Common.Button.Yes", "Yes");
			butNoToAll.Text = LanguageManager.Get("Overwrite.NoToAll", "No to all");
			butNoToGroup.Text = LanguageManager.Get("Overwrite.NoToFolder", "No to folder");
			butNoToMod.Text = LanguageManager.Get("Overwrite.NoToMod", "No to Mod");
			butNo.Text = LanguageManager.Get("Common.Button.No", "No");
		}

		private void LayoutActionButtons()
		{
			SimpleButton[] buttons =
			{
				butYesToAll, butYesToGroup, butYesToMod, butYes,
				butNoToAll, butNoToGroup, butNoToMod, butNo
			};
			const int iconSize = 16;
			const int minimumButtonWidth = 75;
			const int buttonPadding = 20;
			const int spacing = 6;
			const int horizontalMargin = 12;

			int[] widths = new int[buttons.Length];
			int totalWidth = spacing * (buttons.Length - 1);
			for (int i = 0; i < buttons.Length; i++)
			{
				int textWidth = TextRenderer.MeasureText(
					buttons[i].Text ?? String.Empty,
					buttons[i].Font,
					System.Drawing.Size.Empty,
					TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
				widths[i] = Math.Max(minimumButtonWidth, textWidth + iconSize + buttonPadding);
				totalWidth += widths[i];
			}

			int targetWidth = Math.Max(670, totalWidth + horizontalMargin * 2);
			if (ClientSize.Width != targetWidth)
				ClientSize = new System.Drawing.Size(targetWidth, ClientSize.Height);

			int x = (targetWidth - totalWidth) / 2;
			for (int i = 0; i < buttons.Length; i++)
			{
				buttons[i].SetBounds(x, 4, widths[i], 23);
				x += widths[i] + spacing;
			}

			lblMessage.Width = Math.Max(100, targetWidth - 22);
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			LayoutActionButtons();
		}

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
			m_owrResult = (OverwriteResult)((SimpleButton)sender).Tag;
			Close();
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == (Keys.Control | Keys.C))
			{
				try
				{
					Clipboard.SetText(lblMessage.Text);
					return true;
				}
				catch { }
			}
			return base.ProcessCmdKey(ref msg, keyData);
		} 
	}
}
