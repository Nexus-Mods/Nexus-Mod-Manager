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
	public partial class CategoryListView : TreeListView
    {
		private ReadOnlyObservableList<IMod> m_rolManagedMods;
		private ReadOnlyObservableList<IMod> m_rolActiveMods;
		private IModRepository m_mmrModRepository;
		private bool m_booCategoryMode = true;
		private string m_strLastSearchFilter = String.Empty;
		private ToolStripMenuItem m_mniCategories;
		private ToolStripMenuItem m_mniCategoryMoveTo;
		private ToolStripMenuItem m_mniModActivate;
		private ToolStripMenuItem m_mniModReinstall;
		private ToolStripMenuItem m_mniModDeactivate;
		private ToolStripMenuItem m_mniModUninstall;
		private ToolStripMenuItem m_mniModReadme;
		private ToolStripMenuItem m_mniModWarnings;


		#region Custom Events

		/// <summary>
		/// Occurs whenever all mods "warning update" status toggled.
		/// </summary>
		public event EventHandler<ModUpdateWarningEventArgs> AllUpdateWarningsToggled;
		public event EventHandler CategorySwitch;
		public event EventHandler CategoryRemoved;
		/// <summary>
		/// Occurs whenever "show empty categories" status toggled.
		/// </summary>
		public event EventHandler CategoryShowEmptyToggled;
		public event EventHandler FileDropped;
		/// <summary>
		/// Occurs whenever the user performs an action on the selected mod.
		/// </summary>
		public event EventHandler<ModActionEventArgs> ModActionRequested;
		/// <summary>
		/// Occurs whenever the selected mod's information is requested.
		/// </summary>
		public event EventHandler<ModInfoRequestEventArgs> ModInfoRequested;
		/// <summary>
		/// Occurs whenever the selected mod's readme file is being opened.
		/// </summary>
		public event EventHandler<ModReadmeRequestEventArgs> ModReadmeFileRequested;
		public event EventHandler ReadmeScan;
		/// <summary>
		/// Occurs whenever selected mod's "warning update" status toggled.
		/// </summary>
		public event EventHandler<ModUpdateWarningEventArgs> UpdateWarningToggled;

		#endregion

		#region Properties

		/// <summary>
		/// Indicates whether all categories are collapsed.
		/// </summary>
		public bool AllCategoriesCollapsed
		{
			get
			{
				return !this.TreeModel.TreeView.ExpandedObjects.Cast<object>().Any();
			}
		}

		/// <summary>
		/// Indicates whether all categories are expanded.
		/// </summary>
		public bool AllCategoriesExpanded
		{
			get
			{
				var total = this.TreeModel.RootObjects.Cast<object>().Count();
				return this.TreeModel.TreeView.ExpandedObjects.Cast<object>().Count() == total;
			}
		}

		/// <summary>
		/// Gets or sets whether to show the hidden categories (no mods assigned).
		/// </summary>
		/// <value>Whether to show the hidden categories (no mods assigned).</value>
		public bool ShowHiddenCategories { get; set; }

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
		public IMod SelectedMod
		{
			get
			{
				if (GetSelectedItem.GetType() != typeof(ModCategory))
					return (IMod)this.GetSelectedItem;
				else
					return null;
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
				if (this.SelectedObjects.Count != 1)
				{
					return null;
				}
				return this.SelectedObjects[0] as ModCategory;
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
			ShowHiddenCategories = false;
			InitializeComponent();
			tlcModName.Name = "ModName";
			tlcInstallDate.Name = "InstallDate";
			tlcDownloadDate.Name = "DownloadDate";
			tlcWebVersion.Name = "WebVersion";
			tlcAuthor.Name = "Author";
            tlcLoadOrder.Name = "LoadOrder";
			tlcEndorsement.Name = "Endorsement";
			tlcDownloadId.Name = "DownloadId";
			tlcCategory.Name = "Category";
			tlcModName.AspectName = "Text";
			tlcInstallDate.AspectName = "Text";
			tlcDownloadDate.AspectName = "Text";
			tlcWebVersion.AspectName = "Text";
			tlcAuthor.AspectName = "Text";
			tlcCategory.AspectName = "Text";

			this.cmsContextMenu.Opening += cmsContextMenu_Opening;
			SetupContextMenu();
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
			RefreshContextMenuCategoryList();

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

			tlcDownloadId.AspectGetter = delegate (object rowObject)
			{
				string Value = String.Empty;

				if (rowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)rowObject;
					if (modMod != null)
					{
						if (modMod.DownloadId != null)
							Value = modMod.DownloadId.ToString();
					}
				}

				return Value;
			};

			tlcDownloadId.AspectToStringConverter = delegate (object x)
			{
				return x.ToString();
			};

			tlcWebVersion.AspectGetter = delegate (object rowObject)
			{
				string Val = "?";
				string Local = "?";
				string Online = "?";

				if (rowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = ((IMod)rowObject);
					if (!string.IsNullOrEmpty(modMod.HumanReadableVersion))
						Local = modMod.HumanReadableVersion;

					if (!string.IsNullOrEmpty(modMod.LastKnownVersion))
						Online = modMod.LastKnownVersion;

					if (!(Local.Equals(Online) && Local.Equals("?")))
					{
						Val = string.Format("{0} / {1}", Local, Online);
					}

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

            tlcLoadOrder.AspectGetter = delegate (object rowObject)
            {
                if(rowObject.GetType() != typeof(ModCategory))
                {
                    IMod modMod = (IMod)rowObject;
                    return modMod.NewPlaceInModLoadOrder;
                }

                return -1;
            };

            tlcLoadOrder.AspectToStringConverter = delegate (object x)
            {
                return x.ToString() == "-1" ? "None" : x.ToString();
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

			tlcDownloadId.ImageGetter = delegate (object rowObject)
			{
				if (rowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)rowObject;
					if (modMod != null)
					{
						if ((modMod.DownloadId == "0") || (modMod.DownloadId == "-1") || (string.IsNullOrEmpty(modMod.DownloadId)))
							return new Bitmap(Properties.Resources.help_book, 16, 16);
						else
							return modMod.DownloadId;
					}
				}
				return null;
			};
		}

		#endregion

		#region Category Management

		/// <summary>
		/// Collapses all categories.
		/// </summary>
		public void CollapseAllCategories()
		{
			this.CollapseAll();
			Settings.ShowExpandedCategories = false;
			Settings.Save();
		}

		/// <summary>
		/// Expands all categories.
		/// </summary>
		public void ExpandAllCategories()
		{
			this.ExpandAll();
			Settings.ShowExpandedCategories = true;
			Settings.Save();
		}

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
		/// Shows or hides empty categories.
		/// </summary>
		public void ToggleShowEmptyCategories()
		{
			Settings.ShowEmptyCategory = !Settings.ShowEmptyCategory;
			Settings.Save();

			// update UI
			this.ShowHiddenCategories = Settings.ShowEmptyCategory;
			this.ReloadList(false);

			this.OnCategoryShowEmptyToggled(EventArgs.Empty);
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
				RefreshContextMenuCategoryList();
				//RefreshObject(p_mctCategory);
			}
		}

		/// <summary>
		/// Gets the mod count for the current category.
		/// </summary>
		/// <param name="p_imcCategory">The category to count.</param>
		private Int32 GetCategoryModCount(IModCategory p_imcCategory)
		{
			if (p_imcCategory == null)
			{
				return 0;
			}
			return GetCategoryMods(p_imcCategory).Count;
		}

		/// <summary>
		/// Gets the list of mods in the current category.
		/// </summary>
		/// <param name="p_imcCategory">The category.</param>
		private List<IMod> GetCategoryMods(IModCategory p_imcCategory)
		{
			if (p_imcCategory == null)
			{
				return new List<IMod>();
			}
			return m_rolManagedMods.Where(mod => mod != null && (mod.CustomCategoryId >= 0 ? mod.CustomCategoryId : mod.CategoryId) == p_imcCategory.Id).ToList();
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

                this.CategoryRemoved?.Invoke(p_imcCategory, new EventArgs());
            }
		}

		/// <summary>
		/// Adds a new category.
		/// </summary>
		public void AddNewCategory()
		{
			this.AddData(CategoryManager.AddCategory(), true);
			this.RefreshContextMenuCategoryList();
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

		#region Context Menu Management

		/// <summary>
		/// Setup the CategoryView context menu
		/// </summary>
		public void RefreshContextMenuCategoryList()
		{
			m_mniCategoryMoveTo.DropDownItems.Clear();
			foreach (IModCategory imcCategory in Categories.OrderBy(x => x.CategoryName))
			{
				var item = new ToolStripMenuItem(imcCategory.CategoryName, null, cmsContextMenu_CategoryClicked) { Tag = imcCategory.Id };
				m_mniCategoryMoveTo.DropDownItems.Add(item);
			}
		}

		/// <summary>
		/// Populates "Category -> expand/collapse" sub-items as appropriate.
		/// </summary>
		private void RenderContextMenuCategoryExpandCollapse()
		{
			var hasExpanded = !this.AllCategoriesCollapsed;
			var hasCollpased = !this.AllCategoriesExpanded;

			if (hasCollpased || hasExpanded)
			{
				m_mniCategories.DropDownItems.Add("-");
			}
			if (hasCollpased)
			{
				m_mniCategories.DropDownItems.Add("Expand all", null, (s, e) => this.ExpandAllCategories());
			}
			if (hasExpanded)
			{
				m_mniCategories.DropDownItems.Add("Collapse all", null, (s, e) => this.CollapseAllCategories());
			}

			if (hasCollpased || hasExpanded)
			{
				m_mniCategories.DropDownItems.Add("-");
			}
			m_mniCategories.DropDownItems.Add(this.ShowHiddenCategories ? "Hide empty" : "Show empty",
				this.ShowHiddenCategories ? new Bitmap(Properties.Resources.eye_open2_24x24, 16, 16) : new Bitmap(Properties.Resources.eye_open_24x24, 16, 16), 
				(s, e) => this.ToggleShowEmptyCategories());
		}

		/// <summary>
		/// Populates "Deactivate -> ..." sub-items as appropriate.
		/// </summary>
		private void RenderContextMenuModUninstall()
		{
			m_mniModUninstall.DropDownItems.Add("From active profile", null,
				(s, e) => this.OnModActionRequested(ModAction.Uninstall));
			m_mniModUninstall.DropDownItems.Add("From all profiles", null,
				(s, e) => this.OnModActionRequested(ModAction.UninstallAll));
			m_mniModUninstall.DropDownItems.Add("-");
			m_mniModUninstall.DropDownItems.Add("Delete mod (permanently) and uninstall.", new Bitmap(Properties.Resources.dialog_cancel_4_16, 16, 16),
				(s, e) => this.OnModActionRequested(ModAction.Delete));
		}

		/// <summary>
		/// Populates "Open readme" sub-items as appropriate.
		/// </summary>
		private void RenderContextMenuModReadme()
		{
			m_mniModReadme.DropDownItems.Clear();
			m_mniModReadme.Click -= cmsContextMenu_OpenReadMeFile;
			m_mniModReadme.Tag = null;

			var args = new ModInfoRequestEventArgs(this.SelectedMod);
			this.OnModInfoRequested(args);

			// if there is only one readme file then allow the user to click on the context menu instead of showing a dropdown
			// else put all readme's in a dropdown list
			if (args.ReadmeFiles.Count == 1)
			{
				m_mniModReadme.Tag = args.ReadmeFiles[0];
				m_mniModReadme.Click += cmsContextMenu_OpenReadMeFile;
			}
			else if (args.ReadmeFiles.Count > 1)
			{
				args.ReadmeFiles.ForEach(f =>
				{
					var item = m_mniModReadme.DropDownItems.Add(f, new Bitmap(Properties.Resources.text_x_generic, 16, 16), cmsContextMenu_OpenReadMeFile);
					item.Tag = f;
				});
			}
			m_mniModReadme.Enabled = args.ReadmeFiles.Count > 0;
		}

		/// <summary>
		/// Populates "Mod update warnings -> ..." sub-items as appropriate.
		/// </summary>
		private void RenderContextMenuModWarningsToggle(List<IMod> p_lstMods)
		{
			if (p_lstMods == null || !p_lstMods.Any())
			{
				return;
			}

			if (p_lstMods.Count == 1)
			{
				m_mniModWarnings.DropDownItems.Add(p_lstMods[0].UpdateWarningEnabled ? "Disable update warning" : "Enable update warning",
					null,
					(s, e) => this.OnUpdateWarningToggled(new ModUpdateWarningEventArgs(!p_lstMods[0].UpdateWarningEnabled)));
			}
			else
			{
				// for some reason linq over mods yield incorrect results, so use the conventional foreach loop...
				bool hasEnabledWarnings = false;
				bool hasDisabledWarnings = false;
				foreach (var mod in p_lstMods)
				{
					if (mod.UpdateWarningEnabled)
					{
						hasEnabledWarnings = true;
					}
					else
					{
						hasDisabledWarnings = true;
					}
				}

				if ((hasEnabledWarnings || hasDisabledWarnings) && m_mniModWarnings.DropDownItems.Count > 0)
				{
					m_mniModWarnings.DropDownItems.Add("-");
				}
				if (hasDisabledWarnings)
				{
					m_mniModWarnings.DropDownItems.Add("Enable for selected files", null,
						(s, e) => this.OnUpdateWarningToggled(new ModUpdateWarningEventArgs(true)));
				}
				if (hasEnabledWarnings)
				{
					m_mniModWarnings.DropDownItems.Add("Disable for selected files", null,
						(s, e) => this.OnUpdateWarningToggled(new ModUpdateWarningEventArgs(false)));
				}
			}

			if (m_mniModWarnings.DropDownItems.Count > 0)
			{
				m_mniModWarnings.DropDownItems.Add("-");
			}
			m_mniModWarnings.DropDownItems.Add("Enable for all files", null,
				(s, e) => this.OnAllUpdateWarningsToggled(new ModUpdateWarningEventArgs(true)));
			m_mniModWarnings.DropDownItems.Add("Disable for all files", null,
				(s, e) => this.OnAllUpdateWarningsToggled(new ModUpdateWarningEventArgs(false)));
		}

		/// <summary>
		/// Prepares the context menu for the currently selected categories.
		/// </summary>
		private void SetupCategoryContextMenu()
		{
			// validate
			if (!this.SelectedObjects.OfType<object>().All(o => o is ModCategory))
			{
				return;
			}

			cmsContextMenu.Items.Add(m_mniCategories);

			if (this.SelectedObjects.Count == 1)
			{
				// single category management
				// can:
				// - add
				// - remove
				// - toggle update warnings
				// - toggle collapse/expand all

				m_mniCategories.DropDownItems.Add("Add new", new Bitmap(Properties.Resources.edit_add_4, 16, 16), cmsContextMenu_CategoryNew);
				m_mniCategories.DropDownItems.Add("Delete", new Bitmap(Properties.Resources.edit_delete_6, 16, 16), cmsContextMenu_CategoryRemove);

				if (GetCategoryModCount(this.SelectedCategory) > 0)
				{
					cmsContextMenu.Items.Add("-");
					cmsContextMenu.Items.Add(m_mniModWarnings);
				}
			}
			else
			{
				// multi-category management
				// can:
				// - delete
				// - toggle update warnings
				// - toggle collapse/expand all

				m_mniCategories.DropDownItems.Add("Delete", new Bitmap(Properties.Resources.edit_delete_6, 16, 16), cmsContextMenu_CategoryRemove);
			}

			// shared - toggle update warnings
			var mods = new List<IMod>();
			this.SelectedObjects.OfType<ModCategory>().ToList().ForEach(modCategory => mods.AddRange(GetCategoryMods(modCategory)));
			this.RenderContextMenuModWarningsToggle(mods);

			// shared - facilitate expand/collapse all categories
			RenderContextMenuCategoryExpandCollapse();
		}

		/// <summary>
		/// Setup the CategoryView context menu
		/// </summary>
		private void SetupContextMenu()
		{
			// menu items with sub-items, sub-items will be populated at the run-time
			m_mniCategoryMoveTo = new ToolStripMenuItem("Move to");
			m_mniCategoryMoveTo.DropDownOpening += m_mniCategoryMoveTo_DropDownOpening;
			m_mniCategories = new ToolStripMenuItem("Category", new Bitmap(Properties.Resources.format_line_spacing_triple, 16, 16));
			m_mniModWarnings = new ToolStripMenuItem("Mod update warnings", new Bitmap(Properties.Resources.update_warning, 16, 16));
			m_mniModUninstall = new ToolStripMenuItem("Uninstall or Delete", new Bitmap(Properties.Resources.dialog_block, 16, 16));

			// menu items without any sub-items
			m_mniModActivate = new ToolStripMenuItem("Activate", new Bitmap(Properties.Resources.dialog_ok_4_16, 16, 16),
				(s, e) => this.OnModActionRequested(ModAction.Activate));
			m_mniModDeactivate = new ToolStripMenuItem("Deactivate", ToolStripRenderer.CreateDisabledImage(new Bitmap(Properties.Resources.dialog_ok_4_16, 16, 16)),
				(s, e) => this.OnModActionRequested(ModAction.Deactivate));
			m_mniModReinstall = new ToolStripMenuItem("Reinstall Mod", new Bitmap(Properties.Resources.change_game_mode, 16, 16),
				(s, e) => this.OnModActionRequested(ModAction.Reinstall));
			m_mniModReadme = new ToolStripMenuItem("Open readme", new Bitmap(Properties.Resources.text_x_generic, 16, 16));
		}

		/// <summary>
		/// Prepares the context menu for the currently selected mods.
		/// </summary>
		private void SetupModContextMenu()
		{
			// validate
			if (!this.SelectedObjects.OfType<object>().All(o => o is IMod))
			{
				return;
			}

			if (this.SelectedObjects.Count == 1)
			{
				// single mod management
				// can:
				// - display mod's filename
				// - display mod's readme(s), if any
				// - activate or deactivate 
				// - uninstall (from active profile, all profiles or permanently delete the archive)
				// - move to another category

				var menuItem = cmsContextMenu.Items.Add(SelectedMod != null ? Path.GetFileName(SelectedMod.Filename) : "Unable to retrieve the mod's filename",
					new Bitmap(Properties.Resources.document_save, 16, 16));
				menuItem.Enabled = false;
				cmsContextMenu.Items.Add("-");

				cmsContextMenu.Items.Add(m_mniModReadme);
				RenderContextMenuModReadme();

				// if mod is active - then allow its deactivation, else - activation
				if (!IsModInstalled(this.SelectedMod))
				{
					m_mniModActivate.Text = @"Install and activate";
					cmsContextMenu.Items.Add(m_mniModActivate);
				}
				else if (!IsModActive(this.SelectedMod))
				{
					m_mniModActivate.Text = @"Activate";
					cmsContextMenu.Items.Add(m_mniModActivate);
				}
				else
				{
					cmsContextMenu.Items.Add(m_mniModDeactivate);
					cmsContextMenu.Items.Add(m_mniModReinstall);
                }

				RenderContextMenuModUninstall();
				if (m_mniModUninstall.DropDownItems.Count > 0)
				{
					cmsContextMenu.Items.Add(m_mniModUninstall);
				}
			}
			else
			{
				// multi-mod management
				// can:
				// - move to another category
			}

			// for both single and multi-mod management can:
			// - manage "update warnings"
			this.RenderContextMenuModWarningsToggle(this.SelectedObjects.OfType<IMod>().ToList());
			if (m_mniModWarnings.DropDownItems.Count > 0)
			{
				cmsContextMenu.Items.Add(m_mniModWarnings);
			}

			if (m_mniCategoryMoveTo.DropDownItems.Count > 0)
			{
				cmsContextMenu.Items.Add("-");
				cmsContextMenu.Items.Add(m_mniCategoryMoveTo);
			}
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
			this.RemoveCategory(this.SelectedCategory);
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
		/// Handles the cmsContextMenu.Opening event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
		{
			cmsContextMenu.Items.Clear();
			m_mniCategories.DropDownItems.Clear();
			m_mniModWarnings.DropDownItems.Clear();
			m_mniModUninstall.DropDownItems.Clear();

			if (this.SelectedObjects.Count < 1)
			{
				e.Cancel = true;
				return;
			}

			// we can also tailor context menu for homogeneous selections - i.e. for multiple selected categories or mods
			if (this.SelectedObjects.OfType<object>().All(o => o is ModCategory))
			{
				this.SetupCategoryContextMenu();
				return;
			}

			if (this.SelectedObjects.OfType<object>().All(o => o is IMod))
			{
				this.SetupModContextMenu();
				return;
			}

			// heterogeneous selection - show no context menu
			e.Cancel = true;
		}

		/// <summary>
		/// Handles the cmsContextMenu.OpenReadMefile event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_OpenReadMeFile(object sender, EventArgs e)
		{
			if (this.SelectedMod == null || 
				!(sender is ToolStripMenuItem) || 
				(sender != m_mniModReadme && ((ToolStripMenuItem)sender).OwnerItem != m_mniModReadme))
			{
				return;
			}

			var fileName = ((ToolStripMenuItem)sender).Tag as string;
			if (string.IsNullOrWhiteSpace(fileName))
			{
				return;
			}

			this.OnModReadmeFileRequested(new ModReadmeRequestEventArgs(this.SelectedMod, fileName));
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
                this.CategorySwitch?.Invoke(Categories.Find(Item => Item.CategoryName == item.Text), new EventArgs());
            }
		}

		/// <summary>
		/// Intercepts opening of the mods category context menu drop down list and disables the current mods category.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void m_mniCategoryMoveTo_DropDownOpening(object sender, EventArgs e)
		{
			var selectedMod = this.SelectedMod;
			if (selectedMod == null)
			{
				return;
			}

			var selectedModCategoryId = selectedMod.CustomCategoryId > 0 ? selectedMod.CustomCategoryId : selectedMod.CategoryId;

			foreach (ToolStripItem item in this.m_mniCategoryMoveTo.DropDownItems)
			{
				var categoryId = item.Tag as int?;
				if (!categoryId.HasValue)
				{
					continue;
				}

				item.Enabled = categoryId != selectedModCategoryId;
				if (categoryId == selectedMod.CategoryId)
				{
					item.Image = Properties.Resources.home_16x16;
				}
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
				var modCategory = x as ModCategory;
				if (modCategory != null && !ShowHiddenCategories)
				{
					if (modCategory.Equals(p_mctModCategory))
					{
						return true;
					}
					if (Categories.Count > 1 && GetCategoryModCount(modCategory) < 1)
					{
						return false;
					}
				}
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
			{
				ApplyFilters(null);
			}

			if (p_booApplySearchFilter)
			{
				AddStringFilter(m_strLastSearchFilter);
			}
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
		/// Resets the Columns to the original width
		/// </summary>
		public void ResetColumns()
		{
			tlcModName.Width = 100;
			tlcCategory.Width = 100;
			tlcInstallDate.Width = 100;
			tlcEndorsement.Width = 100;
			tlcDownloadId.Width = 100;
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
			if (string.IsNullOrWhiteSpace(p_strDate))
			{
				return false;
			}
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
		/// Checks if the specified mod is active.
		/// </summary>
		/// <param name="p_modMod">The mod to check.</param>
		/// <returns><see langword="true"/> if the mod is present in the collection of active mods.</returns>
		private bool IsModActive(IMod p_modMod)
		{
			if (p_modMod == null)
			{
				return false;
			}
			return VirtualModActivator.ActiveModList.Contains(Path.GetFileName(p_modMod.Filename).ToLowerInvariant());
		}

		/// <summary>
		/// Checks if the specified mod is installed (i.e. the acrhive has been unpacked into a Virtual Install folder).
		/// </summary>
		/// <param name="p_modMod">The mod to check.</param>
		/// <returns><see langword="true"/> if the mod is present in the collection of active mods.</returns>
		private bool IsModInstalled(IMod p_modMod)
		{
			if (p_modMod == null)
			{
				return false;
			}
			return m_rolActiveMods != null &&
				m_rolActiveMods.Count > 0 &&
				m_rolActiveMods.Contains(p_modMod);
		}

		/// <summary>
		/// Raises <see cref="AllUpdateWarningsToggled"/> event with supplied arguments.
		/// </summary>
		/// <param name="e"></param>
		private void OnAllUpdateWarningsToggled(ModUpdateWarningEventArgs e)
		{
            this.AllUpdateWarningsToggled?.Invoke(this, e);
        }

		/// <summary>
		/// Raises <see cref="CategoryShowEmptyToggled"/> event with supplied arguments.
		/// </summary>
		/// <param name="e"></param>
		private void OnCategoryShowEmptyToggled(EventArgs e)
		{
            this.CategoryShowEmptyToggled?.Invoke(this, e);
        }

		/// <summary>
		/// Raises <see cref="ModActionRequested"/> event with supplied arguments.
		/// </summary>
		/// <param name="p_action">Action to perform on selected mod.</param>
		private void OnModActionRequested(ModAction p_action)
		{
			if (this.SelectedMod == null)
			{
				return;
			}
            this.ModActionRequested?.Invoke(this, new ModActionEventArgs(this.SelectedMod, p_action));
        }

		/// <summary>
		/// Raises <see cref="ModInfoRequested"/> event with supplied arguments.
		/// </summary>
		/// <param name="e"></param>
		private void OnModInfoRequested(ModInfoRequestEventArgs e)
		{
            this.ModInfoRequested?.Invoke(this, e);
        }

		/// <summary>
		/// Raises <see cref="ModReadmeFileRequested"/> event with supplied arguments.
		/// </summary>
		/// <param name="e"></param>
		private void OnModReadmeFileRequested(ModReadmeRequestEventArgs e)
		{
            this.ModReadmeFileRequested?.Invoke(this, e);
        }

		/// <summary>
		/// Raises <see cref="UpdateWarningToggled"/> event with supplied arguments.
		/// </summary>
		/// <param name="e"></param>
		private void OnUpdateWarningToggled(ModUpdateWarningEventArgs e)
		{
            this.UpdateWarningToggled?.Invoke(this, e);
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
