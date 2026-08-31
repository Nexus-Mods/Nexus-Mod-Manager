using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	public partial class CategoryManager : ICategoryManager
	{
		private static readonly Version CURRENT_VERSION = new Version("0.1.0.0");
		private const String CATEGORY_FILE = "Categories.xml";
		private const String CUSTOM_ATTRIBUTE = "isCustom";
		internal const Int32 FIRST_CUSTOM_CATEGORY_ID = 1000000;

		#region Events

		/// <summary>
		/// Raised after the category definitions or their persisted metadata change.
		/// </summary>
		public event EventHandler CategoriesChanged = delegate { };

		#endregion

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
				XDocument.Load(p_strCategoryPath);
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

		private readonly ThreadSafeObservableList<IModCategory> m_tslCategories = new ThreadSafeObservableList<IModCategory>();
		private readonly HashSet<Int32> m_hstCustomCategoryIds = new HashSet<Int32>();

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
		/// Gets whether the category file exists and can be parsed.
		/// </summary>
		/// <value><c>true</c> when the category file is valid; otherwise <c>false</c>.</value>
		public bool IsValidPath
		{
			get
			{
				return IsValid(CategoryFilePath);
			}
		}

		/// <summary>
		/// Gets the category collection.
		/// </summary>
		/// <value>The category collection.</value>
		public ThreadSafeObservableList<IModCategory> Categories
		{
			get
			{
				return m_tslCategories;
			}
		}

		/// <summary>
		/// Gets the next ID reserved for a user-created category.
		/// </summary>
		/// <value>An unused category ID from the custom-category range.</value>
		public Int32 GetNextId
		{
			get
			{
				return GetNextCustomCategoryId(null);
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
			ModInstallDirectory = p_strModInstallDirectory.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			CategoryPath = Path.Combine(ModInstallDirectory, p_strCategoryPath);
			CategoryFilePath = Path.Combine(CategoryPath, CATEGORY_FILE);
		}

		#endregion

		#region Serialization/Deserialization

		/// <summary>
		/// Loads the categories and upgrades legacy category-origin metadata when defaults are available.
		/// </summary>
		/// <param name="p_strDefaultCategories">The string containing the default repository categories.</param>
		public void LoadCategories(string p_strDefaultCategories)
		{
			XDocument docDefaultCategories = ParseCategoryDocument(p_strDefaultCategories);
			Dictionary<Int32, String> repositoryCategories = GetRepositoryCategoryNames(docDefaultCategories);
			bool shouldSave;

			using (m_tslCategories.BeginUpdate())
			{
				m_tslCategories.Clear();
				m_hstCustomCategoryIds.Clear();

				if (!File.Exists(CategoryFilePath))
				{
					if (docDefaultCategories != null)
						LoadCategories(docDefaultCategories, null, true);
					shouldSave = true;
				}
				else
				{
					shouldSave = LoadCategories(XDocument.Load(CategoryFilePath), repositoryCategories, false);
				}

				EnsureUnassignedCategory();
				if (shouldSave)
					SaveCategories();
			}

			OnCategoriesChanged();
		}

		/// <summary>
		/// Loads categories from an XML document into the current collection.
		/// </summary>
		/// <param name="p_docCategories">The XML document containing the categories.</param>
		/// <param name="p_dctRepositoryCategories">Repository category names keyed by ID, used to identify legacy custom categories.</param>
		/// <param name="p_booRepositoryDocument">Whether every category in the document is repository-owned.</param>
		/// <returns><c>true</c> when legacy metadata was inferred and should be persisted; otherwise <c>false</c>.</returns>
		private bool LoadCategories(XDocument p_docCategories, IDictionary<Int32, String> p_dctRepositoryCategories, bool p_booRepositoryDocument)
		{
			XElement xelRoot = p_docCategories.Element("categoryManager");
			if (xelRoot == null)
				throw new Exception("Invalid Category Manager file: missing categoryManager root element.");

			XAttribute xatVersion = xelRoot.Attribute("fileVersion");
			string strVersion = xatVersion == null ? String.Empty : xatVersion.Value;
			if (!CURRENT_VERSION.ToString().Equals(strVersion))
				throw new Exception(String.Format("Invalid Category Manager version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

			bool inferredLegacyMetadata = false;
			XElement xelCategoryList = p_docCategories.Descendants("categoryList").FirstOrDefault();
			if (xelCategoryList == null)
				return inferredLegacyMetadata;

			foreach (XElement xelCategory in xelCategoryList.Elements("category"))
			{
				XAttribute xatId = xelCategory.Attribute("ID");
				XElement xelName = xelCategory.Element("name");
				if (xatId == null || xelName == null)
					continue;

				Int32 categoryId;
				if (!Int32.TryParse(xatId.Value, out categoryId))
					continue;

				if (FindCategoryInternal(categoryId) != null)
					continue;

				string strCategoryName = xelName.Value;
				XAttribute xatPath = xelCategory.Attribute("path");
				string strCategoryPath = xatPath == null || String.IsNullOrWhiteSpace(xatPath.Value) ? strCategoryName : xatPath.Value;
				if (!Path.IsPathRooted(strCategoryPath))
					strCategoryPath = Path.Combine(ModInstallDirectory, strCategoryPath);

				bool isCustom = false;
				XAttribute xatIsCustom = xelCategory.Attribute(CUSTOM_ATTRIBUTE);
				if (xatIsCustom != null)
				{
					Boolean.TryParse(xatIsCustom.Value, out isCustom);
				}
				else
				{
					inferredLegacyMetadata = true;
					if (!p_booRepositoryDocument && categoryId != 0)
					{
						String repositoryName;
						isCustom = p_dctRepositoryCategories == null || p_dctRepositoryCategories.Count == 0 ||
							!p_dctRepositoryCategories.TryGetValue(categoryId, out repositoryName) ||
							!String.Equals(repositoryName.Trim(), strCategoryName.Trim(), StringComparison.OrdinalIgnoreCase);
					}
				}

				m_tslCategories.Add(new ModCategory(categoryId, strCategoryName, strCategoryPath));
				if (isCustom)
					m_hstCustomCategoryIds.Add(categoryId);
			}

			return inferredLegacyMetadata;
		}

		/// <summary>
		/// Resets all categories to the categories loaded from the specified online document.
		/// </summary>
		/// <param name="p_uriDefaultCategories">The online path where the category file is stored.</param>
		public void ResetCategories(Uri p_uriDefaultCategories)
		{
			ResetCategories(p_uriDefaultCategories == null ? null : XDocument.Load(p_uriDefaultCategories.AbsoluteUri));
		}

		/// <summary>
		/// Resets all categories to the specified repository defaults.
		/// </summary>
		/// <param name="p_strDefaultCategories">The string containing the new category list.</param>
		public void ResetCategories(string p_strDefaultCategories)
		{
			ResetCategories(ParseCategoryDocument(p_strDefaultCategories));
		}

		/// <summary>
		/// Replaces repository categories while preserving user-created categories.
		/// </summary>
		/// <param name="p_strDefaultCategories">The string containing the repository categories.</param>
		/// <param name="p_actRemapCategoryAssignments">The callback used to apply old-to-new category ID mappings.</param>
		public void ResetRepositoryCategories(string p_strDefaultCategories, Action<IDictionary<Int32, Int32>> p_actRemapCategoryAssignments)
		{
			XDocument docDefaultCategories = ParseCategoryDocument(p_strDefaultCategories);
			List<IModCategory> repositoryCategories = GetRepositoryCategories(docDefaultCategories);
			PrepareRepositoryCategories(repositoryCategories, p_actRemapCategoryAssignments);
			List<IModCategory> customCategories = Categories.Where(IsCustomCategory).ToList();

			using (m_tslCategories.BeginUpdate())
			{
				m_tslCategories.Clear();
				m_hstCustomCategoryIds.Clear();
				if (docDefaultCategories != null)
					LoadCategories(docDefaultCategories, null, true);
				EnsureUnassignedCategory();

				foreach (IModCategory customCategory in customCategories)
				{
					if (FindCategoryInternal(customCategory.Id) != null)
						continue;

					m_tslCategories.Add(customCategory);
					m_hstCustomCategoryIds.Add(customCategory.Id);
				}
			}

			SaveCategories();
			OnCategoriesChanged();
		}

		/// <summary>
		/// Restores bundled repository category IDs that are missing from the persisted category file
		/// or are occupied by a legacy custom category, without replacing repository definitions that
		/// are already present. Exact-name migrated custom definitions are folded back automatically.
		/// </summary>
		/// <param name="p_strDefaultCategories">The bundled repository category document.</param>
		/// <param name="p_actRemapCategoryAssignments">The callback used to remap affected mod assignments.</param>
		public void RepairBundledRepositoryCategories(string p_strDefaultCategories, Action<IDictionary<Int32, Int32>> p_actRemapCategoryAssignments)
		{
			List<IModCategory> repositoryCategories = GetRepositoryCategories(ParseCategoryDocument(p_strDefaultCategories));
			List<IModCategory> missingCategories = repositoryCategories
				.Where(category =>
				{
					if (category == null || category.Id == 0)
						return false;

					IModCategory existingCategory = FindCategoryInternal(category.Id);
					return existingCategory == null || IsCustomCategory(existingCategory);
				})
				.ToList();

			if (missingCategories.Count > 0)
				MergeRepositoryCategories(missingCategories, p_actRemapCategoryAssignments);
		}

		/// <summary>
		/// Merges repository categories without overwriting user-created categories that share an ID.
		/// </summary>
		/// <param name="p_enmRepositoryCategories">The repository categories to merge.</param>
		/// <param name="p_actRemapCategoryAssignments">The callback used to apply old-to-new category ID mappings.</param>
		public void MergeRepositoryCategories(IEnumerable<IModCategory> p_enmRepositoryCategories, Action<IDictionary<Int32, Int32>> p_actRemapCategoryAssignments)
		{
			if (p_enmRepositoryCategories == null)
				return;

			List<IModCategory> repositoryCategories = p_enmRepositoryCategories
				.Where(category => category != null && category.Id != 0 && !String.IsNullOrWhiteSpace(category.CategoryName))
				.GroupBy(category => category.Id)
				.Select(group => group.Last())
				.ToList();
			bool categoriesChanged = PrepareRepositoryCategories(repositoryCategories, p_actRemapCategoryAssignments);

			using (m_tslCategories.BeginUpdate())
			{
				foreach (IModCategory repositoryCategory in repositoryCategories)
				{
					IModCategory existingCategory = FindCategoryInternal(repositoryCategory.Id);
					if (existingCategory == null)
					{
						m_tslCategories.Add(CreateRepositoryCategory(repositoryCategory.Id, repositoryCategory.CategoryName));
						categoriesChanged = true;
						continue;
					}

					string repositoryPath = Path.Combine(ModInstallDirectory, repositoryCategory.CategoryName);
					if (!String.Equals(existingCategory.CategoryName, repositoryCategory.CategoryName, StringComparison.Ordinal) ||
						!String.Equals(existingCategory.CategoryPath, repositoryPath, StringComparison.OrdinalIgnoreCase) || IsCustomCategory(existingCategory))
					{
						existingCategory.CategoryName = repositoryCategory.CategoryName;
						existingCategory.CategoryPath = repositoryPath;
						m_hstCustomCategoryIds.Remove(existingCategory.Id);
						categoriesChanged = true;
					}
				}
			}

			if (categoriesChanged)
			{
				SaveCategories();
				OnCategoriesChanged();
			}
		}

		/// <summary>
		/// Reconciles existing categories with repository categories that have the same name before resolving remaining custom ID collisions.
		/// </summary>
		/// <param name="p_lstRepositoryCategories">The repository categories being applied.</param>
		/// <param name="p_actRemapCategoryAssignments">The callback used to remap affected mod assignments.</param>
		/// <returns><c>true</c> when category ownership, IDs, or definitions changed; otherwise <c>false</c>.</returns>
		private bool PrepareRepositoryCategories(IList<IModCategory> p_lstRepositoryCategories, Action<IDictionary<Int32, Int32>> p_actRemapCategoryAssignments)
		{
			if (p_lstRepositoryCategories == null)
				p_lstRepositoryCategories = new List<IModCategory>();

			Dictionary<String, List<IModCategory>> repositoryCategoriesByName = new Dictionary<String, List<IModCategory>>(StringComparer.OrdinalIgnoreCase);
			foreach (IModCategory repositoryCategory in p_lstRepositoryCategories)
			{
				String categoryName = NormalizeCategoryName(repositoryCategory == null ? null : repositoryCategory.CategoryName);
				if (repositoryCategory == null || repositoryCategory.Id == 0 || categoryName.Length == 0)
					continue;

				List<IModCategory> categoriesWithName;
				if (!repositoryCategoriesByName.TryGetValue(categoryName, out categoriesWithName))
				{
					categoriesWithName = new List<IModCategory>();
					repositoryCategoriesByName.Add(categoryName, categoriesWithName);
				}
				categoriesWithName.Add(repositoryCategory);
			}

			List<IModCategory> categoriesToFold = new List<IModCategory>();
			List<IModCategory> categoriesToMarkAsRepository = new List<IModCategory>();
			HashSet<Int32> foldedCategoryIds = new HashSet<Int32>();
			Dictionary<Int32, Int32> repositoryNameRemaps = new Dictionary<Int32, Int32>();
			foreach (IModCategory existingCategory in Categories.Where(category => category != null && category.Id != 0).ToList())
			{
				List<IModCategory> repositoryNameMatches;
				String categoryName = NormalizeCategoryName(existingCategory.CategoryName);
				if (categoryName.Length == 0 || !repositoryCategoriesByName.TryGetValue(categoryName, out repositoryNameMatches) || repositoryNameMatches.Count != 1)
					continue;

				IModCategory repositoryCategory = repositoryNameMatches[0];
				if (existingCategory.Id == repositoryCategory.Id)
				{
					if (IsCustomCategory(existingCategory))
						categoriesToMarkAsRepository.Add(existingCategory);
					continue;
				}

				categoriesToFold.Add(existingCategory);
				foldedCategoryIds.Add(existingCategory.Id);
				repositoryNameRemaps.Add(existingCategory.Id, repositoryCategory.Id);
			}

			HashSet<Int32> repositoryIds = new HashSet<Int32>(p_lstRepositoryCategories.Where(category => category != null).Select(category => category.Id));
			Dictionary<Int32, Int32> customCategoryRemaps = BuildCustomCategoryRemaps(repositoryIds, foldedCategoryIds);
			Dictionary<Int32, Int32> assignmentRemaps = new Dictionary<Int32, Int32>(customCategoryRemaps);
			foreach (KeyValuePair<Int32, Int32> repositoryNameRemap in repositoryNameRemaps)
				assignmentRemaps.Add(repositoryNameRemap.Key, repositoryNameRemap.Value);

			if (assignmentRemaps.Count > 0)
			{
				if (p_actRemapCategoryAssignments == null)
					throw new InvalidOperationException("A category assignment remapper is required to migrate category IDs.");
				p_actRemapCategoryAssignments(assignmentRemaps);
			}

			using (m_tslCategories.BeginUpdate())
			{
				foreach (IModCategory customCategory in Categories.Where(IsCustomCategory).ToList())
				{
					Int32 newId;
					if (!customCategoryRemaps.TryGetValue(customCategory.Id, out newId))
						continue;

					m_hstCustomCategoryIds.Remove(customCategory.Id);
					customCategory.Id = newId;
					m_hstCustomCategoryIds.Add(newId);
				}

				foreach (IModCategory categoryToFold in categoriesToFold)
				{
					m_hstCustomCategoryIds.Remove(categoryToFold.Id);
					m_tslCategories.Remove(categoryToFold);
				}

				foreach (IModCategory categoryToMarkAsRepository in categoriesToMarkAsRepository)
					m_hstCustomCategoryIds.Remove(categoryToMarkAsRepository.Id);
			}

			return categoriesToFold.Count > 0 || categoriesToMarkAsRepository.Count > 0 || customCategoryRemaps.Count > 0;
		}

		/// <summary>
		/// Builds ID migrations for custom categories whose IDs are being reclaimed by repository categories.
		/// </summary>
		/// <param name="p_setReservedIds">Repository IDs that must remain available.</param>
		/// <param name="p_setExcludedCustomIds">Custom category IDs that will be folded into repository categories by name.</param>
		/// <returns>The old-to-new custom category ID mappings.</returns>
		private Dictionary<Int32, Int32> BuildCustomCategoryRemaps(ISet<Int32> p_setReservedIds, ISet<Int32> p_setExcludedCustomIds)
		{
			HashSet<Int32> usedIds = new HashSet<Int32>(Categories.Select(category => category.Id));
			if (p_setReservedIds != null)
				usedIds.UnionWith(p_setReservedIds);

			Dictionary<Int32, Int32> categoryRemaps = new Dictionary<Int32, Int32>();
			foreach (IModCategory customCategory in Categories.Where(IsCustomCategory).ToList())
			{
				if (p_setExcludedCustomIds != null && p_setExcludedCustomIds.Contains(customCategory.Id))
					continue;

				bool hasRepositoryCollision = p_setReservedIds != null && p_setReservedIds.Contains(customCategory.Id);
				// Legacy custom categories may still use repository-range IDs. Do not migrate
				// them merely to normalize the number: repository CategoryId assignments use
				// the same numeric namespace and would be orphaned if no repository category
				// is being installed at that ID in this operation. Move the custom definition
				// only when the repository is actively reclaiming its ID.
				if (!hasRepositoryCollision)
					continue;

				Int32 newId = FindNextCustomCategoryId(usedIds);
				categoryRemaps.Add(customCategory.Id, newId);
				usedIds.Add(newId);
			}

			return categoryRemaps;
		}

		/// <summary>
		/// Normalizes a category name for exact repository-name matching.
		/// </summary>
		/// <param name="p_strCategoryName">The category name to normalize.</param>
		/// <returns>The trimmed category name, or an empty string.</returns>
		private static String NormalizeCategoryName(String p_strCategoryName)
		{
			return String.IsNullOrWhiteSpace(p_strCategoryName) ? String.Empty : p_strCategoryName.Trim();
		}

		/// <summary>
		/// Gets the next unused ID in the custom-category range.
		/// </summary>
		/// <param name="p_setAdditionalUsedIds">Additional IDs that must not be allocated.</param>
		/// <returns>An unused custom-category ID.</returns>
		private Int32 GetNextCustomCategoryId(ISet<Int32> p_setAdditionalUsedIds)
		{
			HashSet<Int32> usedIds = new HashSet<Int32>(Categories.Select(category => category.Id));
			if (p_setAdditionalUsedIds != null)
				usedIds.UnionWith(p_setAdditionalUsedIds);
			return FindNextCustomCategoryId(usedIds);
		}

		/// <summary>
		/// Finds the first unused ID in the custom-category range.
		/// </summary>
		/// <param name="p_setUsedIds">The category IDs that are already reserved.</param>
		/// <returns>An unused custom-category ID.</returns>
		private static Int32 FindNextCustomCategoryId(ISet<Int32> p_setUsedIds)
		{
			Int32 nextId = FIRST_CUSTOM_CATEGORY_ID;
			while (p_setUsedIds.Contains(nextId))
			{
				if (nextId == Int32.MaxValue)
					throw new InvalidOperationException("No free custom category IDs are available.");
				nextId++;
			}

			return nextId;
		}

		/// <summary>
		/// Determines whether a category is owned by the user.
		/// </summary>
		/// <param name="p_mctCategory">The category to inspect.</param>
		/// <returns><c>true</c> for a user-created category; otherwise <c>false</c>.</returns>
		private bool IsCustomCategory(IModCategory p_mctCategory)
		{
			return p_mctCategory != null && m_hstCustomCategoryIds.Contains(p_mctCategory.Id);
		}

		/// <summary>
		/// Replaces the current collection with categories from the specified repository document.
		/// </summary>
		/// <param name="p_docCategories">The repository category document, or <c>null</c> for only Unassigned.</param>
		private void ResetCategories(XDocument p_docCategories)
		{
			using (m_tslCategories.BeginUpdate())
			{
				m_tslCategories.Clear();
				m_hstCustomCategoryIds.Clear();
				if (p_docCategories != null)
					LoadCategories(p_docCategories, null, true);
				EnsureUnassignedCategory();
			}

			SaveCategories();
			OnCategoriesChanged();
		}

		/// <summary>
		/// Creates a repository-owned category with a normalized installation path.
		/// </summary>
		/// <param name="p_intCategoryId">The repository category ID.</param>
		/// <param name="p_strCategoryName">The repository category name.</param>
		/// <returns>The repository-owned category.</returns>
		private IModCategory CreateRepositoryCategory(Int32 p_intCategoryId, String p_strCategoryName)
		{
			return new ModCategory(p_intCategoryId, p_strCategoryName, Path.Combine(ModInstallDirectory, p_strCategoryName));
		}

		/// <summary>
		/// Parses a serialized category document when one was supplied.
		/// </summary>
		/// <param name="p_strCategories">The serialized category document.</param>
		/// <returns>The parsed document, or <c>null</c> when the input is empty.</returns>
		private static XDocument ParseCategoryDocument(String p_strCategories)
		{
			return String.IsNullOrWhiteSpace(p_strCategories) ? null : XDocument.Parse(p_strCategories);
		}

		/// <summary>
		/// Reads repository categories from a category document.
		/// </summary>
		/// <param name="p_docCategories">The repository category document.</param>
		/// <returns>The repository categories contained in the document.</returns>
		private static List<IModCategory> GetRepositoryCategories(XDocument p_docCategories)
		{
			List<IModCategory> categories = new List<IModCategory>();
			if (p_docCategories == null)
				return categories;

			XElement xelCategoryList = p_docCategories.Descendants("categoryList").FirstOrDefault();
			if (xelCategoryList == null)
				return categories;

			foreach (XElement xelCategory in xelCategoryList.Elements("category"))
			{
				XAttribute xatId = xelCategory.Attribute("ID");
				XElement xelName = xelCategory.Element("name");
				Int32 categoryId;
				if (xatId == null || xelName == null || !Int32.TryParse(xatId.Value, out categoryId))
					continue;
				categories.Add(new ModCategory(categoryId, xelName.Value, xelName.Value));
			}

			return categories;
		}

		/// <summary>
		/// Builds a repository category name lookup from a category document.
		/// </summary>
		/// <param name="p_docCategories">The repository category document.</param>
		/// <returns>Repository category names keyed by ID.</returns>
		private static Dictionary<Int32, String> GetRepositoryCategoryNames(XDocument p_docCategories)
		{
			Dictionary<Int32, String> categories = new Dictionary<Int32, String>();
			if (p_docCategories == null)
				return categories;

			XElement xelCategoryList = p_docCategories.Descendants("categoryList").FirstOrDefault();
			if (xelCategoryList == null)
				return categories;

			foreach (XElement xelCategory in xelCategoryList.Elements("category"))
			{
				XAttribute xatId = xelCategory.Attribute("ID");
				XElement xelName = xelCategory.Element("name");
				Int32 categoryId;
				if (xatId == null || xelName == null || !Int32.TryParse(xatId.Value, out categoryId))
					continue;
				categories[categoryId] = xelName.Value;
			}

			return categories;
		}

		/// <summary>
		/// Ensures that the special Unassigned category exists exactly once.
		/// </summary>
		private void EnsureUnassignedCategory()
		{
			m_hstCustomCategoryIds.Remove(0);
			if (FindCategoryInternal(0) == null)
				m_tslCategories.Insert(0, new ModCategory());
		}

		/// <summary>
		/// Saves the category data to the category file.
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
					new XAttribute(CUSTOM_ATTRIBUTE, IsCustomCategory(mct)),
					new XElement("name", new XText(mct.CategoryName))));

			if (!Directory.Exists(CategoryPath))
				Directory.CreateDirectory(CategoryPath);
			docCategories.Save(CategoryFilePath);
		}

		#endregion

		#region Category Management

		/// <summary>
		/// Adds a new user-created category to the list with default values.
		/// </summary>
		public IModCategory AddCategory()
		{
			Int32 nextId = GetNextId;
			Int32 nameSuffix = 1;
			String categoryName;
			do
			{
				categoryName = "New" + nameSuffix++;
			}
			while (Categories.Any(category => String.Equals(category.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase)));

			return AddCategory(new ModCategory(nextId, categoryName, categoryName));
		}

		/// <summary>
		/// Adds a category to the list.
		/// </summary>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> being added.</param>
		public IModCategory AddCategory(IModCategory p_mctCategory)
		{
			if (p_mctCategory == null)
				throw new ArgumentNullException("p_mctCategory");
			if (FindCategoryInternal(p_mctCategory.Id) != null)
				throw new InvalidOperationException(String.Format("A category with ID {0} already exists.", p_mctCategory.Id));

			m_tslCategories.Add(p_mctCategory);
			m_hstCustomCategoryIds.Add(p_mctCategory.Id);
			SaveCategories();
			OnCategoriesChanged();
			return p_mctCategory;
		}

		/// <summary>
		/// Updates the category file.
		/// </summary>
		public void UpdateCategoryFile()
		{
			SaveCategories();
			OnCategoriesChanged();
		}

		/// <summary>
		/// Renames a category without changing its repository or custom ownership.
		/// </summary>
		/// <param name="p_intCategoryId">The category ID.</param>
		/// <param name="p_strNewName">The new category name.</param>
		public void RenameCategory(int p_intCategoryId, string p_strNewName)
		{
			IModCategory imcCategory = FindCategoryInternal(p_intCategoryId);
			if (imcCategory == null)
				return;

			imcCategory.CategoryName = p_strNewName;
			imcCategory.CategoryPath = Path.Combine(ModInstallDirectory, p_strNewName);
			SaveCategories();
			OnCategoriesChanged();
		}

		/// <summary>
		/// Removes a category from the list.
		/// </summary>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> to be removed.</param>
		public void RemoveCategory(IModCategory p_mctCategory)
		{
			if (p_mctCategory != null)
				m_hstCustomCategoryIds.Remove(p_mctCategory.Id);
			m_tslCategories.Remove(p_mctCategory);
			SaveCategories();
			OnCategoriesChanged();
		}

		/// <summary>
		/// Finds the category by ID.
		/// </summary>
		/// <param name="p_intCategoryId">The category ID.</param>
		/// <returns>The category, or Unassigned when no matching category exists.</returns>
		public IModCategory FindCategory(Int32 p_intCategoryId)
		{
			IModCategory imcCategory = FindCategoryInternal(p_intCategoryId);
			return imcCategory ?? new ModCategory();
		}

		/// <summary>
		/// Finds the category by ID without substituting Unassigned for a missing category.
		/// </summary>
		/// <param name="p_intCategoryId">The category ID.</param>
		/// <returns>The category, or <c>null</c> when no category matches.</returns>
		private IModCategory FindCategoryInternal(Int32 p_intCategoryId)
		{
			return m_tslCategories.Find(item => item.Id == p_intCategoryId);
		}

		/// <summary>
		/// Raises the <see cref="CategoriesChanged"/> event.
		/// </summary>
		private void OnCategoriesChanged()
		{
			CategoriesChanged(this, EventArgs.Empty);
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
					FileUtil.Move(CategoryFilePath, strBackupCategoryPath, true);
				}
			}
		}

		/// <summary>
		/// This restores the first valid backup of the category file.
		/// </summary>
		/// <param name="p_strCategoryPath">The path to the category folder.</param>
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
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_ModManager">The Mod Manager.</param>
		/// <param name="p_lstMods">The list of mods to update.</param>
		/// <param name="p_intNewValue">The new category id value.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask Update(ModManager p_ModManager, IList<IMod> p_lstMods, Int32 p_intNewValue, ConfirmActionMethod p_camConfirm)
		{
			CategorySwitchTask cstCategorySwitch = new CategorySwitchTask(p_ModManager, p_lstMods, p_intNewValue);
			cstCategorySwitch.Update(p_camConfirm);
			return cstCategorySwitch;
		}

		/// <summary>
		/// This disposes of the category manager, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
		}
	}
}
