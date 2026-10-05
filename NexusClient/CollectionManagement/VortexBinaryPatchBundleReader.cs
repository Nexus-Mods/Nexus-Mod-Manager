using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using SevenZip;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Reads only the exact characterized Vortex patch payloads from an immutable retained Collection bundle.</summary>
	internal sealed class VortexBinaryPatchBundleReader
	{
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		public VortexBinaryPatchBundleReader(CollectionsRevisionSourceStore revisionSourceStore)
		{
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
		}

		public IDictionary<string, byte[]> Read(CollectionRevisionIdentity revision, ResolvedCollectionMemberPlan member, CancellationToken cancellationToken)
		{
			if (revision == null) throw new ArgumentNullException(nameof(revision));
			if (member == null) throw new ArgumentNullException(nameof(member));
			if (!member.HasVortexBinaryPatches) return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

			var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			using (Stream bundle = _revisionSourceStore.OpenBundle(revision, cancellationToken))
			using (var extractor = new SevenZipExtractor(bundle))
			{
				string patchFolder = ValidatePatchFolder(member.DisplayName);
				foreach (CollectionVortexBinaryPatch patch in member.VortexBinaryPatches.Items)
				{
					cancellationToken.ThrowIfCancellationRequested();
					string expectedPath = "patches/" + patchFolder + "/" + patch.DestinationPath.Replace('\\', '/') + ".diff";
					var candidates = extractor.ArchiveFileData.Where(x => !x.IsDirectory)
						.Where(x => String.Equals(Normalize(x.FileName), expectedPath, StringComparison.OrdinalIgnoreCase)).ToList();
					if (candidates.Count != 1)
						throw new InvalidDataException("The retained Collection bundle must contain exactly one Vortex .diff payload at '" + expectedPath + "'.");
					using (var output = new MemoryStream())
					{
						extractor.ExtractFile(candidates[0].Index, output);
						result.Add(patch.DestinationPath, output.ToArray());
					}
				}
			}
			return result;
		}

		private static string ValidatePatchFolder(string displayName)
		{
			if (String.IsNullOrWhiteSpace(displayName) || !String.Equals(displayName, displayName.Trim(), StringComparison.Ordinal) ||
				displayName == "." || displayName == ".." || Path.IsPathRooted(displayName) ||
				displayName.IndexOf('/') >= 0 || displayName.IndexOf('\\') >= 0 ||
				displayName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				throw new InvalidDataException("The Collection member name cannot identify a safe Vortex patch payload directory.");
			}

			return displayName;
		}

		private static string Normalize(string path)
		{
			return (path ?? String.Empty).Replace('\\', '/').TrimStart('/');
		}
	}
}
