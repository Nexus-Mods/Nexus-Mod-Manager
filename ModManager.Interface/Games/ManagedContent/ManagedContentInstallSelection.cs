using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.Games.ManagedContent
{
	/// <summary>
	/// Captures the bounded archive selection and naming inputs shared by configured game modes.
	/// This is an operation-local read model; native installers retain ownership and deployment.
	/// </summary>
	public sealed class ManagedContentInstallSelection
	{
		/// <summary>Freezes the actual selected source/destination pairs and package naming context.</summary>
		private ManagedContentInstallSelection(List<KeyValuePair<string, string>> files, string packageIdentity,
			string suggestedName, ModInstallContext installContext)
		{
			Files = new ReadOnlyCollection<KeyValuePair<string, string>>(files);
			PackageIdentity = packageIdentity;
			SuggestedName = suggestedName;
			InstallContext = installContext;
		}

		public IReadOnlyList<KeyValuePair<string, string>> Files { get; }
		public string PackageIdentity { get; }
		public string SuggestedName { get; }
		public ModInstallContext InstallContext { get; }

		/// <summary>Validates inventory membership once and captures naming without changing selected destinations.</summary>
		public static ManagedContentInstallSelection Create(IMod mod, ModInstallContext installContext,
			IEnumerable<KeyValuePair<string, string>> selectedFiles, string identityScope)
		{
			if (mod == null) throw new ArgumentNullException(nameof(mod));
			if (installContext == null) throw new ArgumentNullException(nameof(installContext));
			if (selectedFiles == null) throw new ArgumentNullException(nameof(selectedFiles));
			if (String.IsNullOrWhiteSpace(identityScope)) throw new ArgumentException("A stable game identity scope is required.", nameof(identityScope));
			if (installContext.InstallRoot != ModInstallRoot.Data)
				throw new InvalidDataException("Managed-content basic mapping supports only the ordinary Data install root.");
			if (mod.HasInstallScript)
				throw new InvalidDataException("Managed-content basic mapping requires a non-scripted archive; selected scripted effects need their own review.");

			List<string> inventory = mod.GetFileList();
			if (inventory == null || inventory.Count > ManagedContentLayoutPlanner.MaximumFiles)
				throw new InvalidDataException("The bounded archive inventory is unavailable or too large.");
			var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string path in inventory)
			{
				string[] parts;
				if (!ManagedContentPath.TryParse(path, out parts)) throw new InvalidDataException("Unsafe archive inventory path: " + path);
				if (!allowed.Add(String.Join("\\", parts))) throw new InvalidDataException("Duplicate archive inventory path: " + path);
			}

			var requested = new List<KeyValuePair<string, string>>();
			foreach (KeyValuePair<string, string> file in selectedFiles)
			{
				if (requested.Count == ManagedContentLayoutPlanner.MaximumFiles || String.IsNullOrWhiteSpace(file.Key) ||
					!allowed.Contains(file.Key.Replace('/', '\\')))
					throw new InvalidDataException("The selected source is outside the bounded archive inventory: " + file.Key);
				requested.Add(file);
			}
			string filename = Path.GetFileName(mod.Filename ?? String.Empty);
			if (String.IsNullOrWhiteSpace(filename)) throw new InvalidDataException("A stable local archive filename is required.");
			string identity = identityScope + "|" + (mod.DownloadId ?? String.Empty) + "|" + filename;
			string suggestedName = mod.ModName;
			string name = String.IsNullOrWhiteSpace(suggestedName) ? Path.GetFileNameWithoutExtension(filename) : suggestedName;
			return new ManagedContentInstallSelection(requested, identity, name, installContext);
		}

		/// <summary>Validates the complete configured plan and every canonical projection before returning native mappings.</summary>
		public IReadOnlyList<KeyValuePair<string, string>> Map(ManagedContentLayoutPlanner layout)
		{
			if (layout == null) throw new ArgumentNullException(nameof(layout));
			ManagedContentLayoutPlan plan = layout.Plan(Files, PackageIdentity, SuggestedName, InstallContext);
			if (!plan.IsSupported)
				throw new InvalidDataException("Managed-content layout needs review: " + String.Join("; ", plan.Issues.Take(4)));
			var mapped = new List<KeyValuePair<string, string>>(plan.Files.Count);
			foreach (ManagedContentLayoutFile file in plan.Files)
			{
				string canonical = layout.ValidatePlannedPath(file.DestinationPath);
				if (!String.Equals(canonical, file.DestinationPath, StringComparison.Ordinal))
					throw new InvalidDataException("The configured layout changes a reviewed native destination during path projection: " + file.DestinationPath);
				mapped.Add(new KeyValuePair<string, string>(file.SourcePath, file.DestinationPath));
			}
			return new ReadOnlyCollection<KeyValuePair<string, string>>(mapped);
		}
	}
}
