using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Mods;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Inspects selected archive packages before native mutation, then plans reversible per-module owner values.</summary>
	internal sealed class LarianPackageInstallPlanner
	{
		private const int MaximumPackages = 1024;
		private const long MaximumPackageBytes = 32L * 1024 * 1024 * 1024;
		private const long MaximumBatchBytes = 64L * 1024 * 1024 * 1024;
		private readonly LoadOrderCapability _capability;
		private readonly ILoadOrderPackageInspector _inspector;
		private readonly StructuredXmlRecordStore _records;

		/// <summary>Allows isolated inspection/profile fixtures without invoking a real helper or installed game.</summary>
		internal LarianPackageInstallPlanner(LoadOrderCapability capability, ILoadOrderPackageInspector inspector, StructuredXmlRecordStore records)
		{
			_capability = capability ?? throw new ArgumentNullException(nameof(capability));
			_inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
			_records = records ?? throw new ArgumentNullException(nameof(records));
			if (capability.Provider != LoadOrderCapability.LarianProviderId || capability.Revision != 1)
				throw new NotSupportedException("The package registration provider is not supported.");
		}

		/// <summary>Uses the same application-owned helper and configured profile root as load-order inspection.</summary>
		internal static LarianPackageInstallPlanner Create(LoadOrderCapability capability) => new LarianPackageInstallPlanner(capability,
			new LarianPackageInspectorClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "LoadOrder", "LarianPackageInspector.exe")),
			new StructuredXmlRecordStore(capability.Profiles));

		/// <summary>Never registers omitted, secondary-root, override, campaign or incomplete package observations.</summary>
		internal IReadOnlyList<BasicInstallGameSpecificValue> Build(IMod mod, ModInstallContext context, IReadOnlyList<BasicInstallPlanFile> selectedFiles, CancellationToken token)
		{
			if (mod == null || context == null || selectedFiles == null) throw new ArgumentNullException(mod == null ? nameof(mod) : context == null ? nameof(context) : nameof(selectedFiles));
			token.ThrowIfCancellationRequested();
			BasicInstallPlanFile[] packages = selectedFiles.Where(file => file.DeploymentTarget.Root == ModDeploymentRoot.Data &&
				String.Equals(Path.GetExtension(file.DeploymentTarget.RelativePath), ".pak", StringComparison.OrdinalIgnoreCase)).Take(MaximumPackages + 1).ToArray();
			if (packages.Length == 0) return new BasicInstallGameSpecificValue[0];
			if (packages.Length > MaximumPackages) throw new InvalidDataException("The selected package set exceeds the automatic registration limit.");
			if (packages.Any(file => file.DeploymentTarget.RelativePath.IndexOfAny(new[] { '\\', '/' }) >= 0) ||
				packages.Select(file => file.DeploymentTarget.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != packages.Length)
				throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.PackageLayout", "Selected packages must have unique filenames directly in the package folder. Correct the installer selection before installing."));
			if (!_inspector.IsAvailable) throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.InspectorMissing",
				"Automatic module registration needs NMM's package inspection helper. Install the complete NMM distribution, then retry this mod."));

			string staging = Path.Combine(Path.GetTempPath(), "NMM-PackageRegistration-" + Guid.NewGuid().ToString("N"));
			var createdFiles = new List<string>();
			Directory.CreateDirectory(staging);
			try
			{
				long total = 0;
				foreach (BasicInstallPlanFile package in packages)
				{
					token.ThrowIfCancellationRequested();
					string name = package.DeploymentTarget.RelativePath;
					if (Path.GetFileName(name) != name || name == "." || name == ".." || name.IndexOf(':') >= 0)
						throw new InvalidDataException("Unsafe selected package filename.");
					string destination = Path.Combine(staging, name);
					using (FileStream input = mod.GetFileStream(package.SourcePath))
					{
						if (input == null || input.Length == 0 || input.Length > MaximumPackageBytes || total > MaximumBatchBytes - input.Length)
							throw new InvalidDataException("Selected packages exceed the bounded inspection staging size.");
						long length = input.Length; input.Position = 0;
						using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
						{
							createdFiles.Add(destination); CopyPackage(input, output, length, token);
						}
						total += length;
					}
				}
				LoadOrderInventory inventory = new PackageLoadOrderInventoryProvider(_capability, () => staging, _inspector).ReadInventory(token);
				if (!inventory.IsComplete || inventory.Diagnostics.Any(issue => issue.Severity == LoadOrderDiagnosticSeverity.Error))
					throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.InspectionBlocked",
						"Selected packages could not be identified completely and uniquely. No modules were registered. Resolve the package inspection problem before retrying.") + Environment.NewLine +
						String.Join(Environment.NewLine, inventory.Diagnostics.Select(issue => Path.GetFileName(issue.Path) + ": " + issue.Kind + " " + issue.TechnicalDetails)));
				return BuildValues(inventory, token);
			}
			finally
			{
				// Delete only files created by this invocation. Never recursively remove a computed temporary path.
				foreach (string file in createdFiles)
					try { File.Delete(file); }
					catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Trace.TraceWarning("Package inspection cleanup: {0}", error.Message); }
				try { Directory.Delete(staging, false); }
				catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Trace.TraceWarning("Package inspection staging retained at {0}: {1}", staging, error.Message); }
			}
		}

		/// <summary>Produces the established modern record shape and retains existing records/unknown XML through the structured store.</summary>
		private IReadOnlyList<BasicInstallGameSpecificValue> BuildValues(LoadOrderInventory inventory, CancellationToken token)
		{
			var payloads = new List<KeyValuePair<string, byte[]>>();
			foreach (LoadOrderModuleInfo module in inventory.Packages.SelectMany(package => package.Inspection.Modules).Where(module =>
				module.Kind == LoadOrderModuleKind.AddOn && !_capability.BuiltInModuleIdentities.Contains(module.Metadata.Identity, StringComparer.OrdinalIgnoreCase)))
			{
				token.ThrowIfCancellationRequested();
				XmlElement record = NativeLoadOrderDraft.CreateRecord(new XmlDocument { XmlResolver = null }, module.Metadata);
				payloads.Add(new KeyValuePair<string, byte[]>(module.Metadata.Identity, Encoding.UTF8.GetBytes(record.OuterXml)));
			}
			if (payloads.Count == 0) return new BasicInstallGameSpecificValue[0];
			IReadOnlyList<BasicInstallGameSpecificValue> values = _records.BuildPlanFromRecords(payloads);
			var documents = new Dictionary<string, LarianLoadOrderDocument>(StringComparer.OrdinalIgnoreCase);
			var planned = new List<BasicInstallGameSpecificValue>();
			var metadata = inventory.Packages.SelectMany(package => package.Inspection.Modules).ToDictionary(module => module.Metadata.Identity, module => module.Metadata, StringComparer.OrdinalIgnoreCase);
			foreach (BasicInstallGameSpecificValue value in values)
			{
				token.ThrowIfCancellationRequested(); _records.ParseKey(value.Key, out string profile, out string identity);
				if (!documents.TryGetValue(profile, out LarianLoadOrderDocument document)) documents.Add(profile, document = LarianLoadOrderDocument.Read(_records.GetManifestPath(profile)));
				if (document.Legacy && !document.Records.ContainsKey(identity)) throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.LegacyFormat",
					"A game profile uses a legacy module-settings format. Automatic package registration cannot migrate it. Update the profile in the game before retrying."));
				if (document.Entries.Any(entry => entry.IsProtected && entry.Identity == identity)) throw new InvalidDataException("A selected package attempts to register a game-owned module identity.");
				byte[] payload = value.Value;
				if (!document.Legacy && document.Records.TryGetValue(identity, out XmlElement existing))
				{
					string version = LarianLoadOrderDocument.Field(existing, "Version64");
					if (version != null && !UInt64.TryParse(version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong ignored))
					{
						// Correct only this explicitly installed module's invalid sidecar version; keep foreign fields and current position.
						XmlElement corrected = (XmlElement)existing.CloneNode(true);
						XmlElement field = corrected.SelectSingleNode("attribute[@id='Version64']") as XmlElement;
						field.SetAttribute("type", "int64"); field.SetAttribute("value", metadata[identity].Version);
						payload = Encoding.UTF8.GetBytes(corrected.OuterXml);
					}
				}
				planned.Add(new BasicInstallGameSpecificValue(value.Key, payload));
			}
			return planned.AsReadOnly();
		}

		/// <summary>Streams bounded package bytes without allocating an entire PAK or silently accepting a changed stream.</summary>
		private static void CopyPackage(Stream input, Stream output, long expected, CancellationToken token)
		{
			byte[] buffer = new byte[128 * 1024]; long copied = 0; int count;
			while ((count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, expected - copied + 1))) > 0)
			{
				token.ThrowIfCancellationRequested(); copied += count;
				if (copied > expected) throw new InvalidDataException("A selected package grew during inspection staging.");
				output.Write(buffer, 0, count);
			}
			if (copied != expected) throw new InvalidDataException("A selected package changed during inspection staging.");
		}
	}
}
