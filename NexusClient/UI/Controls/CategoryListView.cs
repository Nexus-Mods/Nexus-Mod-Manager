using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;
using Nexus.UI.Controls;

using BrightIdeasSoftware.Design;

namespace Nexus.Client.UI.Controls
{
	public partial class CategoryListView : BrightIdeasSoftware.TreeListView
	{
		IconListView m_lvwList = null;
		IMod m_modSelectedMod = null;
		IModCategory m_imcSelectedCategory = null;

		#region Custom Events

		public event EventHandler CategorySwitch;
		public event EventHandler CategoryRemoved;

		#endregion

		#region Properties

		public ListViewItem GetSelectedItem
		{
			get
			{
				return (ListViewItem)this.SelectedItem.RowObject;
			}
		}

		public IMod GetSelectedMod
		{
			get
			{
				return m_modSelectedMod;
			}
		}
		
		protected IEnumerable<ListViewItem> lviCategoryItems
		{
			get
			{
				return m_lvwList.Items.Cast<ListViewItem>();
			}
		}

		protected CategoryManager CategoryManager { get; private set; }

		protected ThreadSafeObservableList<IModCategory> Categories
		{
			get
			{
				return CategoryManager.Categories;
			}
		}

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
			tlcVersion.Name = "HumanReadableVersion";
			tlcWebVersion.Name = "WebVersion";
			tlcAuthor.Name = "Author";
			tlcEndorsement.Name = "Endorsement";
			tlcModName.AspectName = "Text";
			tlcInstallDate.AspectName = "Text";
			tlcVersion.AspectName = "Text";
			tlcWebVersion.AspectName = "Text";
			tlcAuthor.AspectName = "Text";
		}

		#endregion

		#region TreeListView Setup

		/// <summary>
		/// Setup the CategoryView
		/// </summary>
		/// <param name="p_lvwList">The source list view.</param>
		/// <param name="p_cmgCategoryManager">The mod Category Manager.</param>
		public void Setup(IconListView p_lvwList, CategoryManager p_cmgCategoryManager)
		{
			this.Tag = false;

			this.CellEditActivation = CellEditActivateMode.SingleClick;

			// TODO Category: Check if valid
			m_lvwList = p_lvwList;
			CategoryManager = p_cmgCategoryManager;

			// Setup menuStrip commands
			SetupContextMenu();

			// Setup category validator
			SetupCategoryValidator();

			// Setup category sorter
			SetupCategorySorter();

			this.BooleanCheckStateGetter = delegate(object x)
			{
				ListViewItem lviItem = (ListViewItem)x;
				return lviItem.Checked;
			};

			// Setup AspectGetter (IconListView cell parser)
			SetupColumnParser();

			// Setup hyperlink manager
			SetupHyperlinkManager();

			// Setup mouse events
			SetupMouseEvents();

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
			cmsContextMenu.Items.Add("Move to Category:");
			cmsContextMenu.Items.Add("Categories:");
			(cmsContextMenu.Items[1] as ToolStripMenuItem).DropDownItems.Add("New", null, new EventHandler(cmsContextMenu_CategoryNew));
			(cmsContextMenu.Items[1] as ToolStripMenuItem).DropDownItems.Add("Remove selected", null, new EventHandler(cmsContextMenu_CategoryRemove));

			foreach (IModCategory imcCategory in Categories)
				(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Add(imcCategory.CategoryName, null, new EventHandler(cmsContextMenu_CategoryClicked));
		}

		/// <summary>
		/// Setup the CategoryView category validator
		/// </summary>
		public void SetupCategoryValidator()
		{
			this.CanExpandGetter = delegate(object x)
			{
				ListViewItem lviItem = (ListViewItem)x;
				return ((lviItem.Tag).GetType() == typeof(ModCategory));
			};
		}

		/// <summary>
		/// Setup the CategoryView mod sorter
		/// </summary>
		public void SetupCategorySorter()
		{
			this.ChildrenGetter = delegate(object x)
			{
				ListViewItem Item = (ListViewItem)x;
				object lviItem = Item.Tag; 
				if (lviItem.GetType() == typeof(ModCategory))
				{
					var CategoryMods = from Mod in lviCategoryItems
						where ((IMod)Mod.Tag != null) && ((((IMod)Mod.Tag).CustomCategoryId >= 0 ? ((IMod)Mod.Tag).CustomCategoryId : ((IMod)Mod.Tag).CategoryId) == ((IModCategory)lviItem).Id)
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
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() == typeof(ModCategory))
				{
					Val = ((IModCategory)lviItem.Tag).CategoryName;
					if (!String.IsNullOrEmpty(Val))
						Val += " (" + GetCategoryModCount((IModCategory)lviItem.Tag, lviCategoryItems) + ")";
				}
				else
					Val = ((IMod)lviItem.Tag).ModName;

				return Val;
			};

			tlcInstallDate.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(lviItem.SubItems[tlcInstallDate.Name].Text))
						Val = lviItem.SubItems[tlcInstallDate.Name].Text;
					return Val;
				}
				else
					return String.Empty;
			};

			tlcInstallDate.AspectGetter = delegate(object rowObject)
			{
				string Val = "<No Data>";
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(lviItem.SubItems[tlcInstallDate.Name].Text))
						Val = lviItem.SubItems[tlcInstallDate.Name].Text;
					return Val;
				}
				else
					return String.Empty;
			};

			tlcVersion.AspectGetter = delegate(object rowObject)
			{
				string Val = "<No Data>";
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(lviItem.SubItems[tlcVersion.Name].Text))
						Val = lviItem.SubItems[tlcVersion.Name].Text;
					return Val;
				}
				else
					return String.Empty;
			};

			tlcWebVersion.AspectGetter = delegate(object rowObject)
			{
				string Val = "<No Data>";
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(lviItem.SubItems[tlcWebVersion.Name].Text))
						Val = lviItem.SubItems[tlcWebVersion.Name].Text;
					return Val;
				}
				else
					return String.Empty;
			};

			tlcAuthor.AspectGetter = delegate(object rowObject)
			{
				string Val = "<No Data>";
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(lviItem.SubItems[tlcAuthor.Name].Text))
						Val = lviItem.SubItems[tlcAuthor.Name].Text;
					return Val;
				}
				else
					return String.Empty;
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
					IModInfo mifInfo = (IModInfo)((ListViewItem)(((BrightIdeasSoftware.IsHyperlinkEventArgs)e).Model)).Tag;
					if (!(mifInfo == null))
					{
						if (mifInfo.Website == null)
							e.Url = null;
						else
							e.Url = mifInfo.Website.ToString();
					}
				}
				catch
				{
					e.Url = null;
				}
			};
		}

		/// <summary>
		/// Setup the Mouse Events
		/// </summary>
		public void SetupMouseEvents()
		{
			this.CellRightClick += delegate(object sender, BrightIdeasSoftware.CellRightClickEventArgs e)
			{
				if (((ListViewItem)e.Item.RowObject).Tag.GetType() == typeof(ModCategory))
				{
					this.cmsContextMenu.Items[0].Visible = false;
					this.cmsContextMenu.Items[1].Visible = true;
					m_imcSelectedCategory = (ModCategory)((ListViewItem)e.Item.RowObject).Tag; 
				}
				else
				{
					this.cmsContextMenu.Items[0].Visible = true;
					this.cmsContextMenu.Items[1].Visible = false;
					m_modSelectedMod = (IMod)((ListViewItem)e.Item.RowObject).Tag;
				}

				e.MenuStrip = this.cmsContextMenu;
			};
		}

		/// <summary>
		/// Setup the Image Getters
		/// </summary>
		public void SetupImageGetters()
		{
			tlcWebVersion.ImageGetter = delegate(object rowObject)
			{
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)lviItem.Tag;
					if (modMod != null)
					{
						if (!modMod.IsMatchingVersion())
							return new Bitmap(Properties.Resources.dialog_warning_4, 16, 16);
					}
				}

				return null;
			};

			tlcModName.ImageGetter = delegate(object rowObject)
			{
				ListViewItem lviItem = (ListViewItem)rowObject;
				if ((lviItem.Tag).GetType() == typeof(ModCategory))
				{
					return new Bitmap(Properties.Resources.activate_mod, 16, 16);
				}
				else
					return null;
			};

			tlcEndorsement.ImageGetter = delegate(object rowObject)
			{
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)lviItem.Tag;
					if (modMod != null)
					{
						if (modMod.IsEndorsed)
							return new Bitmap(Properties.Resources.endorsed_small, 16, 16);
						else
							return new Bitmap(Properties.Resources.unendorsed_small, 16, 16);
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
				foreach (ListViewItem Item in Roots)
				{
					if (Item.Tag.GetType() == typeof(ModCategory))
						RemoveObject(Item);
				}
			}

			// Setup categories
			foreach (IModCategory imcCategory in Categories)
			{
				ListViewItem Category = new ListViewItem();
				Category.Text = imcCategory.CategoryName + "(" + GetCategoryModCount(imcCategory, lviCategoryItems).ToString() + ")";
				ListViewItem.ListViewSubItem Sub = new ListViewItem.ListViewSubItem();
				Sub.Name = "InstallDate";
				Sub.Text = "";
				Category.SubItems.Add(Sub);
				Sub = new ListViewItem.ListViewSubItem();
				Sub.Name = "Endorsement";
				Sub.Text = "";
				Category.SubItems.Add(Sub);
				Sub = new ListViewItem.ListViewSubItem();
				Sub.Name = "HumanReadableVersion";
				Sub.Text = "";
				Category.SubItems.Add(Sub);
				Sub = new ListViewItem.ListViewSubItem();
				Sub.Name = "WebVersion";
				Sub.Text = "";
				Category.SubItems.Add(Sub);
				Sub = new ListViewItem.ListViewSubItem();
				Sub.Name = "Author";
				Sub.Text = "";
				Category.SubItems.Add(Sub);
				Category.Tag = imcCategory;
				this.AddObject(Category);
			}
		}

		/// <summary>
		/// Adds a new category to the TreeListView.
		/// </summary>
		public void AddData(IModCategory p_imcCategory)
		{
			ListViewItem Category = new ListViewItem();
			Category.Text = p_imcCategory.CategoryName + "(" + GetCategoryModCount(p_imcCategory, lviCategoryItems).ToString() + ")";
			ListViewItem.ListViewSubItem Sub = new ListViewItem.ListViewSubItem();
			Sub.Name = "InstallDate";
			Sub.Text = "";
			Category.SubItems.Add(Sub);
			Sub = new ListViewItem.ListViewSubItem();
			Sub.Name = "Endorsement";
			Sub.Text = "";
			Category.SubItems.Add(Sub);
			Sub = new ListViewItem.ListViewSubItem();
			Sub.Name = "HumanReadableVersion";
			Sub.Text = "";
			Category.SubItems.Add(Sub);
			Sub = new ListViewItem.ListViewSubItem();
			Sub.Name = "WebVersion";
			Sub.Text = "";
			Category.SubItems.Add(Sub);
			Sub = new ListViewItem.ListViewSubItem();
			Sub.Name = "Author";
			Sub.Text = "";
			Category.SubItems.Add(Sub);
			Category.Tag = p_imcCategory;
			this.AddObject(Category);
			(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Add(p_imcCategory.CategoryName, null, new EventHandler(cmsContextMenu_CategoryClicked));
		}

		/// <summary>
		/// Removes the category from the TreeListView.
		/// </summary>
		public bool RemoveData(ModCategory p_mctCategory)
		{
			if (this.Items.Count > 0)
			{
				foreach (ListViewItem Item in Roots)
				{
					if (((ModCategory)Item.Tag).Equals(p_mctCategory))
					{
						RemoveObject(Item);
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Update the category in the TreeListView.
		/// </summary>
		public void UpdateData(ModCategory p_mctCategory, string strOldValue)
		{
			if (this.Items.Count > 0)
			{
				foreach (ListViewItem Item in Roots)
				{
					if (((ModCategory)Item.Tag).Equals(p_mctCategory))
					{
						foreach (ToolStripDropDownItem DDItem in (cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems)
							if (DDItem.Text == strOldValue)
							{
								(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Remove(DDItem);
								(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Add(p_mctCategory.CategoryName, null, new EventHandler(cmsContextMenu_CategoryClicked));
								break;
							}
						RefreshObject(Item);
						break;
					}
				}
			}
		}

		/// <summary>
		/// Gets the mod count for the current category.
		/// </summary>
		public Int32 GetCategoryModCount(IModCategory p_imcCategory, IEnumerable<ListViewItem> p_lviItems)
		{
			var CategoryMods = from Mod in p_lviItems
							   where ((IMod)Mod.Tag != null) && ((((IMod)Mod.Tag).CustomCategoryId >= 0 ? ((IMod)Mod.Tag).CustomCategoryId : ((IMod)Mod.Tag).CategoryId) == p_imcCategory.Id)
							   select Mod;

			return CategoryMods.Count();
		}

		#endregion

		#region EventHandler

		private void cmsContextMenu_CategoryRemove(object sender, EventArgs e)
		{
			if (m_imcSelectedCategory != null)
			{
				CategoryManager.RemoveCategory(m_imcSelectedCategory);
				if (this.RemoveData(new ModCategory(m_imcSelectedCategory)))
				{
					foreach (ToolStripDropDownItem Item in (cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems)
						if (Item.Text == m_imcSelectedCategory.CategoryName)
						{
							(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Remove(Item);
							break;
						}
				}

				if (this.CategoryRemoved != null)
					this.CategoryRemoved(m_imcSelectedCategory, new EventArgs());
			}
		}

		private void cmsContextMenu_CategoryNew(object sender, EventArgs e)
		{
			ToolStripItem item = sender as ToolStripItem;
			this.AddData(CategoryManager.AddCategory());
			this.RebuildAll(true);
		}

		private void cmsContextMenu_CategoryClicked(object sender, EventArgs e)
		{
			ToolStripItem item = sender as ToolStripItem;
			if (m_modSelectedMod != null)
			{
				if (this.CategorySwitch != null)
					this.CategorySwitch((IModCategory)Categories.Find(Item => Item.CategoryName == item.Text), new EventArgs());
			}
		}

		#endregion
	}
}
