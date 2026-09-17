namespace NexusClientTests
{
	using System;
	using System.ComponentModel;
	using System.Drawing;
	using System.Drawing.Imaging;
	using System.IO;
	using System.Reflection;
	using System.Runtime.Serialization;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModRepositories;
	using Nexus.Client.Mods.Formats.FOMod;
	using Nexus.Client.Util;
	using NUnit.Framework;

	/// <summary>
	/// Protects metadata refreshes from loading archive screenshots when no image update is required.
	/// </summary>
	public class MetadataRefreshPerformanceTests
	{
		[Test]
		public void MetadataOnlyFalseUpdate_DoesNotLoadUnloadedScreenshot()
		{
			FOMod mod = CreateMod(false);
			mod.UpdateInfo(CreateInfo(), false);
			Assert.IsNull(GetScreenshotField(mod));
			Assert.AreEqual("fomod/screenshot.png", mod.ScreenshotPath);
		}

		[Test]
		public void MetadataOnlyNullUpdate_DoesNotLoadUnloadedScreenshot()
		{
			FOMod mod = CreateMod(false);
			mod.UpdateInfo(CreateInfo(), null);
			Assert.IsNull(GetScreenshotField(mod));
			Assert.AreEqual("fomod/screenshot.png", mod.ScreenshotPath);
		}

		[Test]
		public void MetadataOnlyUpdate_PreservesLoadedScreenshot()
		{
			ExtendedImage original = CreateImage();
			FOMod mod = CreateMod(true, original);
			mod.UpdateInfo(CreateInfo(), false);
			Assert.AreSame(original, mod.Screenshot);
		}

		[TestCase(-1)]
		[TestCase(0)]
		[TestCase(77)]
		public void ExplicitCategoryUpdate_AppliesCategoryWithoutLoadingScreenshot(int categoryId)
		{
			FOMod mod = CreateMod(false);
			ModInfo info = CreateInfo();
			info.CustomCategoryId = categoryId;
			info.ForceCustomCategoryId = true;
			mod.UpdateInfo(info, false);
			Assert.AreEqual(categoryId, mod.CustomCategoryId);
			Assert.IsNull(GetScreenshotField(mod));
		}

		[Test]
		public void RepositoryCategoryRefresh_ChangesOnlyRepositoryCategoryWithoutLoadingScreenshot()
		{
			FOMod mod = CreateMod(false);
			var updater = (AutoUpdater)FormatterServices.GetUninitializedObject(typeof(AutoUpdater));

			updater.ApplyRepositoryCategory(mod, 77);

			Assert.AreEqual(77, mod.CategoryId);
			Assert.AreEqual(5, mod.CustomCategoryId);
			Assert.AreEqual("100", mod.Id);
			Assert.AreEqual("200", mod.DownloadId);
			Assert.AreEqual("Original name", mod.ModName);
			Assert.AreEqual("1.0", mod.HumanReadableVersion);
			Assert.AreEqual("1.0", mod.LastKnownVersion);
			Assert.AreEqual(false, mod.IsEndorsed);
			Assert.AreEqual("Author", mod.Author);
			Assert.AreEqual("Description", mod.Description);
			Assert.IsTrue(mod.UpdateWarningEnabled);
			Assert.IsTrue(mod.UpdateChecksEnabled);
			Assert.IsNull(GetScreenshotField(mod));
			Assert.AreEqual("fomod/screenshot.png", mod.ScreenshotPath);
		}

		[Test]
		public void CategoryUpdate_PreservesUnrelatedScalarValues()
		{
			FOMod mod = CreateMod(false);
			ModInfo info = CreateInfo();
			info.CustomCategoryId = 77;
			info.ForceCustomCategoryId = true;
			mod.UpdateInfo(info, false);
			Assert.AreEqual("100", mod.Id);
			Assert.AreEqual("200", mod.DownloadId);
			Assert.AreEqual("Original name", mod.ModName);
			Assert.AreEqual("1.0", mod.HumanReadableVersion);
			Assert.AreEqual("Author", mod.Author);
			Assert.AreEqual("Description", mod.Description);
		}

		[Test]
		public void ExplicitOverwrite_ClearsLoadedScreenshot()
		{
			FOMod mod = CreateMod(true, CreateImage());
			mod.UpdateInfo(CreateInfo(), true);
			Assert.IsNull(mod.Screenshot);
			Assert.IsNull(mod.ScreenshotPath);
		}

		[TestCase(false)]
		[TestCase(null)]
		[TestCase(true)]
		public void SuppliedScreenshot_ReplacesCurrentImage(bool? overwriteAllValues)
		{
			FOMod mod = CreateMod(true, CreateImage());
			ExtendedImage replacement = CreateImage();
			ModInfo info = CreateInfo();
			info.Screenshot = replacement;
			mod.UpdateInfo(info, overwriteAllValues);
			Assert.AreSame(replacement, mod.Screenshot);
			Assert.AreEqual("fomod/screenshot.png", mod.ScreenshotPath);
		}

		private static FOMod CreateMod(bool screenshotLoaded, ExtendedImage screenshot = null)
		{
			var mod = (FOMod)FormatterServices.GetUninitializedObject(typeof(FOMod));
			SetBaseField(mod, "PropertyChanged", new PropertyChangedEventHandler((sender, args) => { }));
			SetField(mod, "_modId", "100");
			SetField(mod, "_downloadId", "200");
			SetField(mod, "_modName", "Original name");
			SetField(mod, "_fileName", "archive.7z");
			SetField(mod, "_humanReadableVersion", "1.0");
			SetField(mod, "_lastKnownVersion", "1.0");
			SetField(mod, "_isEndorsed", (bool?)false);
			SetField(mod, "_machineVersion", new Version(1, 0));
			SetField(mod, "_author", "Author");
			SetField(mod, "_categoryId", 10);
			SetField(mod, "_customCategoryId", 5);
			SetField(mod, "_description", "Description");
			SetField(mod, "_installDate", "2026-09-17");
			SetField(mod, "_website", new Uri("https://www.nexusmods.com/"));
			SetField(mod, "_updateWarningEnabled", true);
			SetField(mod, "_updateChecksEnabled", true);
			SetField(mod, "<ModArchivePath>k__BackingField", "archive.7z");
			SetField(mod, "<ScreenshotPath>k__BackingField", "fomod/screenshot.png");
			if (screenshotLoaded)
				SetField(mod, "_screenshot", screenshot ?? CreateImage());
			return mod;
		}

		private static ModInfo CreateInfo()
		{
			return new ModInfo
			{
				Id = "100", DownloadId = "200", ModName = "Original name", FileName = "archive.7z",
				HumanReadableVersion = "1.0", LastKnownVersion = "1.0", IsEndorsed = false,
				MachineVersion = new Version(1, 0), Author = "Author", CategoryId = 10, CustomCategoryId = 5,
				Description = "Description", InstallDate = "2026-09-17", Website = new Uri("https://www.nexusmods.com/"),
				UpdateWarningEnabled = true, UpdateChecksEnabled = true
			};
		}

		private static ExtendedImage CreateImage()
		{
			using (var bitmap = new Bitmap(1, 1))
			using (var stream = new MemoryStream())
			{
				bitmap.Save(stream, ImageFormat.Png);
				return new ExtendedImage(stream.ToArray());
			}
		}

		private static ExtendedImage GetScreenshotField(FOMod mod)
		{
			return (ExtendedImage)typeof(FOMod).GetField("_screenshot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mod);
		}

		private static void SetField(FOMod mod, string name, object value)
		{
			typeof(FOMod).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mod, value);
		}

		private static void SetBaseField(FOMod mod, string name, object value)
		{
			typeof(FOMod).BaseType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mod, value);
		}
	}
}
