namespace Nexus.Client.SSO
{
    using System.Windows.Forms;

    public partial class ManualApiKeyEntryForm : Form
    {
        private AuthenticationFormViewModel _viewModel;

        public ManualApiKeyEntryForm(AuthenticationFormViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
        }

        private void LinkLabelManageApiKeys_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
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
