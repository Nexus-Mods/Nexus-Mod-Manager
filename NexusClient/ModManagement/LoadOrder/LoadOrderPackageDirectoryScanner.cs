using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Top-level directory evidence; unexpected contents do not imply that enumeration itself failed.</summary>
	public sealed class LoadOrderPackageDirectoryObservation
	{
		/// <summary>Freezes discovered files and problems without claiming a complete package inspection.</summary>
		internal LoadOrderPackageDirectoryObservation(string root, IEnumerable<LoadOrderPackageFile> files,
			IEnumerable<LoadOrderDiagnostic> diagnostics, bool isComplete)
		{
			Root = root; Files = Array.AsReadOnly(files.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
				.ThenBy(file => file.FileName, StringComparer.Ordinal).ToArray());
			Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); IsComplete = isComplete;
		}
		public string Root { get; }
		public IReadOnlyList<LoadOrderPackageFile> Files { get; }
		public IReadOnlyList<LoadOrderDiagnostic> Diagnostics { get; }
		public bool IsComplete { get; }
	}

	/// <summary>Read-only, bounded package enumeration. Does not recurse, flatten, create, quarantine or delete anything.</summary>
	public static class LoadOrderPackageDirectoryScanner
	{
		/// <summary>Enumerates hidden entries too; a missing or inaccessible root is incomplete evidence, not an empty mod list.</summary>
		public static LoadOrderPackageDirectoryObservation Scan(string packageRoot, LoadOrderDirectoryPolicy policy,
			CancellationToken cancellationToken, int maximumEntries = 10000)
		{
			if (String.IsNullOrWhiteSpace(packageRoot) || !Path.IsPathRooted(packageRoot))
				throw new ArgumentException("An absolute package root is required.", nameof(packageRoot));
			if (policy == null) throw new ArgumentNullException(nameof(policy));
			if (maximumEntries < 1 || maximumEntries > 100000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
			string root = Path.GetFullPath(packageRoot);
			var files = new List<LoadOrderPackageFile>();
			var diagnostics = new List<LoadOrderDiagnostic>();
			bool complete = true;
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				if ((File.GetAttributes(root) & FileAttributes.Directory) == 0)
					throw new IOException("The configured package root is not a directory.");
			}
			catch (Exception error) when (IsReadError(error))
			{
				diagnostics.Add(new LoadOrderDiagnostic(LoadOrderDiagnosticKind.RootUnavailable, LoadOrderDiagnosticSeverity.Error, root, technicalDetails: error.Message));
				return new LoadOrderPackageDirectoryObservation(root, files, diagnostics, false);
			}
			try
			{
				int count = 0;
				foreach (string entry in Directory.EnumerateFileSystemEntries(root))
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (++count > maximumEntries)
					{
						diagnostics.Add(new LoadOrderDiagnostic(LoadOrderDiagnosticKind.ScanLimitReached, LoadOrderDiagnosticSeverity.Error, root));
						complete = false;
						break;
					}
					try
					{
						FileAttributes attributes = File.GetAttributes(entry);
						if ((attributes & FileAttributes.Directory) != 0)
						{
							if (!policy.AllowSubdirectories)
								diagnostics.Add(new LoadOrderDiagnostic(LoadOrderDiagnosticKind.UnexpectedDirectory, LoadOrderDiagnosticSeverity.Error, entry));
							continue;
						}
						if (!policy.AllowedExtensions.Contains(Path.GetExtension(entry), StringComparer.OrdinalIgnoreCase))
						{
							diagnostics.Add(new LoadOrderDiagnostic(LoadOrderDiagnosticKind.UnexpectedFile, LoadOrderDiagnosticSeverity.Error, entry));
							continue;
						}
						files.Add(Observe(entry));
					}
					catch (Exception error) when (IsReadError(error))
					{
						complete = false;
						AddReadFailure(diagnostics, entry, error);
					}
				}
			}
			catch (Exception error) when (IsReadError(error))
			{
				complete = false;
				AddReadFailure(diagnostics, root, error);
			}
			return new LoadOrderPackageDirectoryObservation(root, files, diagnostics, complete);
		}

		/// <summary>Observes file metadata; NMM's existing symbolic-link fallback remains readable.</summary>
		internal static LoadOrderPackageFile Observe(string path)
		{
			var file = new FileInfo(path);
			if ((file.Attributes & FileAttributes.Directory) != 0) throw new IOException("A package path became a directory.");
			return new LoadOrderPackageFile(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
		}

		/// <summary>Recognizes expected filesystem failures without hiding cancellation or programming errors.</summary>
		internal static bool IsReadError(Exception error) => error is IOException || error is UnauthorizedAccessException || error is SecurityException;

		/// <summary>Retains actionable path evidence without treating a failed read as proof of absence.</summary>
		private static void AddReadFailure(List<LoadOrderDiagnostic> diagnostics, string path, Exception error)
		{
			diagnostics.Add(new LoadOrderDiagnostic(LoadOrderDiagnosticKind.EnumerationFailed, LoadOrderDiagnosticSeverity.Error, path, technicalDetails: error.Message));
		}
	}
}
