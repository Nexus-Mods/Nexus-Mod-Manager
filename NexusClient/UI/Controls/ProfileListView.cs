using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;
using Nexus.Client.Settings;
using Nexus.UI.Controls;
using BrightIdeasSoftware;

namespace Nexus.Client.UI.Controls
{
	public partial class ProfileListView : BrightIdeasSoftware.TreeListView
	{
		List<IVirtualModLink> m_tslEnabledLinks = null;
		List<IVirtualModInfo> m_tslEnabledMods = null;
		ThreadSafeObservableList<IModInfo> m_mifMods = null;	
		ReadOnlyObservableList<IMod> m_rolManagedMods = null;
		IModProfile m_imcSelectedProfile = null;
		IModRepository m_mmrModRepository = null;
		bool m_booShowMissingMods= false;
		string m_strLastSearchFilter = String.Empty;

		#region Custom Events

		public event EventHandler ProfileSwitch;
		public event EventHandler ProfileRemoved;

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets whether to show missing mods.
		/// </summary>
		/// <value>Whether to show missing mods.</value>
		public bool ShowMissingMods
		{
			get
			{
				return m_booShowMissingMods;
			}
			set
			{
				m_booShowMissingMods = value;
			}
		}

		/// <summary>
		/// Gets the currently selected item.
		/// </summary>
		/// <value>The currently selected item.</value>
		public object GetSelectedItem
		{
			get
			{
				if ((this.SelectedObjects != null) && (this.SelectedObjects.Count > 0))
					return GetSelectedItems.First();
				else
					return null;
			}
		}

		/// <summary>
		/// Gets the currently selected items.
		/// </summary>
		/// <value>The currently selected items.</value>
		public List<object> GetSelectedItems
		{
			get
			{
				List<object> lstObjects = new List<object>();
				foreach (object Item in this.SelectedObjects)
					lstObjects.Add(Item);
				return lstObjects;
			}
		}

		/// <summary>
		/// Gets the currently selected mod.
		/// </summary>
		/// <value>The currently selected mod.</value>
		public IMod GetSelectedMod
		{
			get
			{
				return (IMod)this.GetSelectedItem;
			}
		}

		/// <summary>
		/// Gets the currently selected Profile.
		/// </summary>
		/// <value>The currently selected Profile.</value>
		public IModProfile SelectedProfile
		{
			get
			{
				return m_imcSelectedProfile;
			}
			set
			{
				m_imcSelectedProfile = value;
			}
		}

		/// <summary>
		/// Gets the Profile manager to use to manage categories.
		/// </summary>
		/// <value>The Profile manager to use to manage categories.</value>
		protected IProfileManager ProfileManager { get; private set; }

		/// <summary>
		/// Gets the list of categories being managed by the Profile manager.
		/// </summary>
		/// <value>The list of categories being managed by the Profile manager.</value>
		protected List<IVirtualModLink> ModLinks
		{
			get
			{
				return m_tslEnabledLinks;
			}
		}

		/// <summary>
		/// Gets the list of categories being managed by the Profile manager.
		/// </summary>
		/// <value>The list of categories being managed by the Profile manager.</value>
		protected List<IVirtualModInfo> ModInfo
		{
			get
			{
				return m_tslEnabledMods;
			}
		}

		/// <summary>
		/// Gets the list of categories being managed by the Profile manager.
		/// </summary>
		/// <value>The list of categories being managed by the Profile manager.</value>
		protected ReadOnlyObservableList<IMod> ManagedMods
		{
			get
			{
				return m_rolManagedMods;
			}
		}

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		protected ISettings Settings;

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ProfileListView()
			: base()
		{
			InitializeComponent();
			tlcModName.Name = "ModName";
			tlcFileName.Name = "FileName";
			tlcModId.Name = "ModId";
			tlcFileCount.Name = "FileCount";
		}

		#endregion

		#region TreeListView Setup

		/// <summary>
		/// Setup the ProfileView
		/// </summary>
		/// <param name="p_lvwList">The source list view.</param>
		/// <param name="p_cmgProfileManager">The mod Profile Manager.</param>
		public void Setup(ReadOnlyObservableList<IMod> p_rolManagedMods, IModRepository p_mmrModRepository, IProfileManager p_cmgProfileManager, ISettings p_Settings)
		{
			this.Tag = false;

			this.CellEditActivation = CellEditActivateMode.None;
			this.MultiSelect = true;
			this.AllowDrop = true;
			this.UseFiltering = true;
			this.UseHyperlinks = true;

			ProfileManager = p_cmgProfileManager;
			m_mmrModRepository = p_mmrModRepository;
			m_rolManagedMods = p_rolManagedMods;
			Settings = p_Settings;			
			
			// Setup menuStrip commands
			SetupContextMenu();

			// Setup Profile validator
			SetupProfileValidator();

			// Setup Profile sorter
			SetupProfileSorter();

			this.CheckBoxes = false;
			this.UseSubItemCheckBoxes = false;

			this.FormatRow += delegate(object sender, FormatRowEventArgs e)
			{
				IVirtualModInfo modInfo = (IVirtualModInfo)e.Model;
				var modMod = ManagedMods.Where(x => String.Equals(Path.GetFileName(x.Filename), modInfo.ModFileName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
				if (modMod == null)
					e.Item.ForeColor = Color.Gray;
			};

			// Setup AspectGetter (IconListView cell parser)
			SetupColumnParser();

			// Setup the Drag&Drop functionality
			SetupDragAndDrop();

			// Setup hyperlink manager
			SetupHyperlinkManager();

			// Setup ImageGetters
			SetupImageGetters();

			// Set control initialized
			this.Tag = true;
			this.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
		}

		/// <summary>
		/// Setup the ProfileView context menu
		/// </summary>
		public void SetupContextMenu()
		{
		}

		/// <summary>
		/// Setup the ProfileView Profile validator
		/// </summary>
		public void SetupProfileValidator()
		{
			this.CanExpandGetter = delegate(object x)
			{
				return (x.GetType() == typeof(IVirtualModInfo));
			};
		}

		/// <summary>
		/// Setup the ProfileView mod sorter
		/// </summary>
		public void SetupProfileSorter()
		{
			this.ChildrenGetter = delegate(object x)
			{
				return null;
			};
		}

		/// <summary>
		/// Setup the IconListView cell parser
		/// </summary>
		public void SetupColumnParser()
		{
			tlcModName.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				Val = ((IVirtualModInfo)rowObject).ModName;

				return Val;
			};

			tlcModName.AspectToStringConverter = delegate(object x)
			{
				return x.ToString();
			};

			tlcFileName.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				Val = ((IVirtualModInfo)rowObject).ModFileName;

				return Val;
			};

			tlcFileName.AspectToStringConverter = delegate(object x)
			{
				return x.ToString();
			};

			tlcModId.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				Val = ((IVirtualModInfo)rowObject).ModId;

				return Val;
			};

			tlcModId.AspectToStringConverter = delegate(object x)
			{
				return x.ToString();
			};

			tlcFileCount.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				if (m_tslEnabledLinks != null)
				{

					var linkList = m_tslEnabledLinks.Where(x => x.ModInfo.Equals((IVirtualModInfo)rowObject));

					Val = linkList.Count().ToString();
				}

				return Val;
			};
		}

		/// <summary>
		/// Setup the Drag&Drop
		/// </summary>
		public void SetupDragAndDrop()
		{
			this.IsSimpleDragSource = false;
			this.IsSimpleDropSink = false;

			this.CanDrop += delegate(object sender, BrightIdeasSoftware.OlvDropEventArgs e)
			{
				e.Effect = DragDropEffects.None;
			};

			this.Dropped += delegate(object sender, BrightIdeasSoftware.OlvDropEventArgs e)
			{
			};
		}

		/// <summary>
		/// Setup the Hyperlink Manager
		/// </summary>
		public void SetupHyperlinkManager()
		{
			tlcModId.Hyperlink = true;
			this.IsHyperlink += delegate(object sender, BrightIdeasSoftware.IsHyperlinkEventArgs e)
			{
				try
				{
					IVirtualModInfo vmiInfo = (IVirtualModInfo)e.Model;
					if (!(vmiInfo == null))
					{
						if (String.IsNullOrEmpty(vmiInfo.ModId))
							e.Url = null;
						else
						{
							string strUri = "http://" + m_mmrModRepository.GameModeWebsite + "/mods/" + vmiInfo.ModId;
							e.Url = strUri;
						}					
					}
				}
				catch
				{
					e.Url = null;
				}
			};
		}

		/// <summary>
		/// Setup the context menu items visibility
		/// </summary>
		/// <param name="p_booProfileSetup">Whether to setup the visibility for a Profile or a mod</param>
		/// <param name="p_strReadMeFiles">The readme files</param>
		public void SetupContextMenuFor(bool p_booProfileSetup, string[] p_strReadMeFiles)
		{
		}

		/// <summary>
		/// Setup the Image Getters
		/// </summary>
		public void SetupImageGetters()
		{
			tlcModName.ImageGetter = delegate(object rowObject)
			{
				//try
				//{
				//	if ((m_tslEnabledMods.Select(x => x.ModFileName).Distinct().Contains(((IMod)rowObject).Filename)) && (m_rolActiveMods.Contains((IMod)rowObject)))
				//		return new Bitmap(Properties.Resources.dialog_ok_4_16, 12, 12);
				//	else
				//		return new Bitmap(!m_rolActiveMods.Contains((IMod)rowObject) ? new Bitmap(12, 12) : Properties.Resources.dialog_block, 14, 14);
				//}
				//catch
				//{
				//	return new Bitmap(!m_rolActiveMods.Contains((IMod)rowObject) ? new Bitmap(12, 12) : Properties.Resources.dialog_block, 14, 14);
				//}
				return null;
			};
		}

		#endregion

		#region Profile Management

		/// <summary>
		/// Loads the TreeListView categories.
		/// </summary>
		public void LoadData(List<IVirtualModInfo> p_tslVirtualModInfo, List<IVirtualModLink> p_tslEnabledLinks)
		{
			m_tslEnabledMods = p_tslVirtualModInfo;
			m_tslEnabledLinks = p_tslEnabledLinks;

			if (this.Items.Count > 0)
			{
				this.ClearObjects();
			}

			this.SetObjects(m_tslEnabledMods);

			if ((m_tslEnabledMods != null) && (m_tslEnabledMods.Count > 0))
				this.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
			else
				this.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);

			this.Refresh();
		}

		/// <summary>
		/// Adds a new Profile to the TreeListView.
		/// </summary>
		/// <param name="p_imcProfile">The Profile to add to the roots list.</param>
		/// <param name="booIsNew">Whether the Profile is new or was just hidden.</param>
		public void AddData(IVirtualModInfo p_vmiModInfo, bool booIsNew)
		{
		}

		/// <summary>
		/// Removes the Profile from the TreeListView.
		/// </summary>
		/// <param name="p_mctProfile">The Profile to remove.</param>
		public bool RemoveData(ModProfile p_mctProfile)
		{
			return false;
		}

		/// <summary>
		/// Refresh the Profile in the TreeListView.
		/// </summary>
		/// <param name="p_mctProfile">The Profile to refresh.</param>
		public bool RefreshData(ModProfile p_mctProfile)
		{
			return false;
		}

		/// <summary>
		/// Update the Profile in the TreeListView.
		/// </summary>
		/// <param name="p_mctProfile">The Profile to update.</param>
		/// <param name="strOldValue">The previous Profile name.</param>
		public void UpdateData(ModProfile p_mctProfile, string strOldValue)
		{
		}

		/// <summary>
		/// Removes the selected Profile.
		/// </summary>
		/// <param name="p_imcProfile">The Profile to remove.</param>
		public void RemoveProfile(IVirtualModInfo p_vmiModInfo)
		{
		}

		/// <summary>
		/// Adds a new Profile.
		/// </summary>
		public void AddNewProfile()
		{
		}

		#endregion

		#region EventHandler

		#endregion

		/// <summary>
		/// Applies the default list filters
		/// </summary>
		private void ApplyFilters(IVirtualModInfo p_vmiModInfo)
		{
			this.ModelFilter = new ModelFilter(delegate(object x)
			{
				return true;
			});
		}

		/// <summary>
		/// Realoads the treeview applying the default filters
		/// </summary>
		/// <param name="p_booApplySearchFilter"> True if the function needs to restore the previous search filter.</param>
		public void ReloadList(bool p_booApplySearchFilter)
		{
			this.RebuildAll(true);

			if (p_booApplySearchFilter)
				AddStringFilter(m_strLastSearchFilter);
			else
			{
				RemoveStringFilter();
			}
		}

		/// <summary>
		/// This will apply a string filter to the list.
		/// </summary>
		/// /// <param name="p_strFilter">The string filter.</param>
		public void AddStringFilter(string p_strFilter)
		{
			TextMatchFilter tmfFilter = TextMatchFilter.Contains(this, p_strFilter);
			tmfFilter.Columns = new OLVColumn[] { (OLVColumn)this.Columns[0] };
			this.ModelFilter = tmfFilter;
			HighlightTextRenderer highlightingRenderer = this.GetColumn(0).Renderer as HighlightTextRenderer;
			if (highlightingRenderer != null)
				highlightingRenderer.Filter = tmfFilter;
			m_strLastSearchFilter = p_strFilter;
		}

		/// <summary>
		/// This will remove the list filter.
		/// </summary>
		public void RemoveStringFilter()
		{
			TextMatchFilter tmfFilter = TextMatchFilter.Contains(this, String.Empty);
			tmfFilter.Columns = new OLVColumn[] { (OLVColumn)this.Columns[0] };
			HighlightTextRenderer highlightingRenderer = this.GetColumn(0).Renderer as HighlightTextRenderer;
			if (highlightingRenderer != null)
				highlightingRenderer.Filter = tmfFilter;
			m_strLastSearchFilter = String.Empty;
		}

		/// <summary>
		//Resets the Columns to the original width
		/// </summary>
		public void ResetColumns()
		{
			tlcModName.Width = 200;
			tlcModId.Width = 500;
			tlcFileName.Width = 200;
			tlcFileCount.Width = 100;

			SizeColumnsToFit();
		}

		/// <summary>
		/// This resizes the columns to fill the list view.
		/// </summary>
		public void SizeColumnsToFit()
		{
			Int32 intFixedWidth = 0;
			for (Int32 i = 0; i < this.Columns.Count; i++)
				if (this.Columns[i] != tlcModName)
					intFixedWidth += this.Columns[i].Width;
			tlcModName.Width = this.ClientSize.Width - intFixedWidth;
		}

		/// <summary>
		/// This checks if the passed date string is a valid date.
		/// </summary>
		/// <param name="p_strDate">The date string to check.</param>
		protected bool CheckDate(String p_strDate)
		{
			try
			{
				DateTime dt = DateTime.Parse(p_strDate);
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// This dynamically creates a transparency enabled png merging the folder icon with the mod count.
		/// </summary>
		private Bitmap CreateBitmapImage(string sImageText, Bitmap p_objBmpImage, Int32 p_intFontSize, Int32 p_intFontHRatio, Int32 p_intFontVRatio, Int32 p_intFontHOffset, Int32 p_intFontVOffset)
		{
			int intWidth = 0;
			int intHeight = 0;
			string strImageFont = "Arial";
			System.Drawing.FontStyle fsFontStyle = FontStyle.Bold;

			if (!IsFontInstalled(strImageFont))
				strImageFont = this.Font.FontFamily.ToString();
			if (!SupportBold(new Font(strImageFont, 8)))
				fsFontStyle = FontStyle.Regular;

			Font objFont = new Font(strImageFont, p_intFontSize, fsFontStyle, System.Drawing.GraphicsUnit.Pixel);
			Graphics objGraphics = Graphics.FromImage(p_objBmpImage);
			intWidth = (int)objGraphics.MeasureString(sImageText, objFont).Width;
			intHeight = (int)objGraphics.MeasureString(sImageText, objFont).Height;
			p_objBmpImage = new Bitmap(p_objBmpImage, new Size(intWidth + p_intFontHRatio, intHeight + p_intFontVRatio));
			objGraphics = Graphics.FromImage(p_objBmpImage);
			objGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			objGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			objGraphics.DrawString(sImageText, objFont, new SolidBrush(Color.FromArgb(25, 25, 25)), p_intFontHOffset, p_intFontVOffset);
			objGraphics.Flush();
			p_objBmpImage.MakeTransparent(Color.Magenta);
			return (p_objBmpImage);
		}

		/// <summary>
		/// This checks if the passed font is present.
		/// </summary>
		private bool IsFontInstalled(string fontName)
		{
			try
			{
				using (var testFont = new Font(fontName, 8))
				{
					return 0 == string.Compare(
					  fontName,
					  testFont.Name,
					  StringComparison.InvariantCultureIgnoreCase);
				}
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// This checks if the passed font support Bold.
		/// </summary>
		private bool SupportBold(Font font)
		{
			try
			{
				using (Font bold = new Font(font, FontStyle.Bold))
				{
					return true;
				}
			}
			catch (ArgumentException)
			{
				return false;
			}
		}

		/// <summary>
		/// This sets the sort column for the Profile view control.
		/// </summary>
		/// <param name="p_intPrimaryColumn">The name of the column to sort by.</param>
		/// <param name="p_sroPrimarySortOrder">The sort order.</param>
		public void SetPrimarySortColumn(int p_intPrimaryColumn, SortOrder p_sroPrimarySortOrder)
		{
			if (this.Columns.Count > 0)
			{
				if (p_intPrimaryColumn > (this.Columns.Count - 1))
					p_intPrimaryColumn = this.Columns.Count - 1;
				else if (p_intPrimaryColumn < 0)
					p_intPrimaryColumn = 0;

				this.PrimarySortColumn = this.ColumnsInDisplayOrder[p_intPrimaryColumn];
				this.PrimarySortOrder = p_sroPrimarySortOrder;
				this.SecondarySortColumn = tlcModName;
				this.SecondarySortOrder = SortOrder.Ascending;
			}
		}

		/// <summary>
		/// This sets the sort column for the Profile view control.
		/// </summary>
		public void SetSecondarySortColumn()
		{
			this.SecondarySortColumn = tlcModName;
			this.SecondarySortOrder = SortOrder.Ascending;
		}
	}
}
