using System;
using System.Windows.Forms;
using Nexus.Client.Settings;
using Nexus.Client.Settings.UI;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.StateOfDecay.Settings.UI
{
    /// <summary>
    /// The page of general settings.
    /// </summary>
    public partial class GeneralSettingsPage : ManagedFontUserControl, ISettingsGroupView
    {
        #region ISettingsGroupView Members

        /// <summary>
        /// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
        /// </summary>
        /// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
        public SettingsGroup SettingsGroup { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// The default constructor.
        /// </summary>
        /// <value>This is for comptaiblity with the designer.</value>
        protected GeneralSettingsPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// A sinmple consturctor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_gsgSettings">The settings group whose settings will be editable with this view.</param>
        public GeneralSettingsPage(GeneralSettingsGroup p_gsgSettings)
            :this()
        {
            SettingsGroup = p_gsgSettings;
            rdcDirectories.ViewModel = p_gsgSettings.RequiredDirectoriesVM;

            BindingHelper.CreateFullBinding(tbxCommand, () => tbxCommand.Text, p_gsgSettings, () => p_gsgSettings.CustomLaunchCommand);
            BindingHelper.CreateFullBinding(tbxCommandArguments, () => tbxCommandArguments.Text, p_gsgSettings, () => p_gsgSettings.CustomLaunchCommandArguments);
        }

        #endregion
    }
}
