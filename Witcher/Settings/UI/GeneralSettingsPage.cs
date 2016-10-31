using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nexus.Client.Settings.UI;
using Nexus.Client.Settings;

namespace Nexus.Client.Games.Witcher.Settings.UI
{
    public partial class GeneralSettingsPage : UserControl, ISettingsGroupView
    {
        private GeneralSettingsGroup gsgGeneralSettings;

        public GeneralSettingsPage()
        {
            InitializeComponent();
        }

        public GeneralSettingsPage(GeneralSettingsGroup gsgGeneralSettings)
        {
            this.gsgGeneralSettings = gsgGeneralSettings;
        }

        public SettingsGroup SettingsGroup
        {
            get
            {
                throw new NotImplementedException();
            }
        }
    }
}
