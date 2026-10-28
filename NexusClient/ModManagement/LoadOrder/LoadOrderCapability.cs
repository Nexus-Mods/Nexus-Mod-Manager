using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Optional inventory capability; its presence does not authorize native order publication.</summary>
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

	/// <summary>Validated read-only capability selected by game.json; existing native effects remain separate.</summary>
	public sealed class LoadOrderCapability
	{
		public const string LarianProviderId = "larian-module-settings-v1";

		/// <summary>Captures the existing profile rule so the capability cannot introduce a second profile location.</summary>
		internal LoadOrderCapability(string provider, int revision, LoadOrderDirectoryPolicy directoryPolicy, StructuredXmlRecordRule profiles)
		{
			Provider = provider;
			Revision = revision;
			DirectoryPolicy = directoryPolicy ?? throw new ArgumentNullException(nameof(directoryPolicy));
			Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
		}

		public string Provider { get; }
		public int Revision { get; }
		public LoadOrderDirectoryPolicy DirectoryPolicy { get; }
		public StructuredXmlRecordRule Profiles { get; }
	}
}
