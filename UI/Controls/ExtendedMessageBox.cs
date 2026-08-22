using System;
using System.Drawing;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.Util.Localization;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A message box with several extended features.
	/// </summary>
	/// <remarks>
	/// Among other added features is the ability to indicate if the last selection should be remembered, and
	/// a collapsable details pane.
	/// </remarks>
	public partial class ExtendedMessageBox : XtraForm
	{
		#region Show Methods

		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_mbbButtons">The buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		public static DialogResult Show(Control p_ctlParent, string p_strMessage, string p_strCaption, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon)
		{
			return Show(p_ctlParent, p_strMessage, p_strCaption, null, p_mbbButtons, p_mbiIcon);
		}


		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_mbbButtons">The buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		/// <param name="p_booRemember">Indicates whether the selected button should be remembered.</param>
		public static DialogResult Show(Control p_ctlParent, string p_strMessage, string p_strCaption, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon, out bool p_booRemember)
		{
			return Show(p_ctlParent, p_strMessage, p_strCaption, null, p_mbbButtons, p_mbiIcon, out p_booRemember);
		}

		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_strDetails">The HTML-formatted details to display.</param>
		/// <param name="p_mbbButtons">The buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		public static DialogResult Show(Control p_ctlParent, string p_strMessage, string p_strCaption, string p_strDetails, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon)
		{
			ExtendedMessageBox mbxBox = new ExtendedMessageBox();
			mbxBox.Init(p_strMessage, p_strCaption, p_strDetails, p_mbbButtons, p_mbiIcon, false);
			return Show(mbxBox, p_ctlParent);
		}

		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_strDetails">The HTML-formatted details to display.</param>
		/// <param name="p_mbbButtons">The buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		/// <param name="p_booRemember">Indicates whether the selected button should be remembered.</param>
		public static DialogResult Show(Control p_ctlParent, string p_strMessage, string p_strCaption, string p_strDetails, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon, out bool p_booRemember)
		{
			ExtendedMessageBox mbxBox = new ExtendedMessageBox();
			mbxBox.Init(p_strMessage, p_strCaption, p_strDetails, p_mbbButtons, p_mbiIcon, true);
			DialogResult drsResult = Show(mbxBox, p_ctlParent);
			p_booRemember = mbxBox.RememberSelection;
			return drsResult;
		}

		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_strDetails">The HTML-formatted details to display.</param>
		/// <param name="p_ebbButtons">The extended buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		public static DialogResult Show(Control p_ctlParent, string p_strMessage, string p_strCaption, string p_strDetails, ExtendedMessageBoxButtons p_ebbButtons, MessageBoxIcon p_mbiIcon)
		{
			ExtendedMessageBox mbxBox = new ExtendedMessageBox();
			mbxBox.Init(p_strMessage, p_strCaption, p_strDetails, p_ebbButtons, p_mbiIcon, false);
			return Show(mbxBox, p_ctlParent);
		}

		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_strDetails">The HTML-formatted details to display.</param>
		/// <param name="p_ebbButtons">The extended buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		/// <param name="p_booRemember">Indicates whether the selected button should be remembered.</param>
		public static DialogResult Show(Control p_ctlParent, string p_strMessage, string p_strCaption, string p_strDetails, ExtendedMessageBoxButtons p_ebbButtons, MessageBoxIcon p_mbiIcon, out bool p_booRemember)
		{
			ExtendedMessageBox mbxBox = new ExtendedMessageBox();
			mbxBox.Init(p_strMessage, p_strCaption, p_strDetails, p_ebbButtons, p_mbiIcon, true);
			DialogResult drsResult = Show(mbxBox, p_ctlParent);
			p_booRemember = mbxBox.RememberSelection;
			return drsResult;
		}

		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_strDetails">The HTML-formatted details to display.</param>
		/// <param name="p_intMinWidth">The minimum width of the message box.</param>
		/// <param name="p_intDetailHeight">The initial height of the details section.</param>
		/// <param name="p_mbbButtons">The buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		public static DialogResult Show(Control p_ctlParent, string p_strMessage, string p_strCaption, string p_strDetails, Int32 p_intMinWidth, Int32 p_intDetailHeight, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon)
		{
			ExtendedMessageBox mbxBox = new ExtendedMessageBox();
			mbxBox.MinimumSize = new Size(p_intMinWidth, mbxBox.MinimumSize.Height);
			mbxBox.LastDetailsHeight = p_intDetailHeight;
			mbxBox.Init(p_strMessage, p_strCaption, p_strDetails, p_mbbButtons, p_mbiIcon, false, true);
			return Show(mbxBox, p_ctlParent);
		}


		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_strDetails">The HTML-formatted details to display.</param>
		/// <param name="p_intMinWidth">The minimum width of the message box.</param>
		/// <param name="p_intDetailHeight">The initial height of the details section.</param>
		/// <param name="p_mbbButtons">The buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		public static DialogResult Show(Control p_ctlParent, string p_strMessage, string p_strCaption, string p_strDetails, Int32 p_intMinWidth, Int32 p_intDetailHeight, ExtendedMessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon)
		{
			ExtendedMessageBox mbxBox = new ExtendedMessageBox();
			mbxBox.MinimumSize = new Size(p_intMinWidth, mbxBox.MinimumSize.Height);
			mbxBox.LastDetailsHeight = p_intDetailHeight;
			mbxBox.Init(p_strMessage, p_strCaption, p_strDetails, p_mbbButtons, p_mbiIcon, false, true);
			return Show(mbxBox, p_ctlParent);
		}

		/// <summary>
		/// Shows the message box.
		/// </summary>
		/// <param name="p_ctlParent">The parent of the message box.</param>
		/// <param name="p_mbxBox">The dialog to display.</param>
		protected static DialogResult Show(ExtendedMessageBox p_mbxBox, Control p_ctlParent)
		{
			DialogResult drsResult = DialogResult.OK;

			if (p_ctlParent == null)
				drsResult = p_mbxBox.ShowDialog();
			else
				drsResult = p_mbxBox.ShowDialog(p_ctlParent);
			return drsResult;
		}

		#endregion

		private Int32 m_intMinimumDetailsHeight = -1;
		private bool m_booForceDetails = false;
		private SimpleButton m_butDetails;
		private string m_strDetailsText = String.Empty;
		private readonly string m_strSeeDetailsText;
		private readonly string m_strHideDetailsText;

		#region Properties

		/// <summary>
		/// Gets whether the remember selection checkbox is checked.
		/// </summary>
		/// <value>Whether the remember selection checkbox is checked.</value>
		public bool RememberSelection
		{
			get
			{
				return cbxRemember.Checked;
			}
		}

		/// <summary>
		/// Gets or sets the previous height of the details section of the message box.
		/// </summary>
		/// <value>The previous height of the details section of the message box.</value>
		public Int32 LastDetailsHeight { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		protected ExtendedMessageBox()
		{
			InitializeComponent();
			m_strSeeDetailsText = LanguageManager.Get("Common.MessageBox.SeeDetails.Name", "See details");
			m_strHideDetailsText = LanguageManager.Get("Common.MessageBox.HideDetails.Name", "Hide details");
			cbxRemember.Properties.Caption = LanguageManager.Get("Common.MessageBox.RememberSelection.Option", "Remember my selection");
			LastDetailsHeight = -1;
			this.Shown += new EventHandler(Form_Shown);
		}

		#endregion

		/// <summary>
		/// Sets up the form.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_strDetails">The HTML-formatted details.</param>
		/// <param name="p_mbbButtons">The buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		/// <param name="p_booShowRemember">Whether to display the remember selection checkbox.</param>
		protected void Init(string p_strMessage, string p_strCaption, string p_strDetails, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon, bool p_booShowRemember, bool p_booForceDetails = false)
		{
			ExtendedMessageBoxButtons ebbButtons = ExtendedMessageBoxButtons.None;
			switch (p_mbbButtons)
			{
				case MessageBoxButtons.AbortRetryIgnore:
					ebbButtons = ExtendedMessageBoxButtons.Abort | ExtendedMessageBoxButtons.Retry | ExtendedMessageBoxButtons.Ignore;
					break;
				case MessageBoxButtons.OK:
					ebbButtons = ExtendedMessageBoxButtons.OK;
					break;
				case MessageBoxButtons.OKCancel:
					ebbButtons = ExtendedMessageBoxButtons.OK | ExtendedMessageBoxButtons.Cancel;
					break;
				case MessageBoxButtons.RetryCancel:
					ebbButtons = ExtendedMessageBoxButtons.Retry | ExtendedMessageBoxButtons.Cancel;
					break;
				case MessageBoxButtons.YesNo:
					ebbButtons = ExtendedMessageBoxButtons.Yes | ExtendedMessageBoxButtons.No;
					break;
				case MessageBoxButtons.YesNoCancel:
					ebbButtons = ExtendedMessageBoxButtons.Yes | ExtendedMessageBoxButtons.No | ExtendedMessageBoxButtons.Cancel;
					break;
			}

			m_booForceDetails = p_booForceDetails;

			Init(p_strMessage, p_strCaption, p_strDetails, ebbButtons, p_mbiIcon, p_booShowRemember);
		}

		protected void Init(string p_strMessage, string p_strCaption, string p_strDetails, ExtendedMessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon, bool p_booShowRemember, bool p_booForceDetails = false)
		{
			ExtendedMessageBoxButtons ebbButtons = ExtendedMessageBoxButtons.None;
			
			ebbButtons = ExtendedMessageBoxButtons.Update | ExtendedMessageBoxButtons.Backup;
			
			m_booForceDetails = p_booForceDetails;

			Init(p_strMessage, p_strCaption, p_strDetails, ebbButtons, p_mbiIcon, p_booShowRemember);
		}

		/// <summary>
		/// Sets up the form.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The windows title.</param>
		/// <param name="p_strDetails">The HTML-formatted details.</param>
		/// <param name="p_ebbButtons">The extended buttons to display.</param>
		/// <param name="p_mbiIcon">The icon to display.</param>
		/// <param name="p_booShowRemember">Whether to display the remember selection checkbox.</param>
		protected void Init(string p_strMessage, string p_strCaption, string p_strDetails, ExtendedMessageBoxButtons p_ebbButtons, MessageBoxIcon p_mbiIcon, bool p_booShowRemember)
		{
			if (p_strDetails == null)
				p_strDetails = "";
			bool booShowIcon = true;
			switch (p_mbiIcon)
			{
				case MessageBoxIcon.Information:
					pbxIcon.Image = Bitmap.FromHicon(SystemIcons.Information.Handle);
					break;
				case MessageBoxIcon.Error:
					pbxIcon.Image = Bitmap.FromHicon(SystemIcons.Error.Handle);
					break;
				case MessageBoxIcon.Warning:
					pbxIcon.Image = Bitmap.FromHicon(SystemIcons.Warning.Handle);
					break;
				case MessageBoxIcon.Question:
					pbxIcon.Image = Bitmap.FromHicon(SystemIcons.Question.Handle);
					break;
				case MessageBoxIcon.None:
					booShowIcon = false;
					break;
			}
			if (booShowIcon)
			{
				pbxIcon.MinimumSize = new Size(pbxIcon.Padding.Left + pbxIcon.Padding.Right + pbxIcon.Image.Width, pbxIcon.Padding.Top + pbxIcon.Padding.Bottom + pbxIcon.Image.Height);
				pbxIcon.MaximumSize = new Size(pbxIcon.Padding.Left + pbxIcon.Padding.Right + pbxIcon.Image.Width, pbxIcon.Padding.Top + pbxIcon.Padding.Bottom + pbxIcon.Image.Height);
				pnlMessage.MinimumSize = new Size(0, pbxIcon.MinimumSize.Height);
			}
			pbxIcon.Visible = booShowIcon;
			pnlRemember.Visible = p_booShowRemember;
			pnlDetails.Visible = false;
			m_strDetailsText = p_strDetails.Replace("\0", "\\0");
			SetDetailsContent(m_strDetailsText);

			Text = p_strCaption;

			albPrompt.Text = p_strMessage;

			Int32 intBorderWidth = Size.Width - ClientSize.Width;
			Int32 intMaxWindowClientWidth = ((MaximumSize.Width > 0) ? MaximumSize.Width : Int32.MaxValue) - intBorderWidth;
			Int32 intMaxLabelWidth = intMaxWindowClientWidth;
			if (booShowIcon)
				intMaxLabelWidth -= pbxIcon.MinimumSize.Width;

			Graphics gphGraphics = albPrompt.CreateGraphics();
			SizeF szeTextSize = gphGraphics.MeasureString(albPrompt.Text, albPrompt.Font, intMaxLabelWidth);
			if (booShowIcon)
			{
				Int32 intLabelPadding = (pbxIcon.MinimumSize.Height - (Int32)Math.Ceiling(szeTextSize.Height)) / 2;
				if (intLabelPadding > pnlLabel.Padding.Top)
					pnlLabel.Padding = new Padding(pnlLabel.Padding.Left, intLabelPadding, pnlLabel.Padding.Right, 0);
			}

			Int32 intWindowClientWidth = (Int32)Math.Ceiling(szeTextSize.Width) + pnlLabel.Padding.Left + pnlLabel.Padding.Right + (booShowIcon ? pbxIcon.MinimumSize.Width : 0);
			if (intWindowClientWidth > intMaxWindowClientWidth)
				intWindowClientWidth = intMaxWindowClientWidth;
			if (intWindowClientWidth + intBorderWidth < MinimumSize.Width)
				intWindowClientWidth = MinimumSize.Width - intBorderWidth;

			Int32 intMinimumWidth = AddButtons(p_ebbButtons, !String.IsNullOrEmpty(p_strDetails));
			if (intWindowClientWidth < intMinimumWidth)
				intWindowClientWidth = intMinimumWidth;

			Int32 intWindowClientHeight = (Int32)Math.Max((booShowIcon ? pbxIcon.MinimumSize.Height : 0), Math.Ceiling(szeTextSize.Height + pnlLabel.Padding.Top + pnlLabel.Padding.Bottom)) + (p_booShowRemember ? pnlRemember.Height : 0) + pnlButtons.Height;

			Int32 intBorderHeight = Size.Height - ClientSize.Height;
			MinimumSize = new Size(intWindowClientWidth + intBorderWidth, intWindowClientHeight + intBorderHeight);
			MaximumSize = new Size(Int32.MaxValue, MinimumSize.Height);
		}

		/// <summary>
		/// Adds the requested DevExpress dialog buttons and returns the minimum width they require.
		/// </summary>
		private Int32 AddButtons(ExtendedMessageBoxButtons p_ebbButtons, bool p_booShowDetails)
		{
			Int32 intLastButtonLeft = pnlButtons.Right - 6;
			Int32 intMinimumWidth = 6;

			if (p_booShowDetails && !m_booForceDetails)
			{
				m_butDetails = new SimpleButton();
				m_butDetails.Text = m_strSeeDetailsText;
				m_butDetails.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
				m_butDetails.Location = new Point(pnlButtons.Left + 6, 12);
				m_butDetails.Size = new Size(92, 23);
				m_butDetails.Click += Details_Click;
				m_butDetails.TabIndex = 0;
				pnlButtons.Controls.Add(m_butDetails);
				intMinimumWidth += m_butDetails.Width + 12;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Cancel) == ExtendedMessageBoxButtons.Cancel)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.Cancel", "Cancel"), DialogResult.Cancel, 7, ref intLastButtonLeft);
				CancelButton = button;
				intMinimumWidth += button.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.No) == ExtendedMessageBoxButtons.No)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.No", "No"), DialogResult.No, 6, ref intLastButtonLeft);
				if ((p_ebbButtons & ExtendedMessageBoxButtons.Cancel) != ExtendedMessageBoxButtons.Cancel)
					CancelButton = button;
				intMinimumWidth += button.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Yes) == ExtendedMessageBoxButtons.Yes)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.Yes", "Yes"), DialogResult.Yes, 5, ref intLastButtonLeft);
				AcceptButton = button;
				intMinimumWidth += button.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.OK) == ExtendedMessageBoxButtons.OK)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.Ok", "OK"), DialogResult.OK, 4, ref intLastButtonLeft);
				AcceptButton = button;
				intMinimumWidth += button.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Ignore) == ExtendedMessageBoxButtons.Ignore)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.Ignore", "Ignore"), DialogResult.Ignore, 3, ref intLastButtonLeft);
				CancelButton = button;
				intMinimumWidth += button.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Retry) == ExtendedMessageBoxButtons.Retry)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.Retry", "Retry"), DialogResult.Retry, 2, ref intLastButtonLeft);
				AcceptButton = button;
				intMinimumWidth += button.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Abort) == ExtendedMessageBoxButtons.Abort)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.Abort", "Abort"), DialogResult.Abort, 1, ref intLastButtonLeft);
				AcceptButton = button;
				intMinimumWidth += button.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Backup) == ExtendedMessageBoxButtons.Backup)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.Backup", "Backup"), DialogResult.Yes, 1, ref intLastButtonLeft);
				AcceptButton = button;
				intMinimumWidth += button.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Update) == ExtendedMessageBoxButtons.Update)
			{
				SimpleButton button = AddDialogButton(LanguageManager.Get("Common.Action.Update", "Update"), DialogResult.No, 1, ref intLastButtonLeft);
				AcceptButton = button;
				intMinimumWidth += button.Width + 6;
			}

			return intMinimumWidth;
		}

		/// <summary>
		/// Adds a single skin-aware action button to the dialog button panel.
		/// </summary>
		private SimpleButton AddDialogButton(string text, DialogResult result, int tabIndex, ref int lastButtonLeft)
		{
			SimpleButton button = new SimpleButton();
			button.Text = text;
			button.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			button.Size = new Size(75, 23);
			button.Location = new Point(lastButtonLeft - button.Width - 6, 12);
			button.Click += Button_Click;
			button.Tag = result;
			button.TabIndex = tabIndex;
			pnlButtons.Controls.Add(button);
			lastButtonLeft = button.Left;
			return button;
		}

		/// <summary>
		/// Loads details into the DevExpress HTML viewer while preserving plain-text line breaks.
		/// </summary>
		private void SetDetailsContent(string details)
		{
			string content = details ?? String.Empty;
			if (!Regex.IsMatch(content, @"<[^>]+>"))
				content = WebUtility.HtmlEncode(content).Replace("\r\n", "<br/>").Replace("\r", "<br/>").Replace("\n", "<br/>").Replace("\t", "&nbsp;&nbsp;&nbsp;&nbsp;");
			content = content.Replace("${", "&#36;{");
			hlbDetails.HtmlTemplate.Set("<div class=\"details\">" + content + "</div>", ".details { padding: 6px; }");
			hlbDetails.Refresh();
		}

		/// <summary>
		/// Hides or unhides the details panel.
		/// </summary>
		private void ToggleDetails()
		{
			if (pnlDetails.Visible)
			{
				pnlDetails.MinimumSize = new Size(0, 0);
				MinimumSize = new Size(MinimumSize.Width, MinimumSize.Height - m_intMinimumDetailsHeight);
				//because the details panel is docked FULL, we can't set the panel's height directly
				// instead, we have to resize the window by an amount sufficient to make the
				// panel the desired size.
				// as such, it is more accurate to calculate the panel's height by calculating
				// the change in the window size; this will factor in any padding that may be
				// present.
				LastDetailsHeight = Size.Height - MinimumSize.Height;
				MaximumSize = new Size(Int32.MaxValue, MinimumSize.Height);
			}
			else
			{
				if (m_intMinimumDetailsHeight < 0)
					m_intMinimumDetailsHeight = Math.Max(120, ClientSize.Height / 2);
				if (LastDetailsHeight < 0)
					LastDetailsHeight = m_intMinimumDetailsHeight;
				pnlDetails.MinimumSize = new Size(0, m_intMinimumDetailsHeight);
				MaximumSize = new Size(Int32.MaxValue, Int32.MaxValue);
				Size = new Size(Size.Width, Size.Height + LastDetailsHeight);
				MinimumSize = new Size(MinimumSize.Width, MinimumSize.Height + m_intMinimumDetailsHeight + 40);
			}
			pnlDetails.Visible = !pnlDetails.Visible;
			if (m_butDetails != null)
				m_butDetails.Text = pnlDetails.Visible ? m_strHideDetailsText : m_strSeeDetailsText;
			this.PerformLayout();
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the show details button.
		/// </summary>
		/// <remarks>
		/// This shows or hides the details pane as appropriate.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event properties.</param>
		private void Form_Shown(object sender, EventArgs e)
		{
			if (m_booForceDetails)
			{
				ToggleDetails();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the show details button.
		/// </summary>
		/// <remarks>
		/// This shows or hides the details pane as appropriate.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event properties.</param>
		private void Details_Click(object sender, EventArgs e)
		{
			ToggleDetails();
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the message box's buttons.
		/// </summary>
		/// <remarks>
		/// This set the appropriate <see cref="DialogResult"/>.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event properties.</param>
		private void Button_Click(object sender, EventArgs e)
		{
			DialogResult = (DialogResult)((SimpleButton)sender).Tag;
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == (Keys.Control | Keys.C))
			{
				try
				{
					Clipboard.SetText(albPrompt.Text + Environment.NewLine + Environment.NewLine + m_strDetailsText);
					return true;
				}
				catch { }
			}
			return base.ProcessCmdKey(ref msg, keyData);
		} 
	}
}
