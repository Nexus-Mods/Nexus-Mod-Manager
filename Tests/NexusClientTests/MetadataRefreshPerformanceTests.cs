namespace NexusClientTests
{
	using System;
	using System.ComponentModel;
	using System.Drawing;
	using System.Drawing.Imaging;
	using System.IO;
	using System.Reflection;
	using System.Runtime.Serialization;
	using System.Xml;
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

		/// <summary>Metadata from legacy, default-namespace and prefixed FOMOD archives must deserialize identically.</summary>
		[TestCase("")]
		[TestCase("default")]
		[TestCase("prefixed")]
		public void LoadInfo_ArchiveSchemaNamespace_PreservesMetadata(string namespaceStyle)
		{
			const string schema = "https://raw.githubusercontent.com/BellCubeDev/fomod-schema/master/info.xsd";
			XmlDocument document = new XmlDocument();
			document.LoadXml("<fomod><Name>Additional Clockwork</Name><Author>BellCube Dev</Author>" +
				"<Version MachineVersion='3.0.4'>3.0.4</Version><Id>47087</Id><DownloadId>322413</DownloadId>" +
				"<Website>https://www.nexusmods.com/skyrimspecialedition/mods/47087</Website>" +
				"<Description>Clockwork changes</Description><IsEndorsed>false</IsEndorsed>" +
				"<EndorsementStatus>Abstained</EndorsementStatus></fomod>");
			if (namespaceStyle.Length > 0)
			{
				XmlDocument namespaced = new XmlDocument();
				XmlElement root = namespaced.CreateElement(namespaceStyle == "prefixed" ? "f" : String.Empty, "fomod", schema);
				namespaced.AppendChild(root);
				foreach (XmlNode child in document.DocumentElement.ChildNodes)
				{
					XmlElement element = namespaced.CreateElement(root.Prefix, child.LocalName, schema);
					element.InnerText = child.InnerText;
					foreach (XmlAttribute attribute in child.Attributes) element.SetAttribute(attribute.Name, attribute.Value);
					root.AppendChild(element);
				}
				document = namespaced;
			}
			FOMod mod = CreateMod(false);

			typeof(FOMod).GetMethod("LoadInfo", BindingFlags.Instance | BindingFlags.NonPublic, null,
				new[] { typeof(XmlNode), typeof(bool) }, null).Invoke(mod, new object[] { document, false });

			Assert.AreEqual("Additional Clockwork", mod.ModName);
			Assert.AreEqual("BellCube Dev", mod.Author);
			Assert.AreEqual("3.0.4", mod.HumanReadableVersion);
			Assert.AreEqual(new Version(3, 0, 4), mod.MachineVersion);
			Assert.AreEqual("47087", mod.Id);
			Assert.AreEqual("322413", mod.DownloadId);
			Assert.AreEqual("Clockwork changes", mod.Description);
			Assert.AreEqual(false, mod.IsEndorsed);
			Assert.AreEqual("https://www.nexusmods.com/skyrimspecialedition/mods/47087", mod.Website.AbsoluteUri);
		}

		/// <summary>A malformed metadata root reports an XML error instead of an unexplained null-reference exception.</summary>
		[Test]
		public void LoadInfo_MissingFomodRoot_ReportsXmlError()
		{
			XmlDocument document = new XmlDocument();
			document.LoadXml("<invalid><Name>Invalid metadata</Name></invalid>");
			TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => typeof(FOMod)
				.GetMethod("LoadInfo", BindingFlags.Instance | BindingFlags.NonPublic, null,
					new[] { typeof(XmlNode), typeof(bool) }, null).Invoke(CreateMod(false), new object[] { document, false }));
			Assert.IsInstanceOf<XmlException>(error.InnerException);
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
