using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>A detached editable module-inclusion/order draft; changes never write to disk.</summary>
	public sealed class NativeLoadOrderDraft
	{
		private readonly LarianLoadOrderDocument _observed;
		private readonly LoadOrderInventory _inventory;
		private readonly List<string> _included;
		private readonly Dictionary<string, LoadOrderModuleInfo> _available;
		private readonly HashSet<string> _refreshed = new HashSet<string>();

		/// <summary>Imports native order and inspected add-ons while retaining game records as protected entries.</summary>
		public NativeLoadOrderDraft(LarianLoadOrderDocument observed, LoadOrderInventory inventory)
		{
			_observed = observed ?? throw new ArgumentNullException(nameof(observed));
			_inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
			if (!CanEditInventory(inventory))
				throw new InvalidOperationException(LanguageManager.Get("LoadOrder.Draft.UnsafeInventory", "Resolve directory, package-change or duplicate-identity problems before editing. An unreadable package alone does not remove saved entries."));
			_available = inventory.Packages.Where(package => package.Inspection.Status == LoadOrderInspectionStatus.Complete)
				.SelectMany(package => package.Inspection.Modules).Where(module => module.Kind == LoadOrderModuleKind.AddOn)
				.ToDictionary(module => LarianLoadOrderDocument.Identity(module.Metadata.Identity), StringComparer.OrdinalIgnoreCase);
			_included = observed.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToList();
		}
		/// <summary>Allows explicit manual edits around unreadable PAKs while keeping directory, mutation and identity failures blocking.</summary>
		internal static bool CanEditInventory(LoadOrderInventory inventory)
		{
			if (inventory == null) return false;
			if (inventory.Diagnostics.Any(issue => issue.Kind != LoadOrderDiagnosticKind.InspectorUnavailable &&
				issue.Kind != LoadOrderDiagnosticKind.PackageInspectionFailed && issue.Kind != LoadOrderDiagnosticKind.PackageInspectionIncomplete)) return false;
			LoadOrderModuleInfo[] modules = inventory.Packages.SelectMany(package => package.Inspection.Modules).Take(10001).ToArray();
			if (modules.Length > 10000 || modules.GroupBy(module => LarianLoadOrderDocument.Identity(module.Metadata.Identity), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1)) return false;
			return inventory.IsComplete || !String.IsNullOrWhiteSpace(inventory.PackageRoot) &&
				(inventory.Diagnostics.Count > 0 || inventory.Packages.Any(package => package.Inspection.Status != LoadOrderInspectionStatus.Complete));
		}

		/// <summary>Existing native records can be retained or deliberately re-enabled; new identities require a complete package observation.</summary>
		internal bool CanInclude(string identity) => _observed.Records.ContainsKey(identity) || _available.ContainsKey(identity);

		public IReadOnlyList<string> IncludedIdentities => Array.AsReadOnly(_included.ToArray());
		public IReadOnlyList<LoadOrderModuleInfo> AvailableModules => Array.AsReadOnly(_available.Values.ToArray());
		public IReadOnlyList<NativeLoadOrderEntry> ProtectedEntries => Array.AsReadOnly(_observed.Entries.Where(entry => entry.IsProtected).ToArray());

		/// <summary>Moves an included add-on to an explicit zero-based add-on position, leaving campaign records fixed.</summary>
		public void Move(string identity, int position)
		{
			identity = LarianLoadOrderDocument.Identity(identity);
			int index = _included.IndexOf(identity);
			if (index < 0 || position < 0 || position >= _included.Count) throw new ArgumentOutOfRangeException(nameof(position));
			_included.RemoveAt(index); _included.Insert(position, identity);
		}

		/// <summary>Changes module inclusion only; physically installed override files may remain effective in the game.</summary>
		public void SetIncluded(string identity, bool included)
		{
			identity = LarianLoadOrderDocument.Identity(identity);
			if (_observed.Entries.Any(entry => entry.IsProtected && entry.Identity == identity))
				throw new InvalidOperationException("Game-owned module records are protected.");
			if (!included) { _included.Remove(identity); return; }
			if (_included.Contains(identity)) return;
			if (!_observed.Records.ContainsKey(identity) && !_available.ContainsKey(identity)) throw new InvalidOperationException("The module has no native or inspected package metadata.");
			if (_observed.Legacy && !_observed.Records.ContainsKey(identity))
				throw new InvalidOperationException("Adding modern package metadata to a legacy LSX format requires an explicit format migration.");
			_included.Add(identity);
		}

		/// <summary>Refreshes verified modern module metadata after a package update, preserving unknown native fields.</summary>
		public void RefreshMetadata(string identity)
		{
			identity = LarianLoadOrderDocument.Identity(identity);
			if (_observed.Legacy || !_included.Contains(identity) || !_available.ContainsKey(identity))
				throw new InvalidOperationException("Metadata refresh requires an included modern module with inspected package metadata.");
			_refreshed.Add(identity);
		}

		/// <summary>Builds immutable before/after images, retaining existing module fields and unrelated XML.</summary>
		public NativeLoadOrderPreview Prepare()
		{
			XmlDocument document = LarianLoadOrderDocument.Parse(_observed.Bytes);
			XmlElement children = LarianLoadOrderDocument.Children(LarianLoadOrderDocument.Container(document, "Mods", true));
			foreach (XmlElement node in children.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "node" && node.GetAttribute("id") == "ModuleShortDesc").ToArray()) children.RemoveChild(node);
			foreach (NativeLoadOrderEntry entry in ProtectedEntries) children.AppendChild(document.ImportNode(_observed.Records[entry.Identity], true));
			foreach (string id in _included)
			{
				XmlElement existing;
				XmlElement record = _observed.Records.TryGetValue(id, out existing) ? (XmlElement)document.ImportNode(existing, true) : CreateRecord(document, _available[id].Metadata);
				if (_refreshed.Contains(id))
				{
					LoadOrderModuleMetadata metadata = _available[id].Metadata;
					SetField(document, record, "Folder", "LSString", metadata.Folder); SetField(document, record, "Name", "LSString", metadata.Name);
					SetField(document, record, "Version64", "int64", metadata.Version);
					if (metadata.Md5 != null) SetField(document, record, "MD5", "LSString", metadata.Md5);
					if (metadata.PublishHandle != null) SetField(document, record, "PublishHandle", "uint64", metadata.PublishHandle);
				}
				children.AppendChild(record);
			}
			XmlElement order = LarianLoadOrderDocument.Container(document, "ModOrder", false);
			if (order != null)
			{
				XmlElement ordered = LarianLoadOrderDocument.Children(order);
				var original = new Dictionary<string, XmlElement>(StringComparer.OrdinalIgnoreCase);
				foreach (XmlElement node in ordered.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "node" && node.GetAttribute("id") == "Module").ToArray())
				{
					string id = LarianLoadOrderDocument.Identity(LarianLoadOrderDocument.Field(node, "UUID"));
					if (!_observed.Records.ContainsKey(id)) throw new InvalidDataException("The retained ModOrder contains an unresolved record; it cannot be silently discarded.");
					if (original.ContainsKey(id)) throw new InvalidDataException("The retained ModOrder has duplicate UUIDs.");
					original.Add(id, node); ordered.RemoveChild(node);
				}
				foreach (string id in ProtectedEntries.Select(entry => entry.Identity).Where(id => !id.StartsWith("game:", StringComparison.Ordinal)).Concat(_included))
				{
					XmlElement node;
					if (!original.TryGetValue(id, out node))
					{
						node = document.CreateElement("node"); node.SetAttribute("id", "Module"); AddField(document, node, "UUID", "FixedString", id);
					}
					ordered.AppendChild(node);
				}
			}
			byte[] proposed = _refreshed.Count == 0 && _observed.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).SequenceEqual(_included)
				? _observed.Bytes : LarianLoadOrderDocument.Serialize(document);
			// Reparse the proposed format before any caller can request publication.
			return new NativeLoadOrderPreview(_observed, new LarianLoadOrderDocument(_observed.FilePath, proposed), InventoryIdentity(_inventory),
				_observed.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).Union(_available.Keys).ToArray());
		}

		/// <summary>Identifies all metadata used by review, independently of filesystem timestamp hints.</summary>
		internal static string InventoryIdentity(LoadOrderInventory inventory) => LarianLoadOrderDocument.Hash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(inventory)));

		/// <summary>Creates only the established modern ModuleShortDesc payload from inspected metadata.</summary>
		internal static XmlElement CreateRecord(XmlDocument document, LoadOrderModuleMetadata metadata)
		{
			XmlElement node = document.CreateElement("node"); node.SetAttribute("id", "ModuleShortDesc");
			AddField(document, node, "Folder", "LSString", metadata.Folder);
			AddField(document, node, "MD5", "LSString", metadata.Md5 ?? String.Empty);
			AddField(document, node, "Name", "LSString", metadata.Name);
			AddField(document, node, "PublishHandle", "uint64", metadata.PublishHandle ?? "0");
			AddField(document, node, "UUID", "guid", metadata.Identity);
			AddField(document, node, "Version64", "int64", metadata.Version);
			return node;
		}

		/// <summary>Escapes literal XML values without altering numeric strings.</summary>
		private static void SetField(XmlDocument document, XmlElement node, string id, string type, string value)
		{
			LarianLoadOrderDocument.Field(node, id); // Reject ambiguous existing fields before changing one.
			XmlElement field = node.SelectSingleNode("attribute[@id='" + id + "']") as XmlElement;
			if (field == null) AddField(document, node, id, type, value); else field.SetAttribute("value", value);
		}

		/// <summary>Escapes literal XML values without altering numeric strings.</summary>
		private static void AddField(XmlDocument document, XmlElement node, string id, string type, string value)
		{
			XmlElement field = document.CreateElement("attribute"); field.SetAttribute("id", id); field.SetAttribute("type", type); field.SetAttribute("value", value); node.AppendChild(field);
		}
	}

	/// <summary>An immutable exact-file review; clients cannot replace its proposed bytes or inventory identity.</summary>
	public sealed class NativeLoadOrderPreview
	{
		/// <summary>Captures before/after documents and the inspected package set used by the draft.</summary>
		internal NativeLoadOrderPreview(LarianLoadOrderDocument before, LarianLoadOrderDocument after, string inventoryIdentity, string[] knownIdentities)
		{
			Before = before; After = after; InventoryIdentity = inventoryIdentity; KnownIdentities = knownIdentities;
		}
		public LarianLoadOrderDocument Before { get; }
		public LarianLoadOrderDocument After { get; }
		public bool IsChanged => Before.Sha256 != After.Sha256;
		internal string InventoryIdentity { get; }
		internal string[] KnownIdentities { get; }
	}
}
