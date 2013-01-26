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
		bool m_booShowEmpty = false;

		#region Custom Events

		public event EventHandler CategorySwitch;
		public event EventHandler CategoryRemoved;

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
		/// Gets the currently selected item.
		/// </summary>
		/// <value>The currently selected item.</value>
		public ListViewItem GetSelectedItem
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
		public List<ListViewItem> GetSelectedItems
		{
			get
			{
				List<ListViewItem> lstListViewItem = new List<ListViewItem>();
				foreach (ListViewItem item in this.SelectedObjects)
					lstListViewItem.Add(item);
				return lstListViewItem;
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
				return m_modSelectedMod;
			}
		}

		/// <summary>
		/// Gets the ListViewItems from the reference table.
		/// </summary>
		/// <value>The ListViewItems from the reference table.</value>
		protected IEnumerable<ListViewItem> lviCategoryItems
		{
			get
			{
				return m_lvwList.Items.Cast<ListViewItem>();
			}
		}

		/// <summary>
		/// Gets the category manager to use to manage categories.
		/// </summary>
		/// <value>The category manager to use to manage categories.</value>
		protected CategoryManager CategoryManager { get; private set; }

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

			this.CellEditActivation = CellEditActivateMode.None;
			this.MultiSelect = true;

			m_lvwList = p_lvwList;
			CategoryManager = p_cmgCategoryManager;

			// Setup menuStrip commands
			SetupContextMenu();

			// Setup category validator
			SetupCategoryValidator();

			// Setup category sorter
			SetupCategorySorter();

			this.CheckBoxes = false;
			this.UseSubItemCheckBoxes = true;
			this.BooleanCheckStateGetter = delegate(object x)
			{
				ListViewItem lviItem = (ListViewItem)x;
				return lviItem.Checked;
			};

			// Setup AspectGetter (IconListView cell parser)
			SetupColumnParser();

			// Setup the Drag&Drop functionality
			SetupDragAndDrop();

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
			cmsContextMenu.Items.Clear();
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
				}
				else
					Val = ((IMod)lviItem.Tag).ModName;

				return Val;
			};

			tlcModName.AspectToStringConverter = delegate(object x)
			{
				return x.ToString();
			};

			tlcInstallDate.AspectGetter = delegate(object rowObject)
			{
				string Val = String.Empty;
				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					if (!String.IsNullOrEmpty(lviItem.SubItems[tlcInstallDate.Name].Text))
						Val = lviItem.SubItems[tlcInstallDate.Name].Text;		
					if (CheckDate(Val))
						return Convert.ToDateTime(Val);
				}

				return null;
			};

			tlcEndorsement.AspectGetter = delegate(object rowObject)
			{
				string Value = String.Empty;

				ListViewItem lviItem = (ListViewItem)rowObject;

				if (lviItem.Tag.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)lviItem.Tag;
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
		/// Setup the Drag&Drop
		/// </summary>
		public void SetupDragAndDrop()
		{
			this.IsSimpleDragSource = true;
			this.IsSimpleDropSink = true;

			this.CanDrop += delegate(object sender, BrightIdeasSoftware.OlvDropEventArgs e)
			{
				if ((e.DropTargetItem == null) || (this.GetSelectedItem.Tag.GetType() == typeof(ModCategory)))
					return;

				ListViewItem lviItem = (ListViewItem)e.DropTargetItem.RowObject;

				if (lviItem != null)
				{
					e.Effect = DragDropEffects.Move;
				}
			};

			this.Dropped += delegate(object sender, BrightIdeasSoftware.OlvDropEventArgs e)
			{
				if (e.DropTargetItem == null)
					return;

				ListViewItem lviItem = (ListViewItem)e.DropTargetItem.RowObject;

				if ((lviItem != null) && (lviItem.Tag != null))
				{
					IModCategory imcCategory = null;

					if (lviItem.Tag.GetType() == typeof(ModCategory))
					{
						imcCategory = (IModCategory)lviItem.Tag;
					}
					else
					{
						try
						{
							IMod modMod = (IMod)lviItem.Tag;
							imcCategory = CategoryManager.FindCategory(modMod.CustomCategoryId >= 0 ? modMod.CustomCategoryId : modMod.CategoryId);
						}
						catch
						{
						}
					}

					if ((imcCategory != null) && (this.CategorySwitch != null))
					{
						m_modSelectedMod = (IMod)this.GetSelectedItem.Tag;
						this.CategorySwitch(imcCategory, new EventArgs());
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
				if (e.Item != null)
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
				}
				else
					e.MenuStrip = null;
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
					return new Bitmap(CreateBitmapImage(GetCategoryModCount((IModCategory)lviItem.Tag, lviCategoryItems).ToString()), 13 , 15);
				}
				else
				{
					return new Bitmap(lviItem.Checked ? Properties.Resources.dialog_ok_4_16 : Properties.Resources.dialog_cancel_4_16, 12, 12);
				}
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
				Int32 ModCount = GetCategoryModCount(imcCategory, lviCategoryItems);

				if (m_booShowEmpty || (ModCount > 0))
				{
					Category.Text = imcCategory.CategoryName;
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
		}

		/// <summary>
		/// Adds a new category to the TreeListView.
		/// </summary>
		/// <param name="p_imcCategory">The category to add to the roots list.</param>
		/// <param name="booIsNew">Whether the category is new or was just hidden.</param>
		public void AddData(IModCategory p_imcCategory, bool booIsNew)
		{
			ListViewItem Category = new ListViewItem();
			Category.Text = p_imcCategory.CategoryName;
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

			if (booIsNew)
				(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Add(p_imcCategory.CategoryName, null, new EventHandler(cmsContextMenu_CategoryClicked));

			this.EnsureVisible(this.Items.Count - 1);
		}

		/// <summary>
		/// Removes the category from the TreeListView.
		/// </summary>
		/// <param name="p_mctCategory">The category to remove.</param>
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
		/// Refresh the category in the TreeListView.
		/// </summary>
		/// <param name="p_mctCategory">The category to refresh.</param>
		public bool RefreshData(ModCategory p_mctCategory)
		{
			if (this.Items.Count > 0)
			{
				foreach (ListViewItem Item in Roots)
				{
					if (((ModCategory)Item.Tag).Equals(p_mctCategory))
					{
						RefreshObject(Item);
						return false;
					}
				}
			}

			return true;
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
		/// <param name="p_imcCategory">The category to count.</param>
		public Int32 GetCategoryModCount(IModCategory p_imcCategory)
		{
			var CategoryMods = from Mod in lviCategoryItems
							   where ((IMod)Mod.Tag != null) && ((((IMod)Mod.Tag).CustomCategoryId >= 0 ? ((IMod)Mod.Tag).CustomCategoryId : ((IMod)Mod.Tag).CategoryId) == p_imcCategory.Id)
							   select Mod;

			return CategoryMods.Count();
		}

		/// <summary>
		/// Gets the mod count for the current category.
		/// </summary>
		/// <param name="p_imcCategory">The category to count.</param>
		/// <param name="p_lviItems">The ListViewItem containing the categories.</param>
		public Int32 GetCategoryModCount(IModCategory p_imcCategory, IEnumerable<ListViewItem> p_lviItems)
		{
			var CategoryMods = from Mod in p_lviItems
							   where ((IMod)Mod.Tag != null) && ((((IMod)Mod.Tag).CustomCategoryId >= 0 ? ((IMod)Mod.Tag).CustomCategoryId : ((IMod)Mod.Tag).CategoryId) == p_imcCategory.Id)
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
				if (this.RemoveData(new ModCategory(p_imcCategory)))
				{
					foreach (ToolStripDropDownItem Item in (cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems)
						if (Item.Text == p_imcCategory.CategoryName)
						{
							(cmsContextMenu.Items[0] as ToolStripMenuItem).DropDownItems.Remove(Item);
							break;
						}
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
			this.RebuildAll(true);
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
		private Bitmap CreateBitmapImage(string sImageText)
		{
			Bitmap objBmpImage = new Bitmap(Properties.Resources.category_folder);
			int intWidth = 0;
			int intHeight = 0;
			Font objFont = new Font("Arial", 14, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
			Graphics objGraphics = Graphics.FromImage(objBmpImage);
			intWidth = (int)objGraphics.MeasureString(sImageText, objFont).Width;
			intHeight = (int)objGraphics.MeasureString(sImageText, objFont).Height;
			objBmpImage = new Bitmap(objBmpImage, new Size(intWidth + 2, intHeight + 4));
			objGraphics = Graphics.FromImage(objBmpImage);
			objGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			objGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			objGraphics.DrawString(sImageText, objFont, new SolidBrush(Color.FromArgb(25, 25, 25)), 1, 5);
			objGraphics.Flush();
			objBmpImage.MakeTransparent(Color.Magenta);
			return (objBmpImage);
		}
	}
}
