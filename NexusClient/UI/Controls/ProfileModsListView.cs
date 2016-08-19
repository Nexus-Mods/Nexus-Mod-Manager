using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using BrightIdeasSoftware;

namespace Nexus.Client.UI.Controls
{
	public partial class ProfileModsListView : BrightIdeasSoftware.TreeListView
	{

		List<IVirtualModInfo> m_tslProfileMods = null;

		#region Properties

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
		/// Gets the SaveGame manager to use to manage categories.
		/// </summary>
		/// <value>The SaveGame manager to use to manage categories.</value>
		protected ModManager ModManager { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ProfileModsListView()
			: base()
		{
			InitializeComponent();
			tlcProfileModName.Name = "Mod";
			tlcProfileModStatus.Name = "Status";
			tlcProfileModDownloadID.Name = "DownloadID";
		}

		#endregion

		#region TreeListView Setup

		/// <summary>
		/// Setup the SaveGameView
		/// </summary>
		/// <param name="p_lvwList">The source list view.</param>
		/// <param name="p_cmgSaveGameManager">The mod SaveGame Manager.</param>
		public void Setup(ModManager p_mmModManager)
		{
			this.Tag = false;

			this.CellEditActivation = CellEditActivateMode.None;
			this.MultiSelect = true;
			this.AllowDrop = true;
			this.UseFiltering = true;
			this.UseHyperlinks = true;

			ModManager = p_mmModManager;

			// Setup menuStrip commands
			SetupContextMenu();
			
			// Setup SaveGame sorter
			SetupSaveGameSorter();

			this.CheckBoxes = false;
			this.UseSubItemCheckBoxes = false;

			IVirtualModInfo modInfo = null;

			this.FormatRow += delegate (object sender, FormatRowEventArgs e)
			{
				modInfo = (IVirtualModInfo)e.Model;

				string fileName = Path.GetFileName(modInfo.ModFileName);
				string downloadId = modInfo.DownloadId;
				string fileVersion = modInfo.FileVersion;

				var mod = ModManager.ManagedMods.Where(x => String.Equals(Path.GetFileName(x.Filename), fileName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
				if (mod == null)
					mod = ModManager.ManagedMods.Where(x => String.Equals(x.DownloadId, downloadId, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

				if (mod != null)
				{
					e.ListView.Font = new Font("Arial", 8F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));
					e.Item.Font = new Font("Arial", 8F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));

					if (!ModManager.IsMatchingVersion(fileVersion, mod.HumanReadableVersion))
					{
						e.ListView.ForeColor = Color.DarkSalmon;
						e.Item.ForeColor = Color.DarkSalmon;
					}
					else
					{
						e.ListView.ForeColor = Color.Gray;
						e.Item.ForeColor = Color.Gray;
					}
				}
				else
				{
					e.ListView.ForeColor = Color.Red;
					e.Item.ForeColor = Color.Red;
				}
			};
			
			// Setup AspectGetter (IconListView cell parser)
			SetupColumnParser();

			// Setup the Drag&Drop functionality
			SetupDragAndDrop();

			// Setup hyperlink manager
			//SetupHyperlinkManager();

			// Setup ImageGetters
			SetupImageGetters();

			// Set control initialized
			this.Tag = true;
			//this.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
			ResetColumns();
		}
		
		/// <summary>
		/// Setup the SaveGameView context menu
		/// </summary>
		public void SetupContextMenu()
		{
		}

		/// <summary>
		/// Setup the Image Getters
		/// </summary>
		public void SetupImageGetters()
		{

		}

		/// <summary>
		/// Setup the SaveGameView mod sorter
		/// </summary>
		public void SetupSaveGameSorter()
		{
			this.ChildrenGetter = delegate (object x)
			{
				return null;
			};
		}

		/// <summary>
		/// Setup the IconListView cell parser
		/// </summary>
		public void SetupColumnParser()
		{
			tlcProfileModName.AspectGetter = delegate (object rowObject)
			{
				string Val = String.Empty;

				Val = ((IVirtualModInfo)rowObject).ModName;

				return Val;
			};

			tlcProfileModName.AspectToStringConverter = delegate (object x)
			{
				return x.ToString();
			};

			tlcProfileModDownloadID.AspectGetter = delegate (object rowObject)
			{
				string Val = String.Empty;

				Val = ((IVirtualModInfo)rowObject).DownloadId;

				return Val;
			};

			tlcProfileModDownloadID.AspectToStringConverter = delegate (object x)
			{
				return x.ToString();
			};

			tlcProfileModStatus.AspectGetter = delegate (object rowObject)
			{
				string Val = String.Empty;

				string fileName = Path.GetFileName(((IVirtualModInfo)rowObject).ModFileName);
				string downloadId = ((IVirtualModInfo)rowObject).DownloadId;
				string fileVersion = ((IVirtualModInfo)rowObject).FileVersion;

				var mod = ModManager.ManagedMods.Where(x => String.Equals(Path.GetFileName(x.Filename), fileName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
				if(mod == null)
					mod = ModManager.ManagedMods.Where(x => String.Equals(x.DownloadId, downloadId, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

				if (mod != null)
				{
					if (!ModManager.IsMatchingVersion(fileVersion, mod.HumanReadableVersion))
						Val = "Mismatched";
					else
						Val = "";
				}
				else
					Val = "Missing";
								
				return Val;
			};

			tlcProfileModStatus.AspectToStringConverter = delegate (object x)
			{
				return x.ToString();
			};

		}

		/// <summary>
		/// Setup the Drag&Drop
		/// </summary>
		public void SetupDragAndDrop()
		{
			this.IsSimpleDragSource = false;
			this.IsSimpleDropSink = false;

			this.CanDrop += delegate (object sender, BrightIdeasSoftware.OlvDropEventArgs e)
			{
				e.Effect = DragDropEffects.None;
			};

			this.Dropped += delegate (object sender, BrightIdeasSoftware.OlvDropEventArgs e)
			{
			};
		}

		#endregion

		#region MpdsProfile Management

		/// <summary>
		/// Loads the TreeListView categories.
		/// </summary>
		public void LoadData(List<IVirtualModInfo> p_tslModsProfile)
		{
			m_tslProfileMods = p_tslModsProfile;
			this.SetObjects(m_tslProfileMods);
		}

		#endregion

		#region EventHandler

		#endregion

		/// <summary>
		//Resets the Columns to the original width
		/// </summary>
		public void ResetColumns()
		{
			tlcProfileModName.Width = 200;
			tlcProfileModStatus.Width = 150;
			tlcProfileModDownloadID.Width = 150;
			SizeColumnsToFit();
		}

		/// <summary>
		/// This resizes the columns to fill the list view.
		/// </summary>
		public void SizeColumnsToFit()
		{
			Int32 intFixedWidth = 30;
			for (Int32 i = 0; i < this.Columns.Count; i++)
				if (this.Columns[i] != tlcProfileModName)
					intFixedWidth += this.Columns[i].Width;
			tlcProfileModName.Width = this.ClientSize.Width - intFixedWidth;
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
	}
}
