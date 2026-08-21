namespace Nexus.Client.SSO
{
    using System.Windows.Forms;
    using DevExpress.Utils;
    using Nexus.Client.UI;

    public partial class ManualApiKeyEntryForm : ManagedFontXtraForm
    {
        private AuthenticationFormViewModel _viewModel;

        public ManualApiKeyEntryForm(AuthenticationFormViewModel viewModel)
        {
            InitializeComponent();
            NmmIconProvider.Bind(buttonOk, NmmIconAction.Apply);
            NmmIconProvider.Bind(buttonCancel, NmmIconAction.Cancel);
            _viewModel = viewModel;
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
