using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Optional native load-order capability; mutations still require an explicitly accepted review.</summary>
	public interface ILoadOrderCapabilityProvider
	{
		LoadOrderCapability LoadOrder { get; }
	}

	/// <summary>Immutable package-directory rules, independent of the game's package format.</summary>
	public sealed class LoadOrderDirectoryPolicy
	{
		/// <summary>Freezes a bounded, case-insensitive extension allowlist without changing deployment rules.</summary>
		public LoadOrderDirectoryPolicy(IEnumerable<string> allowedExtensions, bool allowSubdirectories)
		{
			string[] extensions = (allowedExtensions ?? Enumerable.Empty<string>()).Take(17).ToArray();
			if (extensions.Length == 0 || extensions.Length > 16 || extensions.Any(extension =>
				String.IsNullOrEmpty(extension) || extension.Length > 16 || extension[0] != '.' || extension.Length < 2 ||
				!extension.Skip(1).All(character => character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z' || character >= '0' && character <= '9')) ||
				extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != extensions.Length)
				throw new ArgumentException("A package directory requires 1-16 unique file extensions.", nameof(allowedExtensions));
			AllowedExtensions = Array.AsReadOnly(extensions.Select(extension => extension.ToLowerInvariant()).ToArray());
			AllowSubdirectories = allowSubdirectories;
		}

		public IReadOnlyList<string> AllowedExtensions { get; }
		public bool AllowSubdirectories { get; }
	}

	/// <summary>Immutable native load-order configuration selected by game.json and shared by inventory, lifecycle and review services.</summary>
	public sealed class LoadOrderCapability
	{
		public const string LarianProviderId = "larian-module-settings-v1";

		/// <summary>Captures the existing profile rule so the capability cannot introduce a second profile location.</summary>
		internal LoadOrderCapability(string provider, int revision, LoadOrderDirectoryPolicy directoryPolicy, StructuredXmlRecordRule profiles, IEnumerable<string> builtInModuleIdentities = null)
		{
			Provider = provider;
			Revision = revision;
			DirectoryPolicy = directoryPolicy ?? throw new ArgumentNullException(nameof(directoryPolicy));
			Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
			BuiltInModuleIdentities = Array.AsReadOnly(ValidateBuiltInIdentities(builtInModuleIdentities));
		}

		public string Provider { get; }
		public int Revision { get; }
		public LoadOrderDirectoryPolicy DirectoryPolicy { get; }
		public StructuredXmlRecordRule Profiles { get; }
		public IReadOnlyList<string> BuiltInModuleIdentities { get; }

		/// <summary>Validates optional game-supplied vanilla identities without inferring installed game/DLC versions.</summary>
		internal static string[] ValidateBuiltInIdentities(IEnumerable<string> identities)
		{
			string[] source = (identities ?? Enumerable.Empty<string>()).Take(257).ToArray();
			if (source.Length > 256) throw new ArgumentException("loadOrder.builtInModuleIdentities supports at most 256 UUIDs.");
			string[] result = source.Select(value =>
			{
				Guid id;
				if (!Guid.TryParse(value, out id) || id == Guid.Empty) throw new ArgumentException("loadOrder.builtInModuleIdentities requires valid UUIDs.");
				return id.ToString("D");
			}).ToArray();
			if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length) throw new ArgumentException("Duplicate loadOrder built-in module UUID.");
			return result;
		}
	}
}
