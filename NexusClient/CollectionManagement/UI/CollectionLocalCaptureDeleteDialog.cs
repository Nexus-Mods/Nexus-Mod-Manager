using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Confirms deletion of one uniquely identified saved backup without authorizing installed-mod changes.</summary>
	internal sealed class CollectionLocalCaptureDeleteDialog : ManagedFontXtraForm
	{
		/// <summary>Creates a skinned confirmation with named actions and a scrollable exact backup identity.</summary>
		internal CollectionLocalCaptureDeleteDialog(CollectionManagementLocalCapture selected, DevExpressDisplaySettings settings)
		{
			if (selected == null) throw new ArgumentNullException(nameof(selected));
			if (settings == null) throw new ArgumentNullException(nameof(settings));
			Font = settings.Font;
			AutoScaleMode = AutoScaleMode.Font;
			Text = LanguageManager.Get("Collections.LocalDelete.Title", "Delete saved Local Collection");
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			ClientSize = new Size(590, 250);
			Padding = new Padding(12);

			MemoEdit message = new MemoEdit { Dock = DockStyle.Fill };
			message.Properties.ReadOnly = true;
			message.Properties.BorderStyle = BorderStyles.NoBorder;
			message.Text = LanguageManager.Format("Collections.LocalDelete.ConfirmMessage",
				"Delete this saved backup?\r\n\r\n{0}\r\n{1}\r\nBackup ID: {2}\r\n\r\nYou will no longer be able to restore this backup.\r\nYour installed mods and game files will stay as they are.",
				selected.DisplayName, selected.RevisionLabel, selected.CaptureIdentity);
			PanelControl buttons = new PanelControl { Dock = DockStyle.Bottom, Height = 42, BorderStyle = BorderStyles.NoBorder };
			SimpleButton cancel = new SimpleButton
			{
				Text = LanguageManager.Get("Common.Action.Cancel", "Cancel"), AutoSize = true,
				DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Padding = new Padding(8)
			};
			SimpleButton delete = new SimpleButton
			{
				Text = LanguageManager.Get("Collections.LocalDelete.Confirm", "Delete backup"), AutoSize = true,
				DialogResult = DialogResult.OK, Dock = DockStyle.Right, Padding = new Padding(8)
			};
			NmmIconProvider.BindDialogButton(delete, NmmIconAction.Delete);
			NmmIconProvider.BindDialogButton(cancel, NmmIconAction.Cancel);
			buttons.Controls.Add(delete);
			buttons.Controls.Add(cancel);
			Controls.Add(message);
			Controls.Add(buttons);
			AcceptButton = cancel;
			CancelButton = cancel;
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
		}
	}
}
