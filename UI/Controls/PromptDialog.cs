using System;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A dialog that prompts the user for text.
	/// </summary>
	public partial class PromptDialog : Form
	{
		/// <summary>
		/// Displays a prompt dialog with the given message.
		/// </summary>
		/// <param name="p_wndOwner">The owner of the prompt window.</param>
		/// <param name="p_strPrompt">The prompt message.</param>
		/// <param name="p_strCaption">The title of the dialog window.</param>
		/// <param name="p_strDefault">The default prompted text.</param>
		/// <param name="p_strValidationPattern">The regular expression to use to validate the entered text.</param>
		/// <param name="p_strErrorMessage">The error message to display if the entered text fails validation.</param>
		/// <returns>The entered prompted text, or <c>null</c> if the user
		/// cancelled the dialog.</returns>
		public static string ShowDialog(IWin32Window p_wndOwner, string p_strPrompt, string p_strCaption, string p_strDefault, string p_strValidationPattern, string p_strErrorMessage)
		{
			PromptDialog dlgPrompt = new PromptDialog();
			dlgPrompt.Text = p_strCaption;
			dlgPrompt.EnteredText = p_strDefault;
			dlgPrompt.Prompt = p_strPrompt;
			dlgPrompt.ValidationPattern = p_strValidationPattern;
			dlgPrompt.ValidationErrorMessage = p_strErrorMessage;
			if (dlgPrompt.ShowDialog(p_wndOwner) == DialogResult.OK)
				return dlgPrompt.EnteredText;
			return null;
		}

		/// <summary>
		/// Displays a prompt dialog with the given message.
		/// </summary>
		/// <param name="p_wndOwner">The owner of the prompt window.</param>
		/// <param name="p_strPrompt">The prompt message.</param>
		/// <param name="p_strCaption">The title of the dialog window.</param>
		/// <param name="p_strDefault">The default prompted text.</param>
		/// <returns>The entered prompted text, or <c>null</c> if the user
		/// cancelled the dialog.</returns>
		public static string ShowDialog(IWin32Window p_wndOwner, string p_strPrompt, string p_strCaption, string p_strDefault)
		{
			return ShowDialog(p_wndOwner, p_strPrompt, p_strCaption, p_strDefault, null, null);
		}

		private string m_strValidationPattern = null;

		#region Properties

		/// <summary>
		/// Gets or sets the prompt message.
		/// </summary>
		/// <value>The prompt message.</value>
		public string Prompt
		{
			get
			{
				return label1.Text;
			}
			set
			{
				label1.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the entered text.
		/// </summary>
		/// <value>The entered text.</value>
		public string EnteredText
		{
			get
			{
				return tbxPath.Text;
			}
			set
			{
				tbxPath.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the regular expression to use to validate the entered text.
		/// </summary>
		/// <value>The regular expression to use to validate the entered text.</value>
		public string ValidationPattern
		{
			get
			{
				return m_strValidationPattern;
			}
			set
			{
				m_strValidationPattern = value;
				if (!m_strValidationPattern.StartsWith("^"))
					m_strValidationPattern = "^" + m_strValidationPattern;
				if (!m_strValidationPattern.EndsWith("$"))
					m_strValidationPattern += "$";
			}
		}

		/// <summary>
		/// Gets or sets the error message to display if the entered text fails validation.
		/// </summary>
		/// <value>The error message to display if the entered text fails validation.</value>
		public string ValidationErrorMessage { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public PromptDialog()
		{
			InitializeComponent();
		}

		#endregion

		/// <summary>
		/// Hanldes the <see cref="Control.Click"/> event of the OK button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butOK_Click(object sender, EventArgs e)
		{
			if (!String.IsNullOrEmpty(ValidationPattern))
			{
				Regex rgxPattern = new Regex(ValidationPattern);
				if (!rgxPattern.IsMatch(tbxPath.Text))
				{
					erpErrors.SetError(tbxPath, ValidationErrorMessage);
					return;
				}
			}
			DialogResult = DialogResult.OK;
		}
	}
}
