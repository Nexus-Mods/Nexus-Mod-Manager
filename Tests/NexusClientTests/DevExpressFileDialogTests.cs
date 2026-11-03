using System;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies file-dialog history without showing dialogs or changing user settings.</summary>
	[TestFixture]
	public class DevExpressFileDialogTests
	{
		private string _root;
		private KeyedSettings<string> _preferences;
		private ISettings _settings;
		private int _saves;

		/// <summary>Creates isolated folders and an in-memory preference store.</summary>
		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-file-dialog-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
			_preferences = new KeyedSettings<string>();
			_saves = 0;
			_settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				if (method.Name == "get_DockPanelLayouts") return _preferences;
				if (method.Name == "Save") _saves++;
				return null;
			});
		}

		/// <summary>Removes only the temporary directory owned by this test.</summary>
		[TearDown]
		public void TearDown()
		{
			Directory.Delete(_root, true);
		}

		/// <summary>Accepted open/save paths survive the existing XML settings serialization.</summary>
		[Test]
		public void AcceptedSelection_RestoresFolderAfterSettingsRoundTrip()
		{
			DevExpressFileDialog.RememberSelection(_settings, "Export", Path.Combine(_root, "new-report.json"), DialogResult.OK);
			XmlSerializer serializer = new XmlSerializer(typeof(KeyedSettings<string>));
			using (StringWriter writer = new StringWriter())
			{
				serializer.Serialize(writer, _preferences);
				using (StringReader reader = new StringReader(writer.ToString()))
					_preferences = (KeyedSettings<string>)serializer.Deserialize(reader);
			}

			Assert.That(DevExpressFileDialog.GetInitialDirectory(_settings, "Export", string.Empty), Is.EqualTo(_root));
			Assert.That(_saves, Is.EqualTo(1));
		}

		/// <summary>Canceling another selection cannot replace an accepted folder.</summary>
		[Test]
		public void CancelledSelection_DoesNotReplaceHistory()
		{
			DevExpressFileDialog.RememberSelection(_settings, "Import", Path.Combine(_root, "mod.zip"), DialogResult.OK);
			DevExpressFileDialog.RememberSelection(_settings, "Import", Path.Combine(Path.GetTempPath(), "other.zip"), DialogResult.Cancel);

			Assert.That(DevExpressFileDialog.GetInitialDirectory(_settings, "Import", null), Is.EqualTo(_root));
			Assert.That(_saves, Is.EqualTo(1));
		}

		/// <summary>Unrelated actions and explicitly requested folders retain their own starting locations.</summary>
		[Test]
		public void History_IsScopedAndPreservesExplicitDirectory()
		{
			DevExpressFileDialog.RememberSelection(_settings, "Mods", Path.Combine(_root, "mod.zip"), DialogResult.OK);

			Assert.That(DevExpressFileDialog.GetInitialDirectory(_settings, "Plugins", null), Is.Null);
			Assert.That(DevExpressFileDialog.GetInitialDirectory(_settings, "Mods", Path.GetTempPath()), Is.EqualTo(Path.GetTempPath()));
		}

		/// <summary>Deleted folders fall back without changing or deleting stored preferences.</summary>
		[Test]
		public void DeletedFolder_FallsBackToDefault()
		{
			string folder = Directory.CreateDirectory(Path.Combine(_root, "removed")).FullName;
			DevExpressFileDialog.RememberSelection(_settings, "Import", Path.Combine(folder, "mod.zip"), DialogResult.OK);
			Directory.Delete(folder);

			Assert.That(DevExpressFileDialog.GetInitialDirectory(_settings, "Import", string.Empty), Is.Empty);
			Assert.That(_saves, Is.EqualTo(1));
		}

		/// <summary>Malformed paths never interrupt the user's open/save action.</summary>
		[TestCase(null)]
		[TestCase("")]
		[TestCase("relative.json")]
		[TestCase("\0")]
		public void InvalidSelection_DoesNotWritePreferences(string fileName)
		{
			Assert.DoesNotThrow(() => DevExpressFileDialog.RememberSelection(_settings, "Export", fileName, DialogResult.OK));
			Assert.That(_preferences, Is.Empty);
			Assert.That(_saves, Is.Zero);
		}

		/// <summary>A settings write failure must not prevent the caller from processing the selected file.</summary>
		[Test]
		public void SaveFailure_RemainsNonfatal()
		{
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				if (method.Name == "get_DockPanelLayouts") return _preferences;
				if (method.Name == "Save") throw new IOException("Preference store unavailable.");
				return null;
			});

			Assert.DoesNotThrow(() => DevExpressFileDialog.RememberSelection(settings, "Export", Path.Combine(_root, "report.json"), DialogResult.OK));
		}
	}
}
