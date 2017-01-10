using System;
using System.Drawing;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A message box with several extended features.
	/// </summary>
	/// <remarks>
	/// Among other added features is the ability to indicate if the last selection should be remembered, and
	/// a collapsable details pane.
	/// </remarks>
	public partial class ExtendedMessageBox : Form
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
			hlbDetails.Text = p_strDetails.Replace("\0", "\\0");

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

		private Int32 AddButtons(ExtendedMessageBoxButtons p_ebbButtons, bool p_booShowDetails)
		{
			Int32 intLastButtonLeft = pnlButtons.Right - 6;
			Int32 intMinimumWidth = 6;

			//details button
			if (p_booShowDetails && !m_booForceDetails)
			{
				DetailsButton butDetails = new DetailsButton();
				butDetails.AutoSize = true;
				butDetails.AutoSizeMode = AutoSizeMode.GrowAndShrink;
				butDetails.OpenText = "See details";
				butDetails.CloseText = "Hide details";
				butDetails.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
				butDetails.Location = new Point(pnlButtons.Left + 6, 12);
				butDetails.Click += new EventHandler(Details_Click);
				butDetails.TabIndex = 0;
				pnlButtons.Controls.Add(butDetails);
				intMinimumWidth += butDetails.Width + 12;
			}

			//cancel button
			if ((p_ebbButtons & ExtendedMessageBoxButtons.Cancel) == ExtendedMessageBoxButtons.Cancel)
			{
				Button butCancel = new Button();
				butCancel.Text = "Cancel";
				butCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butCancel.Location = new Point(intLastButtonLeft - butCancel.Width - 6, 12);
				butCancel.Click += new EventHandler(Button_Click);
				butCancel.Tag = DialogResult.Cancel;
				butCancel.TabIndex = 7;
				pnlButtons.Controls.Add(butCancel);
				intLastButtonLeft = butCancel.Left;
				this.CancelButton = butCancel;
				intMinimumWidth += butCancel.Width + 6;
			}

			//no button
			if ((p_ebbButtons & ExtendedMessageBoxButtons.No) == ExtendedMessageBoxButtons.No)
			{
				Button butNo = new Button();
				butNo.Text = "No";
				butNo.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butNo.Location = new Point(intLastButtonLeft - butNo.Width - 6, 12);
				butNo.Click += new EventHandler(Button_Click);
				butNo.Tag = DialogResult.No;
				butNo.TabIndex = 6;
				intLastButtonLeft = butNo.Left;
				pnlButtons.Controls.Add(butNo);
				if ((p_ebbButtons & ExtendedMessageBoxButtons.Cancel) != ExtendedMessageBoxButtons.Cancel)
					this.CancelButton = butNo;
				intMinimumWidth += butNo.Width + 6;
			}

			//yes button
			if ((p_ebbButtons & ExtendedMessageBoxButtons.Yes) == ExtendedMessageBoxButtons.Yes)
			{
				Button butYes = new Button();
				butYes.Text = "Yes";
				butYes.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butYes.Location = new Point(intLastButtonLeft - butYes.Width - 6, 12);
				butYes.Click += new EventHandler(Button_Click);
				butYes.Tag = DialogResult.Yes;
				butYes.TabIndex = 5;
				intLastButtonLeft = butYes.Left;
				pnlButtons.Controls.Add(butYes);
				this.AcceptButton = butYes;
				intMinimumWidth += butYes.Width + 6;
			}

			//ok button
			if ((p_ebbButtons & ExtendedMessageBoxButtons.OK) == ExtendedMessageBoxButtons.OK)
			{
				Button butOk = new Button();
				butOk.Text = "OK";
				butOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butOk.Location = new Point(intLastButtonLeft - butOk.Width - 6, 12);
				butOk.Click += new EventHandler(Button_Click);
				butOk.Tag = DialogResult.OK;
				butOk.TabIndex = 4;
				intLastButtonLeft = butOk.Left;
				pnlButtons.Controls.Add(butOk);
				this.AcceptButton = butOk;
				intMinimumWidth += butOk.Width + 6;
			}

			//ignore button
			if ((p_ebbButtons & ExtendedMessageBoxButtons.Ignore) == ExtendedMessageBoxButtons.Ignore)
			{
				Button butIgnore = new Button();
				butIgnore.Text = "Ignore";
				butIgnore.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butIgnore.Location = new Point(intLastButtonLeft - butIgnore.Width - 6, 12);
				butIgnore.Click += new EventHandler(Button_Click);
				butIgnore.Tag = DialogResult.Ignore;
				butIgnore.TabIndex = 3;
				intLastButtonLeft = butIgnore.Left;
				pnlButtons.Controls.Add(butIgnore);
				this.CancelButton = butIgnore;
				intMinimumWidth += butIgnore.Width + 6;
			}

			//retry button
			if ((p_ebbButtons & ExtendedMessageBoxButtons.Retry) == ExtendedMessageBoxButtons.Retry)
			{
				Button butRetry = new Button();
				butRetry.Text = "Retry";
				butRetry.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butRetry.Location = new Point(intLastButtonLeft - butRetry.Width - 6, 12);
				butRetry.Click += new EventHandler(Button_Click);
				butRetry.Tag = DialogResult.Retry;
				butRetry.TabIndex = 2;
				intLastButtonLeft = butRetry.Left;
				pnlButtons.Controls.Add(butRetry);
				this.AcceptButton = butRetry;
				intMinimumWidth += butRetry.Width + 6;
			}

			//abort button
			if ((p_ebbButtons & ExtendedMessageBoxButtons.Abort) == ExtendedMessageBoxButtons.Abort)
			{
				Button butAbort = new Button();
				butAbort.Text = "Abort";
				butAbort.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butAbort.Location = new Point(intLastButtonLeft - butAbort.Width - 6, 12);
				butAbort.Click += new EventHandler(Button_Click);
				butAbort.Tag = DialogResult.Abort;
				butAbort.TabIndex = 1;
				intLastButtonLeft = butAbort.Left;
				pnlButtons.Controls.Add(butAbort);
				this.AcceptButton = butAbort;
				intMinimumWidth += butAbort.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Backup) == ExtendedMessageBoxButtons.Backup)
			{
				Button butBackup = new Button();
				butBackup.Text = "Backup";
				butBackup.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butBackup.Location = new Point(intLastButtonLeft - butBackup.Width - 6, 12);
				butBackup.Click += new EventHandler(Button_Click);
				butBackup.Tag = DialogResult.Yes;
				butBackup.TabIndex = 1;
				intLastButtonLeft = butBackup.Left;
				pnlButtons.Controls.Add(butBackup);
				this.AcceptButton = butBackup;
				intMinimumWidth += butBackup.Width + 6;
			}

			if ((p_ebbButtons & ExtendedMessageBoxButtons.Update) == ExtendedMessageBoxButtons.Update)
			{
				Button butUpdate = new Button();
				butUpdate.Text = "Update";
				butUpdate.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
				butUpdate.Location = new Point(intLastButtonLeft - butUpdate.Width - 6, 12);
				butUpdate.Click += new EventHandler(Button_Click);
				butUpdate.Tag = DialogResult.No;
				butUpdate.TabIndex = 1;
				intLastButtonLeft = butUpdate.Left;
				pnlButtons.Controls.Add(butUpdate);
				this.AcceptButton = butUpdate;
				intMinimumWidth += butUpdate.Width + 6;
			}

			return intMinimumWidth;
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
				// Adding handler for when hlbDetails.Document.Body is null.
				// This is due to not being able to guarantee that the DocumentCompleted event has been run
				// Even if the body ends up being valid, the event often fires after this runs. Check for null.
				if (m_intMinimumDetailsHeight < 0 && (hlbDetails.Document.Body == null))
					m_intMinimumDetailsHeight = ClientSize.Height / 2;
				else if (m_intMinimumDetailsHeight < 0)
					m_intMinimumDetailsHeight = Math.Min(hlbDetails.Document.Body.ScrollRectangle.Height, ClientSize.Height / 2);
				if (LastDetailsHeight < 0)
					LastDetailsHeight = m_intMinimumDetailsHeight;
				pnlDetails.MinimumSize = new Size(0, m_intMinimumDetailsHeight);
				MaximumSize = new Size(Int32.MaxValue, Int32.MaxValue);
				Size = new Size(Size.Width, Size.Height + LastDetailsHeight);
				MinimumSize = new Size(MinimumSize.Width, MinimumSize.Height + m_intMinimumDetailsHeight + 40);
			}
			pnlDetails.Visible = !pnlDetails.Visible;
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
			DialogResult = (DialogResult)((Button)sender).Tag;
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == (Keys.Control | Keys.C))
			{
				try
				{
					Clipboard.SetText(albPrompt.Text + Environment.NewLine + Environment.NewLine + hlbDetails.Text);
					return true;
				}
				catch { }
			}
			return base.ProcessCmdKey(ref msg, keyData);
		} 
	}
}
