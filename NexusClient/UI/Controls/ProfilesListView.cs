using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;
using Nexus.Client.Settings;
using Nexus.UI.Controls;
using BrightIdeasSoftware;

namespace Nexus.Client.UI.Controls
{
	public partial class ProfilesListView : BrightIdeasSoftware.TreeListView
	{
		ThreadSafeObservableList<IModProfile> m_tslEnabledProfiles = null;
        
		ReadOnlyObservableList<IMod> m_rolManagedMods = null;
		IModProfile m_imcSelectedProfile = null;
		bool m_booShowMissingMods= false;
		string m_strLastSearchFilter = String.Empty;

		private ToolStripMenuItem m_mniProfileFolder;

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
		public IModProfile GetSelectedProfile
		{
			get
			{
				return (IModProfile)this.GetSelectedItem;
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
		/// Gets the currently selected category.
		/// </summary>
		/// <value>The currently selected category.</value>
		public ContextMenuStrip ProfileViewContextMenu
		{
			get
			{
				return this.cmsContextMenu;
			}
		}

		/// <summary>
		/// Gets the Profile manager to use to manage categories.
		/// </summary>
		/// <value>The Profile manager to use to manage categories.</value>
		protected ProfileManager ProfileManager { get; private set; }
						
		/// <summary>
		/// Gets the list of categories being managed by the Profile manager.
		/// </summary>
		/// <value>The list of categories being managed by the Profile manager.</value>
		protected ThreadSafeObservableList<IModProfile> ModProfiles
		{
			get
			{
				return m_tslEnabledProfiles;
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
		public ProfilesListView()
			: base()
		{
			InitializeComponent();
			tlcProfileName.Name = "ProfileName";
			tlcActiveMods.Name = "ActiveMods";
			tlcLastBackedUp.Name = "LastBackedUp";
			tlcVersion.Name = "Version";
			tlcAuthor.Name = "Author";
			tlcShared.Name = "Shared";
			tlcEdited.Name = "Edited";
			tlcRevert.Name = "Revert";

			this.cmsContextMenu.Opening += cmsContextMenu_Opening;
			SetupContextMenu();
		}

		#endregion
		
		#region TreeListView Setup

		/// <summary>
		/// Setup the ProfileView
		/// </summary>
		/// <param name="p_lvwList">The source list view.</param>
		/// <param name="p_cmgProfileManager">The mod Profile Manager.</param>
		public void Setup() // ReadOnlyObservableList<IMod> p_rolManagedMods, IModRepository p_mmrModRepository, IProfileManager p_cmgProfileManager, ISettings p_Settings)
		{
			this.Tag = false;

			this.CellEditActivation = CellEditActivateMode.None;
			this.MultiSelect = true;
			this.AllowDrop = true;
			this.UseFiltering = true;
			this.UseHyperlinks = true;
			
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
				IModProfile modProfile = (IModProfile)e.Model;
				var impProfile = ProfileManager.ModProfiles.Where(x => string.Equals(x.Name, modProfile.Name, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
				if (impProfile == null)
					e.Item.ForeColor = Color.Gray;
			};

			// Setup AspectGetter (IconListView cell parser)
			SetupColumnParser();

			// Setup the Drag&Drop functionality
			SetupDragAndDrop();
			
			// Setup ImageGetters
			SetupImageGetters();

			// Set control initialized
			this.Tag = true;
			ResetColumns();
		}

		/// <summary>
		/// Setup the ProfileView context menu
		/// </summary>
		public void SetupContextMenu()
		{
			m_mniProfileFolder = new ToolStripMenuItem("Open Profile Folder", new Bitmap(Properties.Resources.folders_open, 16, 16));
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
			tlcProfileName.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				Val = ((IModProfile)rowObject).Name;

				return Val;
			};

			tlcProfileName.AspectToStringConverter = delegate(object x)
			{
				return x.ToString();
			};

			tlcActiveMods.AspectGetter = delegate(object rowObject)
			{
				int Val = 0;

				Val = ((ModProfile)rowObject).ModCount;

				return Val;
			};

			tlcActiveMods.AspectToStringConverter = delegate(object x)
			{
				return x.ToString();
			};

			tlcLastBackedUp.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				if (((ModProfile)rowObject).BackupDate == "")
					Val = "Never";
				else
					Val = ((ModProfile)rowObject).BackupDate;

				return Val;
			};

			tlcLastBackedUp.AspectToStringConverter = delegate(object x)
			{
				if (x != null)
					return x.ToString();
				else
					return "Never";
			};

			tlcVersion.AspectGetter = delegate (object rowObject)
			{
				string Val = "0";

				if (((ModProfile)rowObject).IsShared)
					Val = ((ModProfile)rowObject).Version.ToString();
				else
					Val = "-";

				return Val;
			};

			tlcVersion.AspectToStringConverter = delegate (object x)
			{
				return x.ToString();
			};

			tlcAuthor.AspectGetter = delegate (object rowObject)
			{
				string Val = String.Empty;

				Val = ((ModProfile)rowObject).Author;

				return Val;
			};

			tlcAuthor.AspectToStringConverter = delegate (object x)
			{
				return x.ToString();
			};

			tlcShared.AspectGetter = delegate(object rowObject)
			{
				string Val = "Yes";

				if (!((ModProfile)rowObject).IsShared)
					Val = "No";
												
				return Val;
			};

			tlcEdited.AspectGetter = delegate(object rowObject)
			{
				string Val = "Yes";

				if (!((ModProfile)rowObject).IsEdited)
					Val = "No";

				return Val;
			};

			this.CellClick += new EventHandler<CellClickEventArgs>(Column_Click);
		}

		private void Column_Click(object sender, CellClickEventArgs e)
		{
			if(e.Column == tlcRevert)
			{
				ModProfile Profile = (ModProfile)e.Model;

				if ((Profile.BackupDate != "") && (Profile.IsEdited))
				{
					string strRevertWarning = "If you click YES, any changes you've made to this profile will be deleted and the profile will revert back to its original settings." + Environment.NewLine +
					"If you click NO, the current profile won't be touched and a new profile will be made with the original settings." + Environment.NewLine +
					"If you click CANCEL, this window will close and nothing will happen." + Environment.NewLine;

					DialogResult drResult = ExtendedMessageBox.Show(this, strRevertWarning, "Revert", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
				}
			}
		}

		/// <summary>
		/// Handles the cmsContextMenu.Opening event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
		{
			cmsContextMenu.Items.Clear();

			if (this.SelectedObjects.Count < 1)
			{
				e.Cancel = true;
				return;
			}
			
			if (this.SelectedObjects.OfType<object>().All(o => o is IModProfile))
			{
				this.SetupModContextMenu();
				return;
			}

			// heterogeneous selection - show no context menu
			e.Cancel = true;
		}

		/// <summary>
		/// Prepares the context menu for the currently selected mods.
		/// </summary>
		private void SetupModContextMenu()
		{
			// validate
			if (!this.SelectedObjects.OfType<object>().All(o => o is IModProfile))
			{
				return;
			}

			if (this.SelectedObjects.Count == 1)
			{
				// single profile management
				// can:
				// - display profile's filename
				// - display profile's folder, if any
				
				var menuItem = cmsContextMenu.Items.Add(GetSelectedProfile != null ? Path.GetFileName(GetSelectedProfile.Name) : "Unable to retrieve the profile's filename",
					new Bitmap(Properties.Resources.document_save, 16, 16));
				menuItem.Enabled = false;
				cmsContextMenu.Items.Add("-");

				cmsContextMenu.Items.Add(m_mniProfileFolder);
				RenderContextMenuProfileFolder();		
			}
		}

		/// <summary>
		/// Populates "Open Profile Folder" sub-items as appropriate.
		/// </summary>
		private void RenderContextMenuProfileFolder()
		{
			m_mniProfileFolder.DropDownItems.Clear();
			m_mniProfileFolder.Click += cmsContextMenu_OpenProfileFolder;
			m_mniProfileFolder.Tag = null;
		}

		/// <summary>
		/// Handles the cmsContextMenu.OpenProfileFolder event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_OpenProfileFolder(object sender, EventArgs e)
		{
			if (this.GetSelectedProfile == null ||
				!(sender is ToolStripMenuItem) ||
				(sender != m_mniProfileFolder && ((ToolStripMenuItem)sender).OwnerItem != m_mniProfileFolder))
			{
				return;
			}

			var LocalID = GetSelectedProfile.Id as string;
			if (string.IsNullOrWhiteSpace(LocalID))
			{
				return;
			}

			string LocalDirectory = Path.Combine(ProfileManager.ProfileManagerPath, LocalID);
			if (Directory.Exists(LocalDirectory))
			{
				System.Diagnostics.Process.Start(LocalDirectory);
			}
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
			tlcRevert.ImageGetter = delegate(object rowObject)
			{
				try
				{
					var Profile = (IModProfile)rowObject;
					
					if ((Profile.BackupDate != "") && (Profile.IsEdited))
						return new Bitmap(Properties.Resources.change_game_mode, 12, 12);
					else
						return null;
				}
				catch
				{
					//return new Bitmap(!m_rolActiveMods.Contains((IMod)rowObject) ? new Bitmap(12, 12) : Properties.Resources.dialog_block, 14, 14);
				}
				return null;
			};
		}

		#endregion

		#region Profile Management
		
		/// <summary>
		/// Loads the TreeListView categories.
		/// </summary>
		public void LoadData(ThreadSafeObservableList<IModProfile> p_tslModProfile)
		{
			m_tslEnabledProfiles = p_tslModProfile;
			
			if (m_tslEnabledProfiles.Count > 0)
				this.SetObjects(m_tslEnabledProfiles);
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
			if (this.Items.Count > 0)
			{
				SetupContextMenu();
			}
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
			tlcProfileName.Width = 150;
			tlcActiveMods.Width = 80;
			tlcLastBackedUp.Width = 90;
			tlcVersion.Width = 80;
			tlcAuthor.Width = 80;
			tlcShared.Width = 50;
			tlcEdited.Width = 50;
			tlcRevert.Width = 21;

			SizeColumnsToFit();
		}

		/// <summary>
		/// This resizes the columns to fill the list view.
		/// </summary>
		public void SizeColumnsToFit()
		{
			Int32 intFixedWidth = 0;
			for (Int32 i = 0; i < this.Columns.Count; i++)
				if (this.Columns[i] != tlcProfileName)
					intFixedWidth += this.Columns[i].Width;
			tlcProfileName.Width = this.ClientSize.Width - intFixedWidth;
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
				this.SecondarySortColumn = tlcProfileName;
				this.SecondarySortOrder = SortOrder.Ascending;
			}
		}

		/// <summary>
		/// This sets the sort column for the Profile view control.
		/// </summary>
		public void SetSecondarySortColumn()
		{
			this.SecondarySortColumn = tlcProfileName;
			this.SecondarySortOrder = SortOrder.Ascending;
		}
	}
}
