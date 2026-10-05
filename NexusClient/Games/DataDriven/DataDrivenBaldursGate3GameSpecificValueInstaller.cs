using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ChinhDo.Transactions;
using Newtonsoft.Json;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>
	/// Applies BG3 PlayerProfiles modsettings module entries through NMM's native game-specific-value ownership stack.
	/// </summary>
	public class DataDrivenBaldursGate3GameSpecificValueInstaller : IGameSpecificValueInstaller,
		IGameSpecificValueInstallDecisionSupport, IGameSpecificValueRestoreSupport
	{
		private readonly IMod _mod;
		private readonly IInstallLog _installLog;
		private readonly TxFileManager _fileManager;
		private readonly ConfirmItemOverwriteDelegate _overwriteConfirmation;
		private readonly HashSet<string> _snapshotted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public DataDrivenBaldursGate3GameSpecificValueInstaller(IMod mod, IInstallLog installLog, TxFileManager fileManager,
			ConfirmItemOverwriteDelegate overwriteConfirmation)
		{
			_mod = mod ?? throw new ArgumentNullException(nameof(mod));
			_installLog = installLog ?? throw new ArgumentNullException(nameof(installLog));
			_fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
			_overwriteConfirmation = overwriteConfirmation;
		}

		public virtual bool EditGameSpecificValue(string key, byte[] value)
		{
			if (!ResolveGameSpecificValueEdit(key))
				return false;
			return ApplyResolvedGameSpecificValueEdit(key, value);
		}

		public virtual bool ResolveGameSpecificValueEdit(string key)
		{
			string profileName;
			string uuid;
			DataDrivenBaldursGate3ModSettingsValue.ParseKey(key, out profileName, out uuid);
			string currentOwner = _installLog.GetCurrentGameSpecificValueEditOwnerKey(key);
			string installingOwner = _installLog.GetModKey(_mod);
			if (String.IsNullOrEmpty(currentOwner) ||
				StringComparer.OrdinalIgnoreCase.Equals(currentOwner, _installLog.OriginalValuesKey) ||
				StringComparer.OrdinalIgnoreCase.Equals(currentOwner, installingOwner))
			{
				return true;
			}

			if (_overwriteConfirmation == null)
				return false;

			string message = String.Format("Baldur's Gate 3 module '{0}' in profile '{1}' is already managed by another mod. Overwrite the managed value?", uuid, profileName);
			OverwriteResult result = _overwriteConfirmation(message, false, false);
			return result == OverwriteResult.Yes || result == OverwriteResult.YesToAll;
		}

		public virtual bool ApplyResolvedGameSpecificValueEdit(string key, byte[] value)
		{
			string profileName;
			string uuid;
			DataDrivenBaldursGate3ModSettingsValue.ParseKey(key, out profileName, out uuid);
			DataDrivenBaldursGate3ModSettingsValue.ValidatePayload(uuid, value);
			string manifestPath = DataDrivenBaldursGate3ModSettingsValue.GetManifestPath(profileName);
			if (!File.Exists(manifestPath))
				throw new InvalidOperationException("The reviewed Baldur's Gate 3 profile manifest no longer exists: " + profileName);

			byte[] current;
			bool exists = DataDrivenBaldursGate3ModSettingsValue.TryReadValue(manifestPath, uuid, out current);
			string currentOwner = _installLog.GetCurrentGameSpecificValueEditOwnerKey(key);
			string installingOwner = _installLog.GetModKey(_mod);
			bool sameOwner = !String.IsNullOrEmpty(currentOwner) && StringComparer.OrdinalIgnoreCase.Equals(currentOwner, installingOwner);
			bool originalOwner = !String.IsNullOrEmpty(currentOwner) && StringComparer.OrdinalIgnoreCase.Equals(currentOwner, _installLog.OriginalValuesKey);

			// A reviewed Collection operation may not silently absorb a physical change that happened after review.
			if (exists && !BytesEqual(current, value) && !sameOwner)
				throw new InvalidOperationException("The reviewed Baldur's Gate 3 module entry changed before native execution.");
			if (!exists && !String.IsNullOrEmpty(currentOwner) && !sameOwner && !originalOwner)
				throw new InvalidOperationException("InstallLog owns a Baldur's Gate 3 module entry that is no longer present in the profile manifest.");

			if (String.IsNullOrEmpty(currentOwner))
				_installLog.LogOriginalGameSpecificValue(key, exists ? current : null);

			if (!exists || !BytesEqual(current, value))
			{
				Snapshot(manifestPath);
				DataDrivenBaldursGate3ModSettingsValue.WriteValue(manifestPath, uuid, value);
			}

			if (sameOwner)
				_installLog.ReplaceGameSpecificValueEdit(_mod, key, value);
			else
				_installLog.AddGameSpecificValueEdit(_mod, key, value);
			return true;
		}

		public void UnEditGameSpecificValue(string key)
		{
			string profileName;
			string uuid;
			DataDrivenBaldursGate3ModSettingsValue.ParseKey(key, out profileName, out uuid);
			string installingOwner = _installLog.GetModKey(_mod);
			string currentOwner = _installLog.GetCurrentGameSpecificValueEditOwnerKey(key);
			if (String.IsNullOrEmpty(installingOwner) || !StringComparer.OrdinalIgnoreCase.Equals(installingOwner, currentOwner))
				return;

			string manifestPath = DataDrivenBaldursGate3ModSettingsValue.GetManifestPath(profileName);
			if (File.Exists(manifestPath))
			{
				Snapshot(manifestPath);
				byte[] previous = _installLog.GetPreviousGameSpecificValue(key);
				if (previous == null)
					DataDrivenBaldursGate3ModSettingsValue.RemoveValue(manifestPath, uuid);
				else
					DataDrivenBaldursGate3ModSettingsValue.WriteValue(manifestPath, uuid, previous);
			}

			_installLog.RemoveGameSpecificValueEdit(_mod, key);
		}

		public bool TryReadGameSpecificValue(string key, out byte[] value)
		{
			value = null;
			try
			{
				string profileName;
				string uuid;
				DataDrivenBaldursGate3ModSettingsValue.ParseKey(key, out profileName, out uuid);
				string manifestPath = DataDrivenBaldursGate3ModSettingsValue.GetManifestPath(profileName);
				return File.Exists(manifestPath) && DataDrivenBaldursGate3ModSettingsValue.TryReadValue(manifestPath, uuid, out value);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is IOException ||
				ex is UnauthorizedAccessException || ex is XmlException)
			{
				return false;
			}
		}

		public bool TryRestoreGameSpecificValue(string key, byte[] value)
		{
			try
			{
				string profileName;
				string uuid;
				DataDrivenBaldursGate3ModSettingsValue.ParseKey(key, out profileName, out uuid);
				string manifestPath = DataDrivenBaldursGate3ModSettingsValue.GetManifestPath(profileName);
				if (!File.Exists(manifestPath))
					return false;
				Snapshot(manifestPath);
				if (value == null)
					DataDrivenBaldursGate3ModSettingsValue.RemoveValue(manifestPath, uuid);
				else
					DataDrivenBaldursGate3ModSettingsValue.WriteValue(manifestPath, uuid, value);
				return true;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is IOException ||
				ex is UnauthorizedAccessException || ex is XmlException)
			{
				return false;
			}
		}

		public virtual void FinalizeInstall()
		{
		}

		protected IInstallLog InstallLog { get { return _installLog; } }
		protected IMod Mod { get { return _mod; } }

		private void Snapshot(string manifestPath)
		{
			if (_snapshotted.Add(manifestPath))
				_fileManager.Snapshot(manifestPath);
		}

		private static bool BytesEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int index = 0; index < left.Length; index++)
				if (left[index] != right[index]) return false;
			return true;
		}
	}

	/// <summary>Upgrade variant that removes previously owned BG3 manifest entries omitted by the new reviewed recipe.</summary>
	public sealed class DataDrivenBaldursGate3GameSpecificValueUpgradeInstaller : DataDrivenBaldursGate3GameSpecificValueInstaller
	{
		private readonly HashSet<string> _originallyInstalledEdits;

		public DataDrivenBaldursGate3GameSpecificValueUpgradeInstaller(IMod mod, IInstallLog installLog, TxFileManager fileManager,
			ConfirmItemOverwriteDelegate overwriteConfirmation)
			: base(mod, installLog, fileManager, overwriteConfirmation)
		{
			_originallyInstalledEdits = new HashSet<string>(installLog.GetInstalledGameSpecificValueEdits(mod), StringComparer.Ordinal);
		}

		public override bool ResolveGameSpecificValueEdit(string key)
		{
			if (WasPreviouslyInstalledByThisMod(key))
				return true;
			return base.ResolveGameSpecificValueEdit(key);
		}

		public override bool ApplyResolvedGameSpecificValueEdit(string key, byte[] value)
		{
			if (WasPreviouslyInstalledByThisMod(key))
			{
				string installingOwner = InstallLog.GetModKey(Mod);
				string currentOwner = InstallLog.GetCurrentGameSpecificValueEditOwnerKey(key);
				if (!StringComparer.OrdinalIgnoreCase.Equals(installingOwner, currentOwner))
				{
					DataDrivenBaldursGate3ModSettingsValue.ValidatePayloadFromKey(key, value);
					InstallLog.ReplaceGameSpecificValueEdit(Mod, key, value);
					_originallyInstalledEdits.Remove(key);
					return true;
				}
			}

			bool result = base.ApplyResolvedGameSpecificValueEdit(key, value);
			if (result)
				_originallyInstalledEdits.Remove(key);
			return result;
		}

		private bool WasPreviouslyInstalledByThisMod(string key)
		{
			string installingOwner = InstallLog.GetModKey(Mod);
			if (String.IsNullOrEmpty(installingOwner))
				return false;
			return InstallLog.GetGameSpecificValueEditInstallers(key)
				.Any(installer => installer != null && StringComparer.OrdinalIgnoreCase.Equals(InstallLog.GetModKey(installer), installingOwner));
		}

		public override void FinalizeInstall()
		{
			string installingOwner = InstallLog.GetModKey(Mod);
			foreach (string key in _originallyInstalledEdits.Where(DataDrivenBaldursGate3ModSettingsValue.IsSupportedKey).ToList())
			{
				string currentOwner = InstallLog.GetCurrentGameSpecificValueEditOwnerKey(key);
				if (!String.IsNullOrEmpty(installingOwner) && StringComparer.OrdinalIgnoreCase.Equals(installingOwner, currentOwner))
					UnEditGameSpecificValue(key);
				else
					InstallLog.RemoveGameSpecificValueEdit(Mod, key);
			}
		}
	}

	/// <summary>Deterministic key/payload codec for one BG3 profile ModuleShortDesc entry.</summary>
	internal static class DataDrivenBaldursGate3ModSettingsValue
	{
		private const string KeyPrefix = "bg3-modsettings-v1|";
		private const int MaxPayloadBytes = 256 * 1024;
		private const int MaxModules = 2048;
		private const int MaxProfiles = 256;

		internal static string ProfilesDirectory
		{
			get
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					@"Larian Studios\Baldur's Gate 3\PlayerProfiles");
			}
		}

		internal static IReadOnlyList<BasicInstallGameSpecificValue> BuildPlanFromInfoBytes(byte[] infoBytes, string profilesDirectory)
		{
			if (infoBytes == null || infoBytes.Length == 0)
				throw new InvalidDataException("Baldur's Gate 3 info.json is empty.");
			if (String.IsNullOrWhiteSpace(profilesDirectory))
				throw new ArgumentException("Baldur's Gate 3 profile directory is required.", nameof(profilesDirectory));

			Bg3InfoJson info;
			try
			{
				info = JsonConvert.DeserializeObject<Bg3InfoJson>(Encoding.ASCII.GetString(infoBytes));
			}
			catch (JsonException ex)
			{
				throw new InvalidDataException("Baldur's Gate 3 info.json could not be parsed for deterministic special-install planning.", ex);
			}
			if (info == null)
				throw new InvalidDataException("Baldur's Gate 3 info.json does not contain a mod descriptor.");

			try
			{
				return BuildPlan(info.Mods, profilesDirectory);
			}
			catch (XmlException ex)
			{
				throw new InvalidDataException("A Baldur's Gate 3 profile manifest could not be parsed for deterministic special-install planning.", ex);
			}
		}

		internal static string CreateKey(string profileName, string uuid)
		{
			ValidateProfileName(profileName);
			return KeyPrefix + profileName + "|" + NormalizeUuid(uuid);
		}

		internal static void ParseKey(string key, out string profileName, out string uuid)
		{
			if (String.IsNullOrWhiteSpace(key) || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
				throw new ArgumentException("Unsupported Baldur's Gate 3 game-specific value key.", nameof(key));
			string suffix = key.Substring(KeyPrefix.Length);
			int separator = suffix.IndexOf('|');
			if (separator <= 0 || separator == suffix.Length - 1 || suffix.IndexOf('|', separator + 1) >= 0)
				throw new ArgumentException("Malformed Baldur's Gate 3 game-specific value key.", nameof(key));
			profileName = suffix.Substring(0, separator);
			uuid = suffix.Substring(separator + 1);
			ValidateProfileName(profileName);
			uuid = NormalizeUuid(uuid);
		}

		internal static bool IsSupportedKey(string key)
		{
			try
			{
				string profileName;
				string uuid;
				ParseKey(key, out profileName, out uuid);
				return true;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException)
			{
				return false;
			}
		}

		internal static string GetManifestPath(string profileName)
		{
			return GetManifestPath(ProfilesDirectory, profileName);
		}

		internal static string GetManifestPath(string profilesDirectory, string profileName)
		{
			if (String.IsNullOrWhiteSpace(profilesDirectory))
				throw new ArgumentException("Baldur's Gate 3 profile directory is required.", nameof(profilesDirectory));
			ValidateProfileName(profileName);
			return Path.Combine(profilesDirectory, profileName, "modsettings.lsx");
		}

		private static IReadOnlyList<BasicInstallGameSpecificValue> BuildPlan(IEnumerable<Bg3Module> modules, string profilesDirectory)
		{
			var result = new List<BasicInstallGameSpecificValue>();
			if (modules == null || !Directory.Exists(profilesDirectory))
				return result.AsReadOnly();

			List<Bg3Module> sourceModules = modules.Where(x => x != null).ToList();
			if (sourceModules.Count > MaxModules)
				throw new InvalidDataException("The Baldur's Gate 3 special-install metadata contains too many module entries.");

			DirectoryInfo[] profiles = new DirectoryInfo(profilesDirectory).GetDirectories()
				.Where(x => !x.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
				.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.Name, StringComparer.Ordinal)
				.ToArray();
			if (profiles.Length > MaxProfiles)
				throw new InvalidDataException("The Baldur's Gate 3 profile set is too large for bounded deterministic special-install planning.");

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (DirectoryInfo profile in profiles)
			{
				string manifestPath = Path.Combine(profile.FullName, "modsettings.lsx");
				if (!File.Exists(manifestPath))
					continue;

				foreach (Bg3Module module in sourceModules)
				{
					string key = CreateKey(profile.Name, module.UUID);
					if (!seen.Add(key))
						continue;
					byte[] existing;
					byte[] desired = TryReadValue(manifestPath, module.UUID, out existing) ? existing : CreatePayload(module);
					result.Add(new BasicInstallGameSpecificValue(key, desired));
				}
			}
			return result.AsReadOnly();
		}

		internal static byte[] CreatePayload(string uuid, string folder, string name, string version)
		{
			return CreatePayload(new Bg3Module { UUID = uuid, Folder = folder, Name = name, Version = version });
		}

		private static byte[] CreatePayload(Bg3Module module)
		{
			if (module == null) throw new ArgumentNullException(nameof(module));
			ValidateUuid(module.UUID);
			var document = new XmlDocument();
			XmlElement node = document.CreateElement("node");
			node.SetAttribute("id", "ModuleShortDesc");
			AppendAttribute(document, node, "Folder", "LSString", module.Folder ?? module.folderName);
			AppendAttribute(document, node, "MD5", "LSString", String.Empty);
			AppendAttribute(document, node, "Name", "LSString", module.Name ?? module.modName);
			AppendAttribute(document, node, "UUID", "FixedString", module.UUID);
			AppendAttribute(document, node, "Version64", "int64", module.Version ?? "1");
			document.AppendChild(node);
			return EncodeNode(node);
		}

		internal static void ValidatePayloadFromKey(string key, byte[] value)
		{
			string profileName;
			string uuid;
			ParseKey(key, out profileName, out uuid);
			ValidatePayload(uuid, value);
		}

		internal static void ValidatePayload(string uuid, byte[] value)
		{
			if (value == null || value.Length == 0 || value.Length > MaxPayloadBytes)
				throw new InvalidDataException("Baldur's Gate 3 module payload length is invalid.");
			XmlElement node = ParsePayload(value);
			string payloadUuid = ReadModuleUuid(node);
			if (!SameUuid(uuid, payloadUuid))
				throw new InvalidDataException("Baldur's Gate 3 module payload UUID does not match its native key.");
		}

		internal static bool TryReadValue(string manifestPath, string uuid, out byte[] value)
		{
			value = null;
			XmlDocument document = LoadManifest(manifestPath);
			XmlElement module = FindModule(GetChildren(document), uuid);
			if (module == null)
				return false;
			value = EncodeNode(module);
			return true;
		}

		internal static void WriteValue(string manifestPath, string uuid, byte[] value)
		{
			ValidatePayload(uuid, value);
			XmlDocument document = LoadManifest(manifestPath);
			XmlNode children = GetChildren(document);
			XmlElement existing = FindModule(children, uuid);
			XmlElement payload = ParsePayload(value);
			XmlNode imported = document.ImportNode(payload, true);
			if (existing == null)
				children.AppendChild(imported);
			else
				children.ReplaceChild(imported, existing);
			SaveManifest(document, manifestPath);
		}

		internal static void RemoveValue(string manifestPath, string uuid)
		{
			XmlDocument document = LoadManifest(manifestPath);
			XmlNode children = GetChildren(document);
			XmlElement existing = FindModule(children, uuid);
			if (existing == null)
				return;
			children.RemoveChild(existing);
			SaveManifest(document, manifestPath);
		}

		private static XmlDocument LoadManifest(string manifestPath)
		{
			if (String.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
				throw new FileNotFoundException("Baldur's Gate 3 modsettings.lsx is unavailable.", manifestPath);
			var document = new XmlDocument();
			document.Load(manifestPath);
			GetChildren(document);
			return document;
		}

		private static XmlNode GetChildren(XmlDocument document)
		{
			XmlNode mods = document == null || document.DocumentElement == null
				? null
				: document.DocumentElement.SelectSingleNode("//*[@id='Mods']");
			XmlNode children = mods == null ? null : mods.ChildNodes.Cast<XmlNode>().FirstOrDefault(x => x.NodeType == XmlNodeType.Element);
			if (children == null)
				throw new InvalidDataException("Baldur's Gate 3 modsettings.lsx does not contain the expected Mods children node.");
			return children;
		}

		private static XmlElement FindModule(XmlNode children, string uuid)
		{
			ValidateUuid(uuid);
			foreach (XmlNode child in children.ChildNodes)
			{
				XmlElement module = child as XmlElement;
				if (module == null || !StringComparer.Ordinal.Equals(module.GetAttribute("id"), "ModuleShortDesc"))
					continue;
				string currentUuid = ReadModuleUuid(module);
				if (SameUuid(currentUuid, uuid))
					return module;
			}
			return null;
		}

		private static string ReadModuleUuid(XmlElement module)
		{
			if (module == null) return null;
			foreach (XmlNode child in module.ChildNodes)
			{
				XmlElement attribute = child as XmlElement;
				if (attribute != null && StringComparer.Ordinal.Equals(attribute.Name, "attribute") &&
					StringComparer.Ordinal.Equals(attribute.GetAttribute("id"), "UUID"))
				{
					return attribute.GetAttribute("value");
				}
			}
			return null;
		}

		private static XmlElement ParsePayload(byte[] value)
		{
			if (value == null || value.Length == 0 || value.Length > MaxPayloadBytes)
				throw new InvalidDataException("Baldur's Gate 3 module payload length is invalid.");
			var document = new XmlDocument();
			document.LoadXml(Encoding.UTF8.GetString(value));
			XmlElement node = document.DocumentElement;
			if (node == null || !StringComparer.Ordinal.Equals(node.Name, "node") ||
				!StringComparer.Ordinal.Equals(node.GetAttribute("id"), "ModuleShortDesc") || String.IsNullOrWhiteSpace(ReadModuleUuid(node)))
			{
				throw new InvalidDataException("Baldur's Gate 3 module payload is not a ModuleShortDesc node.");
			}
			return node;
		}

		private static byte[] EncodeNode(XmlElement node)
		{
			byte[] value = Encoding.UTF8.GetBytes(node.OuterXml);
			if (value.Length == 0 || value.Length > MaxPayloadBytes)
				throw new InvalidDataException("Baldur's Gate 3 module payload exceeds the supported deterministic size.");
			return value;
		}

		private static void AppendAttribute(XmlDocument document, XmlElement parent, string id, string type, string value)
		{
			XmlElement attribute = document.CreateElement("attribute");
			attribute.SetAttribute("id", id);
			attribute.SetAttribute("type", type);
			attribute.SetAttribute("value", value ?? String.Empty);
			parent.AppendChild(attribute);
		}

		private static void SaveManifest(XmlDocument document, string manifestPath)
		{
			var settings = new XmlWriterSettings { Indent = true, IndentChars = "\t" };
			using (XmlWriter writer = XmlWriter.Create(manifestPath, settings))
				document.Save(writer);
		}

		private static void ValidateProfileName(string profileName)
		{
			if (String.IsNullOrWhiteSpace(profileName) || profileName == "." || profileName == ".." || profileName.IndexOf('|') >= 0 ||
				profileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
				profileName.IndexOf(Path.DirectorySeparatorChar) >= 0 || profileName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
			{
				throw new ArgumentException("Invalid Baldur's Gate 3 profile name.", nameof(profileName));
			}
		}

		private static bool SameUuid(string left, string right)
		{
			Guid leftGuid;
			Guid rightGuid;
			return Guid.TryParse(left, out leftGuid) && Guid.TryParse(right, out rightGuid) && leftGuid.Equals(rightGuid);
		}

		private static void ValidateUuid(string uuid)
		{
			NormalizeUuid(uuid);
		}

		private static string NormalizeUuid(string uuid)
		{
			Guid parsed;
			if (String.IsNullOrWhiteSpace(uuid) || !Guid.TryParse(uuid, out parsed))
				throw new InvalidDataException("Baldur's Gate 3 module metadata contains an invalid UUID.");
			return parsed.ToString("D").ToLowerInvariant();
		}

		private sealed class Bg3InfoJson
		{
			public Bg3Module[] Mods;
		}

		private sealed class Bg3Module
		{
			public string Name;
			public string modName;
			public string Folder;
			public string folderName;
			public string Version;
			public string UUID;
		}
	}
}
