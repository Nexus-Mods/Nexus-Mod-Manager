using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.Mods;
using Nexus.Client.ModManagement.UI;

namespace Nexus.Client.ModManagement
{
	public class ModLinkInstaller : IModLinkInstaller
	{
		private List<string> m_lstOverwriteFolders = new List<string>();
		private List<string> m_lstDontOverwriteFolders = new List<string>();
		private List<string> m_lstOverwriteMods = new List<string>();
		private List<string> m_lstDontOverwriteMods = new List<string>();
		private bool m_booDontOverwriteAll = false;
		private bool m_booOverwriteAll = false;
		private ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmationDelegate = ((s, b, m) => OverwriteResult.YesToAll);

		#region Properties

		/// <summary>
		/// Gets or sets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected VirtualModActivator VirtualModActivator { get; set; }

		#endregion

		#region Constructors

		public ModLinkInstaller(IVirtualModActivator p_ivaVirtualModActivator)
		{
			VirtualModActivator = (VirtualModActivator)p_ivaVirtualModActivator;
		}

		#endregion

		public string AddFileLink(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching)
		{
			return AddFileLink(p_modMod, p_strBaseFilePath, p_strSourceFile, p_booIsSwitching, false);
		}

		public string AddFileLink(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching, bool p_booHandlePlugin)
		{
			Int32 intPriority = 0;
			List<IVirtualModLink> lstFileLinks;
			bool? booLink = false;

			booLink = (TestOverwriteFileLink(p_modMod, p_strBaseFilePath, out intPriority, out lstFileLinks));

			if (booLink != null)
			{
				if (booLink == true)
				{
					if ((intPriority >= 0) && (lstFileLinks != null) && (lstFileLinks.Count > 0))
					{
						VirtualModActivator.UpdateLinkPriority(lstFileLinks);
						p_booIsSwitching = false;
					}
					return VirtualModActivator.AddFileLink(p_modMod, p_strBaseFilePath, p_strSourceFile, p_booIsSwitching, false, p_booHandlePlugin, 0);
				}
				else
					VirtualModActivator.AddInactiveLink(p_modMod, p_strBaseFilePath, intPriority++);
			}

			return String.Empty;
		}

		private bool? TestOverwriteFileLink(IMod p_modMod, string p_strBaseFilePath, out Int32 p_intPriority, out List<IVirtualModLink> p_lstFileLinks)
		{
			IMod modCheck;
			Int32 intPriority = VirtualModActivator.CheckFileLink(p_strBaseFilePath, out modCheck, out p_lstFileLinks);
			p_intPriority = intPriority;
			string strLoweredPath = p_strBaseFilePath.ToLowerInvariant();

			if (intPriority >= 0)
			{
				if (m_lstOverwriteFolders.Contains(Path.GetDirectoryName(strLoweredPath)))
					return true;
				if (m_lstDontOverwriteFolders.Contains(Path.GetDirectoryName(strLoweredPath)))
					return false;
				if (m_booOverwriteAll)
					return true;
				if (m_booDontOverwriteAll)
					return false;
			}

			if (modCheck == p_modMod)
				return null;

			string strModFile = String.Empty;
			string strModFileID = String.Empty;
			string strMessage = null;

			if (modCheck != null)
			{
				strModFile = modCheck.Filename;
				strModFileID = modCheck.Id;
				if (!String.IsNullOrEmpty(strModFileID))
				{
					if (m_lstOverwriteMods.Contains(strModFileID))
						return true;
					if (m_lstDontOverwriteMods.Contains(strModFileID))
						return false;
				}
				else
				{
					if (m_lstOverwriteMods.Contains(strModFile))
						return true;
					if (m_lstDontOverwriteMods.Contains(strModFile))
						return false;
				}
				strMessage = String.Format("Data file '{{0}}' has already been installed by '{1}'", p_strBaseFilePath, modCheck.ModName);
				strMessage += Environment.NewLine + "Activate this mod's file instead?";

				switch (OverwriteForm.ShowDialog(String.Format(strMessage, p_strBaseFilePath), true, (modCheck != null)))
				{
					case OverwriteResult.Yes:
						return true;
					case OverwriteResult.No:
						return false;
					case OverwriteResult.NoToAll:
						m_booDontOverwriteAll = true;
						return false;
					case OverwriteResult.YesToAll:
						m_booOverwriteAll = true;
						return true;
					case OverwriteResult.NoToGroup:
						Queue<string> folders = new Queue<string>();
						folders.Enqueue(Path.GetDirectoryName(strLoweredPath));
						while (folders.Count > 0)
						{
							strLoweredPath = folders.Dequeue();
							if (!m_lstOverwriteFolders.Contains(strLoweredPath))
							{
								m_lstDontOverwriteFolders.Add(strLoweredPath);
								if (Directory.Exists(strLoweredPath))
									foreach (string s in Directory.GetDirectories(strLoweredPath))
									{
										folders.Enqueue(s.ToLowerInvariant());
									}
							}
						}
						return false;
					case OverwriteResult.YesToGroup:
						folders = new Queue<string>();
						folders.Enqueue(Path.GetDirectoryName(strLoweredPath));
						while (folders.Count > 0)
						{
							strLoweredPath = folders.Dequeue();
							if (!m_lstDontOverwriteFolders.Contains(strLoweredPath))
							{
								m_lstOverwriteFolders.Add(strLoweredPath);
								if (Directory.Exists(strLoweredPath))
									foreach (string s in Directory.GetDirectories(strLoweredPath))
									{
										folders.Enqueue(s.ToLowerInvariant());
									}
							}
						}
						return true;
					case OverwriteResult.NoToMod:
						strModFile = modCheck.Filename;
						strModFileID = modCheck.Id;
						if (!String.IsNullOrEmpty(strModFileID))
						{
							if (!m_lstOverwriteMods.Contains(strModFileID))
								m_lstDontOverwriteMods.Add(strModFileID);
						}
						else
						{
							if (!m_lstOverwriteMods.Contains(strModFile))
								m_lstDontOverwriteMods.Add(strModFile);
						}
						return false;
					case OverwriteResult.YesToMod:
						strModFile = modCheck.Filename;
						strModFileID = modCheck.Id;
						if (!String.IsNullOrEmpty(strModFileID))
						{
							if (!m_lstDontOverwriteMods.Contains(strModFileID))
								m_lstOverwriteMods.Add(strModFileID);
						}
						else
						{
							if (!m_lstDontOverwriteMods.Contains(strModFile))
								m_lstOverwriteMods.Add(strModFile);
						}
						return true;
					default:
						throw new Exception("Sanity check failed: OverwriteDialog returned a value not present in the OverwriteResult enum");
				}
			}
			else
				return true;
		}
	}
}
