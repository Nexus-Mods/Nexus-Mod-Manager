namespace Nexus.Client.SSO
{
    using System.Windows.Forms;
    using DevExpress.Utils;
    using Nexus.Client.UI;
    using Nexus.Client.Util.Localization;

    public partial class ManualApiKeyEntryForm : ManagedFontXtraForm
    {
        private AuthenticationFormViewModel _viewModel;

        public ManualApiKeyEntryForm(AuthenticationFormViewModel viewModel)
        {
            InitializeComponent();
            ApplyLocalization();
            NmmIconProvider.Bind(buttonOk, NmmIconAction.Apply);
            NmmIconProvider.Bind(buttonCancel, NmmIconAction.Cancel);
            _viewModel = viewModel;
        }

        private void ApplyLocalization()
        {
            Text = LanguageManager.Get("Authentication.Manual.Window.Title", "Manual API Key Entry");
            label1.Text = LanguageManager.Get("Authentication.Manual.ConnectionInfo", "For unknown reasons NMM cannot communicate with the Nexus SSO service.");
            label2.Text = LanguageManager.Get("Authentication.Manual.Instructions", "Click the link below to get to the API key management page, where you can manually generate an API key and enter it in the field at the bottom.");
            linkLabelManageApiKeys.Text = "<href=api>" + LanguageManager.Get("Authentication.Manual.ManageKeysLink", "API key management") + "</href>";
            label3.Text = LanguageManager.Get("Authentication.Manual.ApiKeyLabel", "API key:");
            buttonOk.Text = LanguageManager.Get("Common.Action.Ok", "OK");
            buttonCancel.Text = LanguageManager.Get("Common.Action.Cancel", "Cancel");
        }

        private void LinkLabelManageApiKeys_HyperlinkClick(object sender, HyperlinkClickEventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.nexusmods.com/users/myaccount?tab=api%20access");
        }

        private void ButtonCancel_Click(object sender, System.EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ButtonOk_Click(object sender, System.EventArgs e)
        {
            _viewModel.ApiKey = textBoxApiKey.Text;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
