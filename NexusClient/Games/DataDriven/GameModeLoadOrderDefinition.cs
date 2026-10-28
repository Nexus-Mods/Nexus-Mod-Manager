using System;
using System.Linq;
using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>Versioned opt-in package inventory configuration; does not enable native order editing.</summary>
	public sealed class GameModeLoadOrderDefinition
	{
		public int SchemaVersion { get; set; }
		public bool? Enabled { get; set; }
		public string Provider { get; set; }
		public int Revision { get; set; }
		public string PackageRoot { get; set; }
		public string ProfileSource { get; set; }
		public GameModeLoadOrderDirectoryDefinition DirectoryPolicy { get; set; }
	}

	/// <summary>Declares package-folder hygiene independently of installation or cleanup behavior.</summary>
	public sealed class GameModeLoadOrderDirectoryDefinition
	{
		public string[] AllowedExtensions { get; set; }
		public bool? AllowSubdirectories { get; set; }
	}

	/// <summary>Resolves the same immutable inventory capability during validation and mode construction.</summary>
	public static class DataDrivenLoadOrder
	{
		/// <summary>Rejects unsupported versions and incompatible native records without creating or scanning game folders.</summary>
		public static LoadOrderCapability Create(GameModeDefinition definition)
		{
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			GameModeLoadOrderDefinition source = definition.LoadOrder;
			if (source == null) return null;
			if (source.SchemaVersion != 1 || source.Provider != LoadOrderCapability.LarianProviderId || source.Revision != 1)
				throw new ArgumentException("loadOrder requires schemaVersion 1, provider larian-module-settings-v1 and revision 1.");
			if (!source.Enabled.HasValue || source.PackageRoot != "ManagedInstallation" || source.ProfileSource != "structuredEffects")
				throw new ArgumentException("loadOrder requires explicit enabled, packageRoot ManagedInstallation and profileSource structuredEffects.");
			if (source.DirectoryPolicy?.AllowSubdirectories != false)
				throw new ArgumentException("loadOrder.directoryPolicy must explicitly disallow subdirectories for this provider.");
			var policy = new LoadOrderDirectoryPolicy(source.DirectoryPolicy.AllowedExtensions, false);
			if (policy.AllowedExtensions.Count != 1 || policy.AllowedExtensions[0] != ".pak")
				throw new ArgumentException("The Larian inventory provider requires exactly the .pak extension.");
			if (!source.Enabled.Value) return null;
			StructuredXmlRecordRule profiles = DataDrivenStructuredEffects.Create(definition);
			if (profiles == null || profiles.ContainerId != "Mods" || profiles.RecordId != "ModuleShortDesc" || profiles.IdentityField != "UUID" ||
				!new[] { "Folder", "Name", "Version64" }.All(id => profiles.Fields.Any(field => field.Id == id)))
				throw new ArgumentException("loadOrder requires compatible structuredEffects module records with Folder, Name, UUID and Version64 fields.");
			if (definition.ManagedContent?.ComponentOrdering != null)
				throw new ArgumentException("loadOrder cannot share a mode with managedContent.componentOrdering.");
			return new LoadOrderCapability(source.Provider, source.Revision, policy, profiles);
		}
	}
}
