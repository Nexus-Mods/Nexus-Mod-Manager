using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	public partial class CategoryManager : ICategoryManager
	{
		private static readonly Version CURRENT_VERSION = new Version("0.1.0.0");
		private static readonly String CATEGORY_FILE = "Categories.xml";

		#region Static Properties

		/// <summary>
		/// Gets the current support version of the category manager.
		/// </summary>
		/// <value>The current support version of the category manager.</value>
		public static Version CurrentVersion
		{
			get
			{
				return CURRENT_VERSION;
			}
		}

		#endregion

		#region Singleton

		private static ICategoryManager m_icmCurrent = null;

		/// <summary>
		/// Initializes the category manager.
		/// </summary>
		/// <param name="p_strModInstallDirectory">The path of the directory where all of the mods are installed.</param>
		/// <param name="p_strCategoryPath">The path from which to load the categories information.</param>
		/// <returns>The initialized category manager.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the category manager has already
		/// been initialized.</exception>
		public static ICategoryManager Initialize(string p_strModInstallDirectory, string p_strCategoryPath)
		{
			if (m_icmCurrent != null)
				throw new InvalidOperationException("The Category Manager has already been initialized.");
			m_icmCurrent = new CategoryManager(p_strModInstallDirectory, p_strCategoryPath);
			return m_icmCurrent;
		}

		#endregion

		/// <summary>
		/// Reads the category manager version from the given category file.
		/// </summary>
		/// <param name="p_strCategoryPath">The category file whose version is to be read.</param>
		/// <returns>The version of the specified category file, or a version of
		/// <c>0.0.0.0</c> if the file format is not recognized.</returns>
		public static Version ReadVersion(string p_strCategoryPath)
		{
			if (!File.Exists(p_strCategoryPath))
				return new Version("0.0.0.0");

			XDocument docCategory = XDocument.Load(p_strCategoryPath);

			XElement xelCategory = docCategory.Element("categoryManager");
			if (xelCategory == null)
				return new Version("0.0.0.0");

			XAttribute xatVersion = xelCategory.Attribute("fileVersion");
			if (xatVersion == null)
				return new Version("0.0.0.0");

			return new Version(xatVersion.Value);
		}

		/// <summary>
		/// Determines if the category file at the given path is valid.
		/// </summary>
		/// <param name="p_strCategoryPath">The path of the category file to validate.</param>
		/// <returns><c>true</c> if the given manager is valid;
		/// <c>false</c> otherwise.</returns>
		protected static bool IsValid(string p_strCategoryPath)
		{
			if (!File.Exists(p_strCategoryPath))
				return false;
			try
			{
				XDocument docCategory = XDocument.Load(p_strCategoryPath);
			}
			catch (Exception e)
			{
				Trace.TraceError("Invalid Category File ({0}):", p_strCategoryPath);
				Trace.Indent();
				TraceUtil.TraceException(e);
				Trace.Unindent();
				return false;
			}
			return true;
		}

		private ThreadSafeObservableList<IModCategory> m_tslCategories = new ThreadSafeObservableList<IModCategory>();

		#region Properties

		/// <summary>
		/// Gets the path of the category folder.
		/// </summary>
		/// <value>The path of the category folder.</value>
		protected string CategoryPath { get; private set; }

		/// <summary>
		/// Gets the path of the category file.
		/// </summary>
		/// <value>The path of the category file.</value>
		protected string CategoryFilePath { get; private set; }

		/// <summary>
		/// Gets the path of the directory where all of the mods are installed.
		/// </summary>
		/// <value>The path of the directory where all of the mods are installed.</value>
		protected string ModInstallDirectory { get; private set; }


		/// <summary>
		/// Gets the list of active mods.
		/// </summary>
		/// <value>The list of active mods.</value>
		public bool IsValidPath
		{
			get
			{
				return IsValid(CategoryFilePath);
			}
		}

		/// <summary>
		/// Gets the list of active mods.
		/// </summary>
		/// <value>The list of active mods.</value>
		public ThreadSafeObservableList<IModCategory> Categories
		{
			get
			{
				return m_tslCategories;
			}
		}

		public Int32 GetNextId
		{
			get
			{
				int Max = Categories.Max(item => item.Id);
				return Enumerable.Range(1, Max + 1).Except(Categories.Select(item => item.Id)).Min();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_strModInstallDirectory">The path of the directory where all of the mods are installed.</param>
		/// <param name="p_strCategoryPath">The path from which to load the categories.</param>
		public CategoryManager(string p_strModInstallDirectory, string p_strCategoryPath)
		{
			ModInstallDirectory = p_strModInstallDirectory.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			CategoryPath = Path.Combine(ModInstallDirectory, p_strCategoryPath);
			CategoryFilePath = Path.Combine(CategoryPath, CATEGORY_FILE);
		}

		#endregion

		#region Serialization/Deserialization

		/// <summary>
		/// Gets the category info that is currently in the category file, indexed by category key.
		/// </summary>
		/// <returns>The category info that is currently in the category file, indexed by category key.</returns>
		private IDictionary<string, IModCategory> GetCategoryInfo()
		{
			Dictionary<string, IModCategory> dicCategoryInfo = new Dictionary<string, IModCategory>();
			XDocument docCategories = XDocument.Load(CategoryFilePath);

			string strVersion = docCategories.Element("categoryManager").Attribute("fileVersion").Value;
			if (!CURRENT_VERSION.ToString().Equals(strVersion))
				throw new Exception(String.Format("Invalid Category Manager version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

			XElement xelCategoryList = docCategories.Descendants("categoryList").FirstOrDefault();
			if (xelCategoryList != null)
			{
				foreach (XElement xelCategory in xelCategoryList.Elements("category"))
				{
					string strCategoryPath = xelCategory.Attribute("path").Value;
					strCategoryPath = Path.Combine(ModInstallDirectory, strCategoryPath);
					string strCategoryName = xelCategory.Element("name").Value;
					IModCategory mctCategory = null;
					mctCategory.CategoryName = strCategoryName;
					mctCategory.CategoryPath = strCategoryPath;
					mctCategory.Id = String.IsNullOrEmpty(xelCategory.Attribute("ID").Value) ? 0 : Convert.ToInt32(xelCategory.Attribute("ID").Value);
					dicCategoryInfo[xelCategory.Attribute("ID").Value] = mctCategory;
				}
			}
			return dicCategoryInfo;
		}

		/// <summary>
		/// Loads the categories.
		/// </summary>
		public void LoadCategories(string p_strDefaultCategories)
		{
			if (!File.Exists(CategoryFilePath))
			{
				m_tslCategories.Add(new ModCategory());
				if (!String.IsNullOrEmpty(p_strDefaultCategories))
					LoadCategories(XDocument.Parse(p_strDefaultCategories));
				SaveCategories();
				return;
			}
			else
				LoadCategories(XDocument.Load(CategoryFilePath));
		}

		/// <summary>
		/// Resets to the repository default categories.
		/// </summary>
		public void ResetCategories(string p_strDefaultCategories)
		{
			m_tslCategories.Clear();
			m_tslCategories.Add(new ModCategory());
			if (!String.IsNullOrEmpty(p_strDefaultCategories))
				LoadCategories(XDocument.Parse(p_strDefaultCategories));
			SaveCategories();
			return;
		}

		/// <summary>
		/// Loads the data from the category file.
		/// </summary>
		private void LoadCategories(XDocument p_docCategories)
		{
			string strVersion = p_docCategories.Element("categoryManager").Attribute("fileVersion").Value;
			if (!CURRENT_VERSION.ToString().Equals(strVersion))
				throw new Exception(String.Format("Invalid Category Manager version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

			XElement xelCategoryList = p_docCategories.Descendants("categoryList").FirstOrDefault();
			if (xelCategoryList != null)
			{
				foreach (XElement xelCategory in xelCategoryList.Elements("category"))
				{
					string strCategoryPath = xelCategory.Attribute("path").Value;
					string strCategoryName = xelCategory.Element("name").Value;
					strCategoryPath = Path.Combine(ModInstallDirectory, strCategoryPath);
					m_tslCategories.Add(new ModCategory(Convert.ToInt32(xelCategory.Attribute("ID").Value), strCategoryName, strCategoryPath));
				}
			}
		}

		/// <summary>
		/// Save the data to the category file.
		/// </summary>
		protected void SaveCategories()
		{
			XDocument docCategories = new XDocument();
			XElement xelRoot = new XElement("categoryManager", new XAttribute("fileVersion", CURRENT_VERSION));
			docCategories.Add(xelRoot);

			XElement xelCategoryList = new XElement("categoryList");
			xelRoot.Add(xelCategoryList);
			xelCategoryList.Add(from mct in m_tslCategories
						   select new XElement("category",
									new XAttribute("path", mct.CategoryPath),
									new XAttribute("ID", mct.Id),
									new XElement("name",
										new XText(mct.CategoryName))));

			if (!Directory.Exists(Path.GetDirectoryName(CategoryPath)))
				Directory.CreateDirectory(Path.GetDirectoryName(CategoryPath));
			docCategories.Save(CategoryFilePath);
		}

		#endregion

		#region Category Management

		/// <summary>
		/// Adds a new category to the list with default values.
		/// </summary>
		public IModCategory AddCategory()
		{
			Int32 Max = GetNextId;
			return AddCategory(new ModCategory(Max, "New" + Max.ToString(), "New" + Max.ToString()));
		}

		/// <summary>
		/// Adds a category to the list.
		/// </summary>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> being added.</param>
		public IModCategory AddCategory(IModCategory p_mctCategory)
		{
			m_tslCategories.Add(p_mctCategory);
			SaveCategories();
			return p_mctCategory;
		}

		/// <summary>
		/// Replaces a category in the list.
		/// </summary>
		/// <param name="p_mctOldCategory">The category to be replaced with the new.</param>
		/// <param name="p_mctNewCategory">The p_mctNewCategory with which to replace the old one.</param>
		public void ReplaceCategory(IModCategory p_mctOldCategory, IModCategory p_mctNewCategory)
		{
			m_tslCategories.Remove(p_mctOldCategory);
			m_tslCategories.Add(p_mctNewCategory);
			SaveCategories();
		}

		/// <summary>
		/// Removes a category from the list.
		/// </summary>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> to be removed.</param>
		public void RemoveCategory(IModCategory p_mctCategory)
		{
			m_tslCategories.Remove(p_mctCategory);
			SaveCategories();
		}

		#endregion

		#region Backup Management

		/// <summary>
		/// This backs up the category file.
		/// </summary>
		public void Backup()
		{
			if (File.Exists(CategoryFilePath))
			{
				string strBackupCategoryPath = CategoryFilePath + ".bak";
				FileInfo fifCategory = new FileInfo(CategoryFilePath);
				FileInfo fifCategoryBak = File.Exists(strBackupCategoryPath) ? new FileInfo(strBackupCategoryPath) : null;

				if ((fifCategoryBak == null) || (fifCategoryBak.LastWriteTimeUtc != fifCategory.LastWriteTimeUtc))
				{
					for (Int32 i = 4; i > 0; i--)
					{
						if (File.Exists(strBackupCategoryPath + i))
							File.Copy(strBackupCategoryPath + i, strBackupCategoryPath + (i + 1), true);
					}
					if (File.Exists(strBackupCategoryPath))
						File.Copy(strBackupCategoryPath, strBackupCategoryPath + "1", true);
					File.Copy(CategoryFilePath, strBackupCategoryPath, true);
				}
			}
		}

		/// <summary>
		/// This restores the first valid backup of the category file.
		/// </summary>
		public static bool Restore(string p_strCategoryPath)
		{
			string strSuffix = "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bad";
			if (File.Exists(p_strCategoryPath))
				FileUtil.Move(p_strCategoryPath, p_strCategoryPath + strSuffix, true);
			string strBackupCategoryPath = p_strCategoryPath + ".bak";
			if (IsValid(strBackupCategoryPath))
			{
				File.Copy(strBackupCategoryPath, p_strCategoryPath, true);
				return true;
			}
			if (File.Exists(strBackupCategoryPath))
				FileUtil.Move(strBackupCategoryPath, strBackupCategoryPath + strSuffix, true);
			for (Int32 i = 1; i < 6; i++)
			{
				if (IsValid(strBackupCategoryPath + i))
				{
					FileUtil.Move(strBackupCategoryPath + i, p_strCategoryPath, true);
					return true;
				}
				if (File.Exists(strBackupCategoryPath + i))
					FileUtil.Move(strBackupCategoryPath + i, strBackupCategoryPath + i + strSuffix, true);
			}
			return false;
		}

		#endregion

		/// <summary>
		/// This disposes of the category manager, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
			m_icmCurrent = null;
		}
	}
}
