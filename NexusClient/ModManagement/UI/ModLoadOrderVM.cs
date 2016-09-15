using Nexus.Client.Games;

namespace Nexus.Client.ModManagement.UI
{
    public class ModLoadOrderVM
    {
        private ModLoadOrderControl m_conLoadOrderControl = null;
        private ModManager m_modManager = null;
        private IGameMode m_igmGameMode = null;
        private IVirtualModActivator m_vmaActivator = null;

        public ModLoadOrderVM(ModManager p_modManager, ModLoadOrderControl p_mloControl, IGameMode p_igmGameMode, IVirtualModActivator p_vmaActivator)
        {
            m_modManager = p_modManager;
            m_conLoadOrderControl = p_mloControl;
            m_igmGameMode = p_igmGameMode;
            m_vmaActivator = p_vmaActivator;
        }

        public void SorteBasedOnList()
        {
            
        }
    }
}
