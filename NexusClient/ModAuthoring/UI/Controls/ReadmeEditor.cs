using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client.Mods;

namespace Nexus.Client.ModAuthoring.UI.Controls
{
	/// <summary>
	/// An editor for <see cref="Readme"/> files.
	/// </summary>
	public partial class ReadmeEditor : UserControl
	{
		#region Properties

		/// <summary>
		/// Gets or sets the <see cref="Readme"/> being edited.
		/// </summary>
		/// <value>The <see cref="Readme"/> being edited.</value>
		[Bindable(true)]
		public Readme Readme
		{
			get
			{
				Readme rmeReadme = new Readme(ReadmeFormat.PlainText, null);
				if (ddtReadme.SelectedTabPage == ddpPlainText)
				{
					rmeReadme.Format = ReadmeFormat.PlainText;
					rmeReadme.Text = tbxReadme.Text;
				}
				else if (ddtReadme.SelectedTabPage == ddpRichText)
				{
					rmeReadme.Format = ReadmeFormat.RichText;
					rmeReadme.Text = rteReadme.Rtf;
				}
				else if (ddtReadme.SelectedTabPage == ddpHTML)
				{
					rmeReadme.Format = ReadmeFormat.HTML;
					rmeReadme.Text = xedReadme.Text;
				}
				return String.IsNullOrEmpty(rmeReadme.Text) ? null : rmeReadme;
			}
			set
			{
				if (value == null)
				{
					ddtReadme.SelectedTabPage = ddpPlainText;
					tbxReadme.Text = null;
				}
				else
				{
					switch (value.Format)
					{
						case ReadmeFormat.PlainText:
							ddtReadme.SelectedTabPage = ddpPlainText;
							tbxReadme.Font = new Font(FontFamily.GenericMonospace, tbxReadme.Font.Size, tbxReadme.Font.Style);
							tbxReadme.Text = value.Text;
							break;
						case ReadmeFormat.RichText:
							ddtReadme.SelectedTabPage = ddpRichText;
							try
							{
								rteReadme.Rtf = value.Text;
							}
							catch
							{
								rteReadme.Text = value.Text;
							}
							break;
						case ReadmeFormat.HTML:
							ddtReadme.SelectedTabPage = ddpHTML;
							xedReadme.Text = value.Text;
							break;
						default:
							throw new InvalidEnumArgumentException("Unrecognized value for ReadmeFormat enum.");
					}
				}
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ReadmeEditor()
		{
			InitializeComponent();

			xedReadme.SetHighlighting("HTML");
		}

		#endregion

		/// <summary>
		/// Shows a preview of the HTML readme.
		/// </summary>
		protected void ShowHTMLPreview()
		{
			Form frmHTMLPreview = new Form();
			WebBrowser wbrBrowser = new WebBrowser();
			frmHTMLPreview.Controls.Add(wbrBrowser);
			wbrBrowser.Dock = DockStyle.Fill;
			wbrBrowser.DocumentCompleted += delegate(object o, WebBrowserDocumentCompletedEventArgs arg)
			{
				frmHTMLPreview.Text = String.IsNullOrEmpty(wbrBrowser.DocumentTitle) ? "Readme" : wbrBrowser.DocumentTitle;
			};
			wbrBrowser.WebBrowserShortcutsEnabled = false;
			wbrBrowser.AllowWebBrowserDrop = false;
			wbrBrowser.AllowNavigation = false;
			wbrBrowser.DocumentText = xedReadme.Text;
			frmHTMLPreview.ShowDialog(this.FindForm());
		}

		/// <summary>
		/// Hanldes the <see cref="Control.Click"/> event of the PReview menu item.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbPreview_Click(object sender, EventArgs e)
		{
			ShowHTMLPreview();
		}

		/// <summary>
		/// Handles the <see cref="Control.KeyPress"/> event of the plain text readme textbox.
		/// </summary>
		/// <remarks>
		/// This selects all text when Ctrl-A is pressed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="KeyPressEventArgs"/> describing the event arguments.</param>
		private void tbxReadme_KeyPress(object sender, KeyPressEventArgs e)
		{
			//character 1 is equivalent to Ctrl-A
			if (e.KeyChar == '\x01')
			{
				((TextBox)sender).SelectAll();
				e.Handled = true;
			}
		}
	}
}
