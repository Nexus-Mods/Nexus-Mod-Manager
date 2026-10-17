using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace Nexus.Client.Games.Witcher3
{
	/// <summary>Non-mutating evidence for one root. The normalized path is not a resolved NTFS identity.</summary>
	public sealed class Witcher3InstallationObservation
	{
		internal Witcher3InstallationObservation(string normalizedGameRoot, bool gameRootExists,
			Witcher3EditionEvidence evidence)
		{
			NormalizedGameRoot = normalizedGameRoot;
			GameRootExists = gameRootExists;
			Evidence = evidence;
			Edition = Witcher3EditionResolver.Resolve(evidence);
		}

		public string NormalizedGameRoot { get; }
		public bool GameRootExists { get; }
		public Witcher3EditionEvidence Evidence { get; }
		public Witcher3EditionResolution Edition { get; }
	}

	/// <summary>
	/// A read-only edition probe. Do not use the lexical root to authorize two NMM modes
	/// against one installation: junctions and other aliases require a separate physical-root
	/// ownership guard before any game writes are enabled.
	/// </summary>
	public static class Witcher3InstallationProbe
	{
		/// <summary>Reads executable existence and version-resource evidence; never creates a directory.</summary>
		public static Witcher3InstallationObservation Inspect(string gameRoot)
		{
			if (String.IsNullOrWhiteSpace(gameRoot))
				throw new ArgumentException("The Witcher 3 installation root must be specified.", nameof(gameRoot));
			if (!Path.IsPathRooted(gameRoot))
				throw new ArgumentException("The Witcher 3 installation root must be absolute.", nameof(gameRoot));

			string normalizedRoot = Path.GetFullPath(gameRoot);
			string volumeRoot = Path.GetPathRoot(normalizedRoot);
			if (normalizedRoot.Length > volumeRoot.Length)
				normalizedRoot = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			bool rootExists = Directory.Exists(normalizedRoot);
			string dx11 = Path.Combine(normalizedRoot, "bin", "x64", "witcher3.exe");
			string dx12 = Path.Combine(normalizedRoot, "bin", "x64_dx12", "witcher3.exe");
			bool hasDx11 = rootExists && File.Exists(dx11);
			bool hasDx12 = rootExists && File.Exists(dx12);
			bool unreadable = false;
			int? dx11Version = hasDx11 ? GetMajorVersion(dx11, ref unreadable) : null;
			int? dx12Version = hasDx12 ? GetMajorVersion(dx12, ref unreadable) : null;
			return new Witcher3InstallationObservation(normalizedRoot, rootExists,
				new Witcher3EditionEvidence(hasDx11, hasDx12, dx11Version, dx12Version, unreadable));
		}

		private static int? GetMajorVersion(string executable, ref bool unreadable)
		{
			try
			{
				FileVersionInfo info = FileVersionInfo.GetVersionInfo(executable);
				int fileMajor = info.FileMajorPart;
				int productMajor = info.ProductMajorPart;
				if (fileMajor > 0 && productMajor > 0 && fileMajor != productMajor)
				{
					// A mismatched PE version/product version cannot be interpreted as a
					// reliable edition marker without a known-good retail fixture.
					unreadable = true;
					return null;
				}
				if (fileMajor > 0)
					return fileMajor;
				return productMajor > 0 ? (int?)productMajor : null;
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is SecurityException || error is Win32Exception)
			{
				unreadable = true;
				return null;
			}
		}
	}
}
