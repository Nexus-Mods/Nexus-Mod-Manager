namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Xml.Linq;

	using Nexus.Client.ModManagement;
	using Nexus.Client.ModRepositories;
	using Nexus.Client.Mods;

	using NUnit.Framework;

	/// <summary>
	/// Verifies custom category ownership, ID migration, and repository category merging.
	/// </summary>
	public class CategoryManagerTests
	{
		private const string DefaultCategories = @"<?xml version=""1.0"" encoding=""utf-8""?>
<categoryManager fileVersion=""0.1.0.0"">
  <categoryList>
    <category path=""Unassigned"" ID=""0""><name>Unassigned</name></category>
    <category path=""Fallout 4"" ID=""1""><name>Fallout 4</name></category>
    <category path=""Clothing - Backpacks"" ID=""49""><name>Clothing - Backpacks</name></category>
  </categoryList>
</categoryManager>";

		private string _rootPath;
		private string _categoryDirectory;
		private string _categoryFile;

		/// <summary>
		/// Creates an isolated category directory for each test.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			_rootPath = Path.Combine(Path.GetTempPath(), "NMM.CategoryManagerTests." + Guid.NewGuid().ToString("N"));
			_categoryDirectory = Path.Combine(_rootPath, "categories");
			_categoryFile = Path.Combine(_categoryDirectory, "Categories.xml");
			Directory.CreateDirectory(_categoryDirectory);
		}

		/// <summary>
		/// Removes the isolated category directory after each test.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_rootPath))
				Directory.Delete(_rootPath, true);
		}

		/// <summary>
		/// Ensures new custom categories use the reserved ID range and persist their ownership.
		/// </summary>
		[Test]
		public void NewCustomCategoryUsesReservedRangeAndPersistsOwnership()
		{
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);

			IModCategory category = manager.AddCategory();

			Assert.GreaterOrEqual(category.Id, 1000000);
			Assert.IsTrue(IsSavedAsCustom(category.Id));
		}

		/// <summary>
		/// Ensures legacy categories that conflict with repository definitions are classified as custom.
		/// </summary>
		[Test]
		public void LegacyCategoryNameConflictIsClassifiedAsCustom()
		{
			WriteLegacyCategories();
			CategoryManager manager = CreateManager();

			manager.LoadCategories(DefaultCategories);

			Assert.IsTrue(IsSavedAsCustom(49));
			Assert.IsTrue(IsSavedAsCustom(54));
			Assert.IsFalse(IsSavedAsCustom(1));
		}

		/// <summary>
		/// Ensures legacy categories are preserved as custom when no repository baseline is available.
		/// </summary>
		[Test]
		public void LegacyCategoriesWithoutDefaultsArePreservedAsCustom()
		{
			WriteLegacyCategories();
			CategoryManager manager = CreateManager();

			manager.LoadCategories(String.Empty);

			Assert.IsTrue(IsSavedAsCustom(1));
			Assert.IsTrue(IsSavedAsCustom(49));
			Assert.IsTrue(IsSavedAsCustom(54));
		}

		/// <summary>
		/// Ensures repository updates migrate custom collisions before adding repository definitions.
		/// </summary>
		[Test]
		public void RepositoryMergeMovesCustomCollisionBeforeAddingRepositoryCategory()
		{
			WriteLegacyCategories();
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);
			Dictionary<Int32, Int32> remaps = null;
			List<IModCategory> repositoryCategories = new List<IModCategory>
			{
				new ModCategory(49, "Clothing - Backpacks", "Clothing - Backpacks")
			};

			manager.MergeRepositoryCategories(repositoryCategories, mappings => remaps = new Dictionary<Int32, Int32>(mappings));

			Assert.IsNotNull(remaps);
			Assert.IsTrue(remaps.ContainsKey(49));
			Assert.IsFalse(remaps.ContainsKey(54));
			Assert.GreaterOrEqual(remaps[49], 1000000);
			Assert.AreEqual("Clothing - Backpacks", manager.FindCategory(49).CategoryName);
			Assert.IsFalse(IsSavedAsCustom(49));
			IModCategory movedCustom = manager.Categories.Single(category => category.Id == remaps[49]);
			Assert.AreEqual("Temp. uninstall and remove mods.", movedCustom.CategoryName);
			Assert.IsTrue(IsSavedAsCustom(movedCustom.Id));
			Assert.AreEqual("Temp uninstall mods.", manager.FindCategory(54).CategoryName);
			Assert.IsTrue(IsSavedAsCustom(54));
		}

		/// <summary>
		/// Ensures repository resets preserve custom definitions while resolving ID collisions.
		/// </summary>
		[Test]
		public void RepositoryResetPreservesCustomCategoriesAndResolvesCollisions()
		{
			WriteLegacyCategories();
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);
			Dictionary<Int32, Int32> remaps = null;

			manager.ResetRepositoryCategories(DefaultCategories, mappings => remaps = new Dictionary<Int32, Int32>(mappings));

			Assert.IsNotNull(remaps);
			Assert.IsTrue(remaps.ContainsKey(49));
			Assert.IsFalse(remaps.ContainsKey(54));
			Assert.AreEqual("Clothing - Backpacks", manager.FindCategory(49).CategoryName);
			Assert.AreEqual("Temp uninstall mods.", manager.FindCategory(54).CategoryName);
			Assert.IsTrue(IsSavedAsCustom(54));
			Assert.IsTrue(manager.Categories.Any(category => category.CategoryName == "Temp. uninstall and remove mods." && IsSavedAsCustom(category.Id)));
			Assert.IsTrue(manager.Categories.Any(category => category.CategoryName == "Temp uninstall mods." && IsSavedAsCustom(category.Id)));
		}

		/// <summary>
		/// Ensures startup recovery reclaims a bundled repository ID occupied by a legacy custom
		/// category while leaving unrelated legacy custom IDs untouched.
		/// </summary>
		[Test]
		public void RepairBundledRepositoryCategoriesReclaimsLegacyCollisionOnly()
		{
			WriteLegacyCategories();
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);
			Dictionary<Int32, Int32> remaps = null;

			manager.RepairBundledRepositoryCategories(DefaultCategories,
				mappings => remaps = new Dictionary<Int32, Int32>(mappings));

			Assert.IsNotNull(remaps);
			Assert.IsTrue(remaps.ContainsKey(49));
			Assert.IsFalse(remaps.ContainsKey(54));
			Assert.AreEqual("Clothing - Backpacks", manager.FindCategory(49).CategoryName);
			Assert.IsFalse(IsSavedAsCustom(49));
			Assert.AreEqual("Temp uninstall mods.", manager.FindCategory(54).CategoryName);
			Assert.IsTrue(IsSavedAsCustom(54));
			Assert.IsTrue(manager.Categories.Any(category =>
				category.Id == remaps[49] && category.CategoryName == "Temp. uninstall and remove mods."));
		}

		/// <summary>
		/// Ensures startup recovery restores bundled repository IDs removed by the 0.92.4/0.92.5
		/// legacy custom-ID migration and folds an exact-name migrated definition back to that ID.
		/// </summary>
		[Test]
		public void RepairBundledRepositoryCategoriesRepairsMigratedDefinition()
		{
			WritePersistedCategories(new ModCategory(1000005, "Clothing - Backpacks", "Clothing - Backpacks"));
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);
			Dictionary<Int32, Int32> remaps = null;

			Assert.AreEqual(0, manager.FindCategory(49).Id);

			manager.RepairBundledRepositoryCategories(DefaultCategories,
				mappings => remaps = new Dictionary<Int32, Int32>(mappings));

			Assert.IsNotNull(remaps);
			Assert.AreEqual(49, remaps[1000005]);
			Assert.AreEqual("Clothing - Backpacks", manager.FindCategory(49).CategoryName);
			Assert.IsFalse(IsSavedAsCustom(49));
			Assert.IsFalse(manager.Categories.Any(category => category.Id == 1000005));
		}

		/// <summary>
		/// Ensures a category previously misclassified as custom is folded into the repository category with the same name.
		/// </summary>
		[Test]
		public void RepositoryMergeReconcilesExactNameWithChangedId()
		{
			WritePersistedCategories(new ModCategory(1000001, "Animation", "Animation"));
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);
			Dictionary<Int32, Int32> remaps = null;

			manager.MergeRepositoryCategories(new[] { new ModCategory(51, "Animation", "Animation") },
				mappings => remaps = new Dictionary<Int32, Int32>(mappings));

			Assert.IsNotNull(remaps);
			Assert.AreEqual(51, remaps[1000001]);
			Assert.AreEqual(1, manager.Categories.Count(category => category.CategoryName == "Animation"));
			Assert.AreEqual("Animation", manager.FindCategory(51).CategoryName);
			Assert.IsFalse(IsSavedAsCustom(51));
		}

		/// <summary>
		/// Ensures a stale repository-owned category is also moved to the current repository ID by exact name.
		/// </summary>
		[Test]
		public void RepositoryMergeReconcilesRepositoryOwnedCategoryWithChangedId()
		{
			WritePersistedCategories(false, new ModCategory(67, "Player homes", "Player homes"));
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);
			Dictionary<Int32, Int32> remaps = null;

			manager.MergeRepositoryCategories(new[] { new ModCategory(28, "Player homes", "Player homes") },
				mappings => remaps = new Dictionary<Int32, Int32>(mappings));

			Assert.IsNotNull(remaps);
			Assert.AreEqual(28, remaps[67]);
			Assert.AreEqual(1, manager.Categories.Count(category => category.CategoryName == "Player homes"));
			Assert.AreEqual("Player homes", manager.FindCategory(28).CategoryName);
			Assert.IsFalse(IsSavedAsCustom(28));
		}

		/// <summary>
		/// Ensures name reconciliation and an ID collision can be resolved in the same repository merge.
		/// </summary>
		[Test]
		public void RepositoryMergeReconcilesNameWhileMovingDifferentCustomCollision()
		{
			WritePersistedCategories(
				new ModCategory(51, "My Animation Tools", "My Animation Tools"),
				new ModCategory(1000001, "Animation", "Animation"));
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);
			Dictionary<Int32, Int32> remaps = null;

			manager.MergeRepositoryCategories(new[] { new ModCategory(51, "Animation", "Animation") },
				mappings => remaps = new Dictionary<Int32, Int32>(mappings));

			Assert.IsNotNull(remaps);
			Assert.AreEqual(51, remaps[1000001]);
			Assert.GreaterOrEqual(remaps[51], 1000000);
			Assert.AreEqual("Animation", manager.FindCategory(51).CategoryName);
			Assert.IsTrue(manager.Categories.Any(category => category.Id == remaps[51] && category.CategoryName == "My Animation Tools"));
			Assert.AreEqual(1, manager.Categories.Count(category => category.CategoryName == "Animation"));
		}

		/// <summary>
		/// Ensures a repository reset removes an already-created duplicate custom category when its name exactly matches a repository category.
		/// </summary>
		[Test]
		public void RepositoryResetReconcilesPreviouslyMigratedDuplicateByName()
		{
			const String defaultsWithGameplay = @"<?xml version=""1.0"" encoding=""utf-8""?>
<categoryManager fileVersion=""0.1.0.0"">
  <categoryList>
    <category path=""Unassigned"" ID=""0""><name>Unassigned</name></category>
    <category path=""Gameplay"" ID=""15""><name>Gameplay</name></category>
  </categoryList>
</categoryManager>";
			WritePersistedCategories(new ModCategory(1000005, "Gameplay", "Gameplay"));
			CategoryManager manager = CreateManager();
			manager.LoadCategories(DefaultCategories);
			Dictionary<Int32, Int32> remaps = null;

			manager.ResetRepositoryCategories(defaultsWithGameplay, mappings => remaps = new Dictionary<Int32, Int32>(mappings));

			Assert.IsNotNull(remaps);
			Assert.AreEqual(15, remaps[1000005]);
			Assert.AreEqual(1, manager.Categories.Count(category => category.CategoryName == "Gameplay"));
			Assert.AreEqual("Gameplay", manager.FindCategory(15).CategoryName);
			Assert.IsFalse(IsSavedAsCustom(15));
		}

		/// <summary>
		/// Creates a category manager bound to the isolated test directory.
		/// </summary>
		/// <returns>The category manager used by the test.</returns>
		private CategoryManager CreateManager()
		{
			return new CategoryManager(_rootPath, "categories");
		}

		/// <summary>
		/// Reads the persisted ownership flag for a category.
		/// </summary>
		/// <param name="p_intCategoryId">The category ID to inspect.</param>
		/// <returns><c>true</c> when the category is persisted as custom; otherwise <c>false</c>.</returns>
		private bool IsSavedAsCustom(Int32 p_intCategoryId)
		{
			XElement category = XDocument.Load(_categoryFile)
				.Descendants("category")
				.Single(item => (Int32)item.Attribute("ID") == p_intCategoryId);
			Boolean isCustom;
			return Boolean.TryParse((String)category.Attribute("isCustom"), out isCustom) && isCustom;
		}

		/// <summary>
		/// Writes custom categories with persisted ownership metadata.
		/// </summary>
		/// <param name="p_arrCategories">The custom categories to persist.</param>
		private void WritePersistedCategories(params IModCategory[] p_arrCategories)
		{
			WritePersistedCategories(true, p_arrCategories);
		}

		/// <summary>
		/// Writes categories with the specified persisted ownership metadata.
		/// </summary>
		/// <param name="p_booIsCustom">Whether the categories are user-owned.</param>
		/// <param name="p_arrCategories">The categories to persist.</param>
		private void WritePersistedCategories(Boolean p_booIsCustom, params IModCategory[] p_arrCategories)
		{
			XDocument document = new XDocument(
				new XElement("categoryManager",
					new XAttribute("fileVersion", "0.1.0.0"),
					new XElement("categoryList",
						new XElement("category",
							new XAttribute("path", "Unassigned"),
							new XAttribute("ID", 0),
							new XAttribute("isCustom", false),
							new XElement("name", "Unassigned")),
						p_arrCategories.Select(category => new XElement("category",
							new XAttribute("path", category.CategoryPath),
							new XAttribute("ID", category.Id),
							new XAttribute("isCustom", p_booIsCustom),
							new XElement("name", category.CategoryName))))));
			document.Save(_categoryFile);
		}

		/// <summary>
		/// Writes a legacy category file without ownership metadata.
		/// </summary>
		private void WriteLegacyCategories()
		{
			File.WriteAllText(_categoryFile, @"<?xml version=""1.0"" encoding=""utf-8""?>
<categoryManager fileVersion=""0.1.0.0"">
  <categoryList>
    <category path=""Unassigned"" ID=""0""><name>Unassigned</name></category>
    <category path=""Fallout 4"" ID=""1""><name>Fallout 4</name></category>
    <category path=""Temp. uninstall and remove mods."" ID=""49""><name>Temp. uninstall and remove mods.</name></category>
    <category path=""Temp uninstall mods."" ID=""54""><name>Temp uninstall mods.</name></category>
  </categoryList>
</categoryManager>");
		}
	}
}
