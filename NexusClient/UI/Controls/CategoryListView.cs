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
	public partial class CategoryListView : BrightIdeasSoftware.TreeListView
	{
		ReadOnlyObservableList<IMod> m_rolManagedMods = null;
		ReadOnlyObservableList<IMod> m_rolActiveMods = null;
		IModCategory m_imcSelectedCategory = null;
		IModRepository m_mmrModRepository = null;
		bool m_booShowEmpty = false;
		bool m_booCategoryMode = true;
		string m_strLastSearchFilter = String.Empty;

		#region Custom Events

		public event EventHandler CategorySwitch;
		public event EventHandler CategoryRemoved;
		public event EventHandler FileDropped;
		public event EventHandler UpdateWarningToggle;
		public event EventHandler ToggleAllWarnings;
		public event EventHandler OpenReadMeFile;
		public event EventHandler ReadmeScan;
		public event EventHandler UninstallMod;
		public event EventHandler UninstallModFromProfiles;

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets whether to show the hidden categories (no mods assigned).
		/// </summary>
		/// <value>Whether to show the hidden categories (no mods assigned).</value>
		public bool ShowHiddenCategories
		{
			get
			{
				return m_booShowEmpty;
			}
			set
			{
				m_booShowEmpty = value;
			}
		}

		/// <summary>
		/// Gets or sets whether to enable the category mode.
		/// </summary>
		/// <value>Whether to enable the category mode.</value>
		public bool CategoryModeEnabled
		{
			get
			{
				return m_booCategoryMode;
			}
			set
			{
				m_booCategoryMode = value;
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
		/// Gets the currently selected category.
		/// </summary>
		/// <value>The currently selected category.</value>
		public IModCategory SelectedCategory
		{
			get
			{
				return m_imcSelectedCategory;
			}
			set
			{
				m_imcSelectedCategory = value;
			}
		}

		/// <summary>
		/// Gets the currently selected category.
		/// </summary>
		/// <value>The currently selected category.</value>
		public ContextMenuStrip CategoryViewContextMenu
		{
			get
			{
				return this.cmsContextMenu;
			}
		}

		/// <summary>
		/// Gets the category manager to use to manage categories.
		/// </summary>
		/// <value>The category manager to use to manage categories.</value>
		protected CategoryManager CategoryManager { get; private set; }

		/// <summary>
		/// Gets the virtual mod activator.
		/// </summary>
		/// <value>The virtual mod activator.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets the list of categories being managed by the category manager.
		/// </summary>
		/// <value>The list of categories being managed by the category manager.</value>
		protected ThreadSafeObservableList<IModCategory> Categories
		{
			get
			{
				return CategoryManager.Categories;
			}
		}

		/// <summary>
		/// Gets the list of currently enabled mods.
		/// </summary>
		/// <value>The list of currently enabled mod.</value>
		protected ThreadSafeObservableList<IVirtualModInfo> VirtualMods
		{
			get
			{
				return VirtualModActivator.VirtualMods;
			}
		}

		/// <summary>
		/// Gets whether the manager is in offline mode.
		/// </summary>
		/// <value>Whether the manager is in offline mode.</value>
		protected bool IsOffline
		{
			get
			{
				return (m_mmrModRepository != null) ? m_mmrModRepository.IsOffline : true;
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
		public CategoryListView()
			: base()
		{
			InitializeComponent();
			tlcModName.Name = "ModName";
			tlcInstallDate.Name = "InstallDate";
			tlcDownloadDate.Name = "DownloadDate";
			tlcVersion.Name = "HumanReadableVersion";
			tlcWebVersion.Name = "WebVersion";
			tlcAuthor.Name = "Author";
			tlcEndorsement.Name = "Endorsement";
			tlcCategory.Name = "Category";
			tlcModName.AspectName = "Text";
			tlcInstallDate.AspectName = "Text";
			tlcDownloadDate.AspectName = "Text";
			tlcVersion.AspectName = "Text";
			tlcWebVersion.AspectName = "Text";
			tlcAuthor.AspectName = "Text";
			tlcCategory.AspectName = "Text";
		}

		#endregion

		#region TreeListView Setup

		/// <summary>
		/// Setup the CategoryView
		/// </summary>
		/// <param name="p_lvwList">The source list view.</param>
		/// <param name="p_cmgCategoryManager">The mod Category Manager.</param>
		public void Setup(ReadOnlyObservableList<IMod> p_rolManagedMods, ReadOnlyObservableList<IMod> p_rolActiveMods, IModRepository p_mmrModRepository, IVirtualModActivator p_ivaVirtualModActivator, CategoryManager p_cmgCategoryManager, ISettings p_Settings)
		{
			this.Tag = false;

			this.CellEditActivation = CellEditActivateMode.None;
			this.MultiSelect = true;
			this.AllowDrop = true;
			this.UseFiltering = true;

			CategoryManager = p_cmgCategoryManager;
			m_mmrModRepository = p_mmrModRepository;
			m_rolManagedMods = p_rolManagedMods;
			m_rolActiveMods = p_rolActiveMods;
			VirtualModActivator = p_ivaVirtualModActivator;
			Settings = p_Settings;

			// Setup menuStrip commands
			SetupContextMenu();

			// Setup category validator
			SetupCategoryValidator();

			// Setup category sorter
			SetupCategorySorter();

			this.CheckBoxes = false;
			this.UseSubItemCheckBoxes = false;
			this.BooleanCheckStateGetter = delegate(object x)
			{
				if (x.GetType() != typeof(ModCategory))
					if (m_rolActiveMods.Contains((IMod)x))
						return true;

				return false;
			};

			this.FormatRow += delegate(object sender, FormatRowEventArgs e)
			{
				if (e.Model.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)e.Model;
					if (!m_rolActiveMods.Contains(modMod))
						e.Item.ForeColor = Color.Gray;
				}
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
		}

		/// <summary>
		/// Setup the CategoryView context menu
		/// </summary>
		public void SetupContextMenu()
		{
			cmsContextMenu.Items.Clear();
			cmsContextMenu.Items.Add("Move to Category:");
			cmsContextMenu.Items.Add("Categories:");
			cmsContextMenu.Items.Add("Mod update warnings", new Bitmap(Properties.Resources.update_warning, 16, 16));
			cmsContextMenu.Items.Add("Scan selected mods for Readme files", new Bitmap(Properties.Resources.text_x_generic, 16, 16), new EventHandler(cmsContextMenu_ReadmeScan));
			(cmsContextMenu.Items[1] as ToolStripMenuItem).DropDownItems.Add("New", null, new EventHandler(cmsContextMenu_CategoryNew));
			(cmsContextMenu.Items[1] as ToolStripMenuItem).DropDownItems.Add("Remove selected", null, new EventHandler(cmsContextMenu_CategoryRemove));
			(cmsContextMenu.Items[2] as ToolStripMenuItem).DropDownItems.Add("Toggle mod update warning on selected file/s", null, new EventHandler(cmsContextMenu_ToggleUpdateWarning));
			(cmsContextMenu.Items[2] as ToolStripMenuItem).DropDownItems.Add("Enable mod update warnings for all files", null, new EventHandler(cmsContextMenu_EnableAllWarnings));
			(cmsContextMenu.Items[2] as ToolStripMenuItem).DropDownItems.Add("Disable mod update warnings for all files", null, new EventHandler(cmsContextMenu_DisableAllWarnings));

			foreach (IModCategory imcCategory in Categories.OrderBy(x => x.CategoryName))
				(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Add(imcCategory.CategoryName, null, new EventHandler(cmsContextMenu_CategoryClicked));
		}

		/// <summary>
		/// Setup the CategoryView category validator
		/// </summary>
		public void SetupCategoryValidator()
		{
			this.CanExpandGetter = delegate(object x)
			{
				if (m_booCategoryMode)
					return (x.GetType() == typeof(ModCategory));
				else
					return (x.GetType() == typeof(IMod));
			};
		}

		/// <summary>
		/// Setup the CategoryView mod sorter
		/// </summary>
		public void SetupCategorySorter()
		{
			this.ChildrenGetter = delegate(object x)
			{
				if (x.GetType() == typeof(ModCategory))
				{
					var CategoryMods = from Mod in m_rolManagedMods
									   where (Mod != null) && ((Mod.CustomCategoryId >= 0 ? Mod.CustomCategoryId : Mod.CategoryId) == ((IModCategory)x).Id)
									   select Mod;
					return CategoryMods;
				}
				else
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

				if (rowObject.GetType() == typeof(ModCategory))
				{
					Val = ((IModCategory)rowObject).CategoryName;
				}
				else
					Val = ((IMod)rowObject).ModName;

				return Val;
			};

			tlcModName.AspectToStringConverter = delegate(object x)
			{
				return x.ToString();
			};

			tlcInstallDate.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				if (rowObject.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(((IMod)rowObject).InstallDate))
						Val = ((IMod)rowObject).InstallDate;
					if (CheckDate(Val))
						return Convert.ToDateTime(Val);
				}
				else
					return ((ModCategory)rowObject).NewMods.ToString();

				return null;
			};

			tlcInstallDate.AspectToStringConverter = delegate(object x)
			{
				int intCheck;
				if ((x != null) && (!Int32.TryParse(x.ToString(), out intCheck)))
				{
					return x.ToString();
				}
				else
					return String.Empty;
			};

			tlcDownloadDate.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				if (rowObject.GetType() != typeof(ModCategory))
				{
					string strFilePath = ((IMod)rowObject).Filename;
					if (!String.IsNullOrWhiteSpace(strFilePath))
						if (File.Exists(strFilePath))
							Val = File.GetLastWriteTime(((IMod)rowObject).Filename).ToString();
					if (CheckDate(Val))
						return Convert.ToDateTime(Val);
				}
				else
					return ((ModCategory)rowObject).NewMods.ToString();

				return null;
			};

			tlcDownloadDate.AspectToStringConverter = delegate(object x)
			{
				int intCheck;
				if ((x != null) && (!Int32.TryParse(x.ToString(), out intCheck)))
				{
					return x.ToString();
				}
				else
					return String.Empty;
			};

			tlcEndorsement.AspectGetter = delegate(object rowObject)
			{
				string Value = String.Empty;

				if (rowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)rowObject;
					if (modMod != null)
					{
						Value = modMod.IsEndorsed.ToString();
					}
				}

				return Value;
			};

			tlcEndorsement.AspectToStringConverter = delegate(object x)
			{
				return String.Empty;
			};

			tlcVersion.AspectGetter = delegate(object rowObject)
			{
				string Val = string.Empty;

				if (rowObject.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(((IMod)rowObject).HumanReadableVersion))
						Val = ((IMod)rowObject).HumanReadableVersion;
					return Val;
				}
				else
					return String.Empty;
			};

			tlcWebVersion.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				if (rowObject.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(((IMod)rowObject).LastKnownVersion))
						Val = ((IMod)rowObject).LastKnownVersion;
					return Val;
				}
				else
					return String.Empty;
			};

			tlcAuthor.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				if (rowObject.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(((IMod)rowObject).Author))
						Val = ((IMod)rowObject).Author;
					return Val;
				}
				else
					return String.Empty;
			};

			tlcCategory.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;

				if (rowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)rowObject;
					Val = CategoryManager.FindCategory(modMod.CustomCategoryId >= 0 ? modMod.CustomCategoryId : modMod.CategoryId).CategoryName;
				}

				return Val;
			};
		}

		/// <summary>
		/// Setup the Drag&Drop
		/// </summary>
		public void SetupDragAndDrop()
		{
			this.IsSimpleDragSource = true;
			this.IsSimpleDropSink = true;

			this.CanDrop += delegate(object sender, BrightIdeasSoftware.OlvDropEventArgs e)
			{
				e.Effect = DragDropEffects.Move;
			};

			this.Dropped += delegate(object sender, BrightIdeasSoftware.OlvDropEventArgs e)
			{
				string[] strFiles = e.DragEventArgs.Data.GetData(DataFormats.FileDrop) != null ? (string[])e.DragEventArgs.Data.GetData(DataFormats.FileDrop) : null;

				if (strFiles != null)
				{
					foreach (string strFilePath in strFiles)
						this.FileDropped(strFilePath, new EventArgs());
				}
				else if (e.DropTargetItem != null)
				{
					if (e.DropTargetItem.RowObject != null)
					{
						IModCategory imcCategory = null;

						if (e.DropTargetItem.RowObject.GetType() == typeof(ModCategory))
						{
							imcCategory = (IModCategory)e.DropTargetItem.RowObject;
						}
						else
						{
							try
							{
								IMod modMod = (IMod)e.DropTargetItem.RowObject;
								imcCategory = CategoryManager.FindCategory(modMod.CustomCategoryId >= 0 ? modMod.CustomCategoryId : modMod.CategoryId);
							}
							catch
							{
							}
						}

						if ((imcCategory != null) && (this.CategorySwitch != null))
						{
							this.CategorySwitch(imcCategory, new EventArgs());
						}
					}
				}
			};
		}

		/// <summary>
		/// Setup the Hyperlink Manager
		/// </summary>
		public void SetupHyperlinkManager()
		{
			tlcWebVersion.Hyperlink = true;
			this.IsHyperlink += delegate(object sender, BrightIdeasSoftware.IsHyperlinkEventArgs e)
			{
				try
				{
					if (e.Model.GetType() != typeof(ModCategory))
					{
						IModInfo mifInfo = (IModInfo)e.Model;
						if (!(mifInfo == null))
						{
							if (mifInfo.Website == null)
								e.Url = null;
							else
								e.Url = mifInfo.Website.ToString();
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
		/// <param name="p_booCategorySetup">Whether to setup the visibility for a category or a mod</param>
		/// <param name="p_strReadMeFiles">The readme files</param>
		public void SetupContextMenuFor(bool p_booCategorySetup, string[] p_strReadMeFiles)
		{
			if (p_booCategorySetup)
			{
				this.cmsContextMenu.Items[0].Visible = false;
				this.cmsContextMenu.Items[1].Visible = true;
				this.cmsContextMenu.Items[2].Visible = true;
				this.cmsContextMenu.Items[3].Visible = true;
				if (cmsContextMenu.Items.Count > 4)
					cmsContextMenu.Items.RemoveAt(4);
			}
			else
			{
				this.cmsContextMenu.Items[0].Visible = true;
				this.cmsContextMenu.Items[1].Visible = false;
				this.cmsContextMenu.Items[2].Visible = true;
				this.cmsContextMenu.Items[3].Visible = true;

				if (cmsContextMenu.Items.Count > 6)
					cmsContextMenu.Items.RemoveAt(6);

				if (cmsContextMenu.Items.Count > 5)
				cmsContextMenu.Items.RemoveAt(5);

				if (cmsContextMenu.Items.Count > 4)
					cmsContextMenu.Items.RemoveAt(4);
				if (p_strReadMeFiles != null)
				{
					cmsContextMenu.Items.Add("Open ReadMe file:", new Bitmap(Properties.Resources.text_x_generic, 16, 16));
					foreach (string strFile in p_strReadMeFiles)
						(cmsContextMenu.Items[4] as ToolStripMenuItem).DropDownItems.Add(strFile, new Bitmap(Properties.Resources.text_x_generic, 16, 16), new EventHandler(cmsContextMenu_OpenReadMeFile));
					this.cmsContextMenu.Items[4].Enabled = true;
				}
				else
				{
					cmsContextMenu.Items.Add("No ReadMe for this mod", new Bitmap(Properties.Resources.text_x_generic, 16, 16));
					this.cmsContextMenu.Items[4].Enabled = false;
				}
				this.cmsContextMenu.Items[4].Visible = true;

				cmsContextMenu.Items.Add("Uninstall Mod", new Bitmap(Properties.Resources.dialog_block, 16, 16), new EventHandler(cmsContextMenu_UninstallMod));
				this.cmsContextMenu.Items[5].Enabled = ((m_rolActiveMods != null) && (m_rolActiveMods.Count > 0) && (m_rolActiveMods.Contains(GetSelectedMod)));
				this.cmsContextMenu.Items[5].Visible = true;

				cmsContextMenu.Items.Add("Uninstall from all Profiles", new Bitmap(Properties.Resources.dialog_block, 16, 16), new EventHandler(cmsContextMenu_UninstallModFromProfiles));
				this.cmsContextMenu.Items[6].Enabled = ((m_rolActiveMods != null) && (m_rolActiveMods.Count > 0) && (m_rolActiveMods.Contains(GetSelectedMod)));
				this.cmsContextMenu.Items[6].Visible = true;
			}
		}

		/// <summary>
		/// Setup the Image Getters
		/// </summary>
		public void SetupImageGetters()
		{
			tlcWebVersion.ImageGetter = delegate(object rowObject)
			{
				if (rowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)rowObject;
					if (modMod != null)
					{
						if (modMod.UpdateWarningEnabled && (!modMod.IsMatchingVersion()))
							return new Bitmap(Properties.Resources.update_warning, 16, 16);
						else if ((!modMod.IsMatchingVersion()) && (!Settings.HideModUpdateWarningIcon))
							return new Bitmap(Properties.Resources.update_warning_disabled, 16, 16);
					}
				}
				else
				{
					int intCategoryId = ((IModCategory)rowObject).Id;

					if (GetOutdatedModList(intCategoryId).Count > 0)
						return new Bitmap(Properties.Resources.update_warning, 16, 16);
				}

				return null;
			};

			tlcModName.ImageGetter = delegate(object rowObject)
			{
				if (rowObject.GetType() == typeof(ModCategory))
				{
					return new Bitmap(CreateBitmapImage(GetCategoryModCount((IModCategory)rowObject).ToString(), Properties.Resources.category_folder, 14, 2, 4, 1, 5), 13, 15);
				}
				else
				{
					try
					{
						IVirtualModInfo vmiMod = VirtualMods.Where(x => x.ModFileName.ToLowerInvariant() == Path.GetFileName(((IMod)rowObject).Filename.ToLowerInvariant())).FirstOrDefault();
						if ((vmiMod != null) && (m_rolActiveMods.Contains((IMod)rowObject)))
							return new Bitmap(Properties.Resources.dialog_ok_4_16, 12, 12);
						else
							return new Bitmap(!m_rolActiveMods.Contains((IMod)rowObject) ? new Bitmap(12, 12) : Properties.Resources.dialog_block, 14, 14);
					}
					catch
					{
						return new Bitmap(!m_rolActiveMods.Contains((IMod)rowObject) ? new Bitmap(12, 12) : Properties.Resources.dialog_block, 14, 14);
					}
				}
			};

			tlcInstallDate.ImageGetter = delegate(object rowObject)
			{
				if (rowObject.GetType() == typeof(ModCategory))
				{
					if (((ModCategory)rowObject).NewMods > 0)
						return new Bitmap(CreateBitmapImage(((ModCategory)rowObject).NewMods.ToString(), Properties.Resources.AddFile_48x48, 16, 4, 4, -2, -1), 16, 16);
				}
				return null;
			};

			tlcEndorsement.ImageGetter = delegate(object rowObject)
			{
				if (rowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)rowObject;
					if (modMod != null)
					{
						if (modMod.IsEndorsed == true)
							return new Bitmap(Properties.Resources.thumb_up, 16, 16);
						else if (modMod.IsEndorsed == false)
							return new Bitmap(Properties.Resources.thumb_no, 16, 16);
					}
				}
				return null;
			};
		}

		#endregion

		#region Category Management

		/// <summary>
		/// Loads the TreeListView categories.
		/// </summary>
		public void LoadData()
		{
			if (this.Items.Count > 0)
			{
				this.ClearObjects();
			}

			if (CategoryModeEnabled)
			{
				tlcCategory.IsVisible = false;
				this.SetObjects(CategoryManager.Categories);
				ApplyFilters(null);
			}
			else
			{
				tlcCategory.IsVisible = true;
				this.SetObjects(m_rolManagedMods);
			}
		}

		/// <summary>
		/// Adds a new category to the TreeListView.
		/// </summary>
		/// <param name="p_imcCategory">The category to add to the roots list.</param>
		/// <param name="booIsNew">Whether the category is new or was just hidden.</param>
		public void AddData(IModCategory p_imcCategory, bool booIsNew)
		{
			ModCategory Category = new ModCategory(p_imcCategory);
			if (CategoryModeEnabled)
			{
				this.AddObject(Category);
				ApplyFilters(Category);
				if (this.Items.Count > 0)
					this.EnsureVisible(this.Items.Count - 1);
			}
		}

		/// <summary>
		/// Removes the category from the TreeListView.
		/// </summary>
		/// <param name="p_mctCategory">The category to remove.</param>
		public bool RemoveData(ModCategory p_mctCategory)
		{
			if (CategoryModeEnabled)
				if (this.Items.Count > 0)
					foreach (object Item in Roots)
						if (((ModCategory)Item).Equals(p_mctCategory))
						{
							RemoveObject(p_mctCategory);
							return true;
						}

			return false;
		}

		/// <summary>
		/// Refresh the category in the TreeListView.
		/// </summary>
		/// <param name="p_mctCategory">The category to refresh.</param>
		public bool RefreshData(ModCategory p_mctCategory)
		{
			if (CategoryModeEnabled)
				if (this.Items.Count > 0)
					foreach (object Item in Roots)
						if (((ModCategory)Item).Equals(p_mctCategory))
						{
							//RefreshObject(p_mctCategory);
							return true;
						}

			return false;
		}

		/// <summary>
		/// Update the category in the TreeListView.
		/// </summary>
		/// <param name="p_mctCategory">The category to update.</param>
		/// <param name="strOldValue">The previous category name.</param>
		public void UpdateData(ModCategory p_mctCategory, string strOldValue)
		{
			if (this.Items.Count > 0)
			{
				SetupContextMenu();
				//RefreshObject(p_mctCategory);
			}
		}

		/// <summary>
		/// Gets the mod count for the current category.
		/// </summary>
		/// <param name="p_imcCategory">The category to count.</param>
		public Int32 GetCategoryModCount(IModCategory p_imcCategory)
		{
			var CategoryMods = from Mod in m_rolManagedMods
							   where (Mod != null) && ((Mod.CustomCategoryId >= 0 ? Mod.CustomCategoryId : Mod.CategoryId) == p_imcCategory.Id)
							   select Mod;

			return CategoryMods.Count();
		}

		/// <summary>
		/// Gets the mod count for the current category.
		/// </summary>
		/// <param name="p_imcCategory">The category to count.</param>
		/// <param name="p_modItems">The Mod List containing the categories.</param>
		public Int32 GetCategoryModCount(IModCategory p_imcCategory, IEnumerable<IMod> p_modItems)
		{
			var CategoryMods = from Mod in p_modItems
							   where (Mod != null) && ((Mod.CustomCategoryId >= 0 ? Mod.CustomCategoryId : Mod.CategoryId) == p_imcCategory.Id)
							   select Mod;

			return CategoryMods.Count();
		}

		/// <summary>
		/// Removes the selected category.
		/// </summary>
		/// <param name="p_imcCategory">The category to remove.</param>
		public void RemoveCategory(IModCategory p_imcCategory)
		{
			if ((p_imcCategory != null) && (p_imcCategory.Id != 0))
			{
				CategoryManager.RemoveCategory(p_imcCategory);

				foreach (ToolStripDropDownItem Item in (cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems)
					if (Item.Text == p_imcCategory.CategoryName)
					{
						(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Remove(Item);
						break;
					}

				if (this.CategoryRemoved != null)
					this.CategoryRemoved(p_imcCategory, new EventArgs());
			}
		}

		/// <summary>
		/// Adds a new category.
		/// </summary>
		public void AddNewCategory()
		{
			this.AddData(CategoryManager.AddCategory(), true);
			this.SetupContextMenu();
		}

		/// <summary>
		/// Gets the list of outdated mods in the selected category.
		/// </summary>
		/// <param name="p_intCategoryID">The category ID.</param>
		public List<IMod> GetOutdatedModList(Int32 p_intCategoryID)
		{
			var CategoryMods = from Mod in m_rolManagedMods
							   where (Mod != null)
							   && Mod.UpdateWarningEnabled
							   && ((Mod.CustomCategoryId >= 0 ? Mod.CustomCategoryId : Mod.CategoryId) == p_intCategoryID)
							   && !Mod.IsMatchingVersion()
							   select Mod;

			return new List<IMod>(CategoryMods);
		}

		#endregion

		#region EventHandler

		/// <summary>
		/// Handles the cmsContextMenu.CategoryRemove event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_CategoryRemove(object sender, EventArgs e)
		{
			RemoveCategory(m_imcSelectedCategory);
		}

		/// <summary>
		/// Handles the cmsContextMenu.CategoryNew event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_CategoryNew(object sender, EventArgs e)
		{
			AddNewCategory();
		}

		/// <summary>
		/// Handles the cmsContextMenu.ToggleUpdateWarning event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_ToggleUpdateWarning(object sender, EventArgs e)
		{
			if (this.UpdateWarningToggle != null)
				this.UpdateWarningToggle(this, new EventArgs());
		}

		/// <summary>
		/// Handles the cmsContextMenu.EnableAllWarnings event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_EnableAllWarnings(object sender, EventArgs e)
		{
			if (this.ToggleAllWarnings != null)
				this.ToggleAllWarnings(true, new EventArgs());
		}

		/// <summary>
		/// Handles the cmsContextMenu.DisableAllWarnings event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_DisableAllWarnings(object sender, EventArgs e)
		{
			if (this.ToggleAllWarnings != null)
				this.ToggleAllWarnings(false, new EventArgs());
		}

		/// <summary>
		/// Handles the cmsContextMenu.ReadmeScan event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_ReadmeScan(object sender, EventArgs e)
		{
			if (this.ReadmeScan != null)
				this.ReadmeScan(this, new EventArgs());
		}

		/// <summary>
		/// Handles the cmsContextMenu.OpenReadMefile event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_OpenReadMeFile(object sender, EventArgs e)
		{
			if (this.OpenReadMeFile != null)
				this.OpenReadMeFile(sender, new EventArgs());
		}

		/// <summary>
		/// Handles the cmsContextMenu.UninstallMod event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_UninstallMod(object sender, EventArgs e)
		{
			if (this.UninstallMod != null)
				this.UninstallMod(GetSelectedMod, new EventArgs());
		}

		/// Handles the cmsContextMenu.UninstallMod event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_UninstallModFromProfiles(object sender, EventArgs e)
		{
			if (this.UninstallModFromProfiles != null)
				this.UninstallModFromProfiles(GetSelectedMod, new EventArgs());
		}

		/// <summary>
		/// Handles the cmsContextMenu.CategoryClicked event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_CategoryClicked(object sender, EventArgs e)
		{
			ToolStripItem item = sender as ToolStripItem;

			if (this.SelectedObjects.Count > 0)
			{
				if (this.CategorySwitch != null)
					this.CategorySwitch((IModCategory)Categories.Find(Item => Item.CategoryName == item.Text), new EventArgs());
			}
		}

		#endregion

		/// <summary>
		/// Applies the default list filters
		/// </summary>
		private void ApplyFilters(ModCategory p_mctModCategory)
		{
			this.ModelFilter = new ModelFilter(delegate(object x)
			{
				if ((x.GetType() == typeof(ModCategory)) && !ShowHiddenCategories)
					if ((p_mctModCategory != null) && ((ModCategory)x == p_mctModCategory))
						return true;
					else if ((Categories.Count > 1) && (GetCategoryModCount((ModCategory)x) <= 0))
						return false;

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
			if (CategoryModeEnabled)
				ApplyFilters(null);
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
			if (this.Columns.Count > 0)
			{
				tmfFilter.Columns = new OLVColumn[] { (OLVColumn)this.Columns[0] };
				HighlightTextRenderer highlightingRenderer = this.GetColumn(0).Renderer as HighlightTextRenderer;
				if (highlightingRenderer != null)
					highlightingRenderer.Filter = tmfFilter;
			}
			m_strLastSearchFilter = String.Empty;
		}

		/// <summary>
		//Resets the Columns to the original width
		/// </summary>
		public void ResetColumns()
		{
			tlcModName.Width = 100;
			tlcCategory.Width = 100;
			tlcInstallDate.Width = 100;
			tlcEndorsement.Width = 100;
			tlcVersion.Width = 100;
			tlcWebVersion.Width = 100;
			tlcAuthor.Width = 100;
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
		/// This sets the sort column for the category view control.
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
		/// This sets the sort column for the category view control.
		/// </summary>
		public void SetSecondarySortColumn()
		{
			this.SecondarySortColumn = tlcModName;
			this.SecondarySortOrder = SortOrder.Ascending;
		}
	}
}
