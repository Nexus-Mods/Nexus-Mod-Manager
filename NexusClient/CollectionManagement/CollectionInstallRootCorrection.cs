using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Frozen existing game-folder content and an optional Collection winner to retain during repair.</summary>
	public sealed class CollectionInstallRootDestination
	{
		/// <summary>Captures one reviewed destination conflict before native mutation.</summary>
		public CollectionInstallRootDestination(CollectionNativeFileContentEvidence before, string currentOwnerKey, bool preserveWinner)
		{
			Before = before ?? throw new ArgumentNullException(nameof(before));
			if (before.Target.Root != ModDeploymentRoot.GameRoot || (preserveWinner && (!before.Existed || String.IsNullOrWhiteSpace(currentOwnerKey))))
				throw new ArgumentException("A preserved game-folder winner requires exact current bytes and ownership.");
			CurrentOwnerKey = currentOwnerKey;
			PreserveWinner = preserveWinner;
		}

		public CollectionNativeFileContentEvidence Before { get; }
		public string CurrentOwnerKey { get; }
		public bool PreserveWinner { get; }
	}

	/// <summary>One reviewed old-folder cleanup, including the bytes and ownership to restore.</summary>
	public sealed class CollectionInstallRootFileRemoval
	{
		/// <summary>Freezes the old target's preimage and its expected remaining winner.</summary>
		public CollectionInstallRootFileRemoval(CollectionNativeFileContentEvidence before,
			CollectionNativeFileContentEvidence after, IEnumerable<string> ownersBefore, IEnumerable<string> ownersAfter)
		{
			Before = before ?? throw new ArgumentNullException(nameof(before));
			After = after ?? throw new ArgumentNullException(nameof(after));
			if (!before.Target.Equals(after.Target)) throw new ArgumentException("Folder correction evidence must describe one old target.");
			OwnersBefore = CopyOwners(ownersBefore);
			OwnersAfter = CopyOwners(ownersAfter);
		}

		public CollectionNativeFileContentEvidence Before { get; }
		public CollectionNativeFileContentEvidence After { get; }
		public ReadOnlyCollection<string> OwnersBefore { get; }
		public ReadOnlyCollection<string> OwnersAfter { get; }

		/// <summary>Copies a resolved ownership stack without duplicate owners.</summary>
		private static ReadOnlyCollection<string> CopyOwners(IEnumerable<string> owners)
		{
			List<string> copied = (owners ?? throw new ArgumentNullException(nameof(owners))).ToList();
			if (copied.Any(String.IsNullOrWhiteSpace) || copied.Distinct(StringComparer.OrdinalIgnoreCase).Count() != copied.Count)
				throw new ArgumentException("Folder correction ownership must be resolved and unique.", nameof(owners));
			return new ReadOnlyCollection<string>(copied);
		}
	}

	/// <summary>Frozen approval to move one installed package from Data to the game folder without changing its method.</summary>
	public sealed class CollectionInstallRootCorrection
	{
		/// <summary>Creates an exact, restart-safe old-folder cleanup scope.</summary>
		public CollectionInstallRootCorrection(string nativeModKey, ModInstallMethod method,
			IEnumerable<CollectionInstallRootFileRemoval> files, IEnumerable<CollectionInstallRootDestination> destinations = null)
		{
			if (String.IsNullOrWhiteSpace(nativeModKey)) throw new ArgumentException("An installed native mod key is required.", nameof(nativeModKey));
			if (!Enum.IsDefined(typeof(ModInstallMethod), method))
				throw new ArgumentOutOfRangeException(nameof(method));
			NativeModKey = nativeModKey;
			InstallMethod = method;
			List<CollectionInstallRootFileRemoval> copied = (files ?? throw new ArgumentNullException(nameof(files))).ToList();
			if (copied.Any(x => x == null || x.Before.Target.Root != ModDeploymentRoot.Data ||
				!x.OwnersBefore.Contains(nativeModKey, StringComparer.OrdinalIgnoreCase) ||
				!x.OwnersAfter.SequenceEqual(x.OwnersBefore.Where(y => !StringComparer.OrdinalIgnoreCase.Equals(y, nativeModKey)), StringComparer.OrdinalIgnoreCase)) ||
				copied.Select(x => x.Before.Target).Distinct().Count() != copied.Count)
				throw new ArgumentException("Folder correction must remove only the reviewed mod from unique Data targets.", nameof(files));
			Files = new ReadOnlyCollection<CollectionInstallRootFileRemoval>(copied.OrderBy(x => x.Before.Target.RelativePath, StringComparer.OrdinalIgnoreCase).ToList());
			List<CollectionInstallRootDestination> targets = (destinations ?? Enumerable.Empty<CollectionInstallRootDestination>()).ToList();
			if (targets.Any(x => x == null) || targets.Select(x => x.Before.Target).Distinct().Count() != targets.Count)
				throw new ArgumentException("Folder correction destinations must be unique.", nameof(destinations));
			Destinations = new ReadOnlyCollection<CollectionInstallRootDestination>(targets.OrderBy(x => x.Before.Target.RelativePath, StringComparer.OrdinalIgnoreCase).ToList());
		}

		public string NativeModKey { get; }
		public ModInstallMethod InstallMethod { get; }
		public ReadOnlyCollection<CollectionInstallRootFileRemoval> Files { get; }
		public ReadOnlyCollection<CollectionInstallRootDestination> Destinations { get; }

		/// <summary>Checks that an approved repair will encounter only its reviewed destination files and owners.</summary>
		internal bool VerifyDestinations(CollectionNativeStateIndex state)
		{
			foreach (CollectionInstallRootDestination destination in Destinations)
			{
				CollectionNativeFileState file;
				state.Files.TryGetValue(destination.Before.Target, out file);
				CollectionNativeRootState root = state.Roots.SingleOrDefault(x => x.Root == destination.Before.Target.Root);
				if (root == null || !StringComparer.OrdinalIgnoreCase.Equals(destination.CurrentOwnerKey, file == null ? null : file.EffectiveOwnerKey) ||
					!MatchesFile(destination.Before, file == null ? Path.Combine(root.PhysicalPath, destination.Before.Target.RelativePath) : file.PhysicalPath)) return false;
			}
			return true;
		}

		/// <summary>Gets the physical result when a higher-priority installed Collection member must remain the winner.</summary>
		internal CollectionInstallRootDestination GetPreservedDestination(ModDeploymentTarget target)
		{
			return Destinations.SingleOrDefault(x => x.PreserveWinner && x.Before.Target.Equals(target));
		}

		/// <summary>Checks that the installed instance still uses the old reviewed method and root.</summary>
		internal bool MatchesPrevious(CollectionNativeModState native)
		{
			return native != null && StringComparer.OrdinalIgnoreCase.Equals(NativeModKey, native.Identity.NativeModKey) &&
				native.InstallMethod == InstallMethod && native.InstallRoot == ModInstallRoot.Data;
		}

		/// <summary>Checks old-folder ownership and bytes against either the approved preimage or committed result.</summary>
		internal bool Verify(CollectionNativeStateIndex state, bool committed)
		{
			foreach (CollectionInstallRootFileRemoval file in Files)
			{
				CollectionNativeFileState observed;
				state.Files.TryGetValue(file.Before.Target, out observed);
				IEnumerable<CollectionNativeOwnerState> owners = observed == null ? Enumerable.Empty<CollectionNativeOwnerState>() :
					observed.InstallLogOwners.Concat(observed.DeploymentOwners).Concat(observed.VirtualOwners);
				if (owners.Any(x => x.Kind == CollectionNativeOwnerKind.Unresolved)) return false;
				bool owned = owners.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, NativeModKey));
				if (owned == committed) return false;
				CollectionNativeFileContentEvidence expected = committed ? file.After : file.Before;
				string physical = observed == null ? null : observed.PhysicalPath;
				if (String.IsNullOrWhiteSpace(physical))
				{
					CollectionNativeRootState root;
					root = state.Roots.SingleOrDefault(x => x.Root == expected.Target.Root);
					if (root == null) return false;
					physical = Path.Combine(root.PhysicalPath, expected.Target.RelativePath);
				}
				if (!MatchesFile(expected, physical)) return false;
				string[] remaining = owners.Where(x => x.Kind == CollectionNativeOwnerKind.NativeMod).Select(x => x.OwnerKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
				IEnumerable<string> expectedOwners = committed ? file.OwnersAfter : file.OwnersBefore;
				if (!new HashSet<string>(remaining, StringComparer.OrdinalIgnoreCase).SetEquals(expectedOwners.Where(x =>
					state.ModsByNativeKey.ContainsKey(x) || StringComparer.OrdinalIgnoreCase.Equals(x, NativeModKey)))) return false;
				string expectedWinner = expectedOwners.LastOrDefault();
				if (expectedWinner != null && state.ModsByNativeKey.ContainsKey(expectedWinner) &&
					(observed == null || !StringComparer.OrdinalIgnoreCase.Equals(expectedWinner, observed.EffectiveOwnerKey))) return false;
			}
			return true;
		}

		/// <summary>Captures the actual native restoration result before presenting a folder correction for review.</summary>
		internal static PreparedCollectionNativeRecipe Prepare(PreparedCollectionNativeRecipe recipe,
			CollectionNativeStateIndex state, CollectionNativeModState previous, IModDeploymentManager deployment,
			IVirtualModActivator virtualActivator, CancellationToken cancellationToken)
		{
			if (previous == null || previous.InstallRoot == recipe.InstallContext.InstallRoot) return recipe;
			if (previous.InstallRoot != ModInstallRoot.Data || recipe.InstallContext.InstallRoot != ModInstallRoot.GameRoot ||
				previous.InstallMethod != recipe.InstallContext.Method)
				throw new InvalidOperationException("Collection folder correction must preserve the installation method and move only from Data to the game folder.");
			ReadOnlyCollection<CollectionNativeFileState> oldFiles;
			var removals = new List<CollectionInstallRootFileRemoval>();
			if (state.FilesByOwnerKey.TryGetValue(previous.Identity.NativeModKey, out oldFiles))
				foreach (CollectionNativeFileState file in oldFiles.Where(x => !recipe.EffectPreview.Files.Any(y => y.Target.Equals(x.Target))))
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (file.Target.Root != ModDeploymentRoot.Data) throw new InvalidOperationException("The old installation has additional game-folder effects that need a separate review.");
					string[] owners = deployment.GetOwnerKeys(file.Target).ToArray();
					string[] remaining = owners.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, previous.Identity.NativeModKey)).ToArray();
					CollectionNativeFileContentEvidence before = CaptureFile(file.Target, file.PhysicalPath);
					string winner = remaining.LastOrDefault();
					string source = winner == null ? null : deployment.GetOwnerSourcePath(file.Target, winner);
					if (winner != null && String.IsNullOrWhiteSpace(source)) source = deployment.GetOwnerBackupPath(file.Target, winner);
					if (winner == null && !file.Promoted)
						foreach (string owner in owners)
						{
							string backup = virtualActivator.GetVirtualOverwritePath(file.Target, owner);
							if (!String.IsNullOrWhiteSpace(backup) && File.Exists(backup)) { source = backup; break; }
						}
					if (winner != null && (String.IsNullOrWhiteSpace(source) || !File.Exists(source)))
						throw new InvalidOperationException("The file to restore after moving this mod is unavailable: " + file.Target.RelativePath);
					removals.Add(new CollectionInstallRootFileRemoval(before, CaptureFile(file.Target, source), owners, remaining));
				}
			return Attach(recipe, new CollectionInstallRootCorrection(previous.Identity.NativeModKey, previous.InstallMethod, removals));
		}

		/// <summary>Reuses a frozen correction without consulting current package rules or recapturing its cleanup scope.</summary>
		internal static PreparedCollectionNativeRecipe Attach(PreparedCollectionNativeRecipe recipe, CollectionInstallRootCorrection correction)
		{
			if (correction == null) return recipe;
			CollectionMemberEffectPreview preview = recipe.EffectPreview.WithInstallRootCorrection(correction);
			string fingerprint;
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream))
			{
				writer.Write(recipe.PreparedNativeIdentity.Fingerprint);
				Write(writer, correction);
				writer.Flush();
				using (SHA256 sha = SHA256.Create()) fingerprint = BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", String.Empty).ToLowerInvariant();
			}
			return new PreparedCollectionNativeRecipe(recipe.Member, PreparedCollectionNativeRecipeIdentity.FromFingerprint("sha256:" + fingerprint),
				recipe.RecipeInput, preview, recipe.SkipReadmeFiles, recipe.RetainedArtifactIds, recipe.GameRootArchiveBaseDirectory);
		}

		/// <summary>Captures a file or proves its directory entry absent, including broken links.</summary>
		internal static CollectionNativeFileContentEvidence CaptureFile(ModDeploymentTarget target, string path)
		{
			if (String.IsNullOrWhiteSpace(path)) return new CollectionNativeFileContentEvidence(target, false, null, 0);
			if (!File.Exists(path))
			{
				try { File.GetAttributes(path); }
				catch (FileNotFoundException) { return new CollectionNativeFileContentEvidence(target, false, null, 0); }
				catch (DirectoryNotFoundException) { return new CollectionNativeFileContentEvidence(target, false, null, 0); }
				throw new IOException("The reviewed target is not a readable file: " + path);
			}
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (SHA256 sha = SHA256.Create())
				return new CollectionNativeFileContentEvidence(target, true,
					CollectionContentHash.FromSha256(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant()), stream.Length);
		}

		/// <summary>Checks exact presence and bytes at one reviewed physical target.</summary>
		private static bool MatchesFile(CollectionNativeFileContentEvidence expected, string path)
		{
			CollectionNativeFileContentEvidence actual = CaptureFile(expected.Target, path);
			return actual.Existed == expected.Existed && actual.ByteLength == expected.ByteLength && Equals(actual.ContentHash, expected.ContentHash);
		}

		/// <summary>Writes the optional correction for versioned review and restart records.</summary>
		internal static void Write(BinaryWriter writer, CollectionInstallRootCorrection correction)
		{
			writer.Write(correction != null);
			if (correction == null) return;
			writer.Write(correction.NativeModKey); writer.Write((int)correction.InstallMethod); writer.Write(correction.Files.Count);
			foreach (CollectionInstallRootFileRemoval file in correction.Files)
			{
				writer.Write(file.Before.Target.RelativePath);
				WriteContent(writer, file.Before); WriteContent(writer, file.After);
				writer.Write(file.OwnersBefore.Count); foreach (string owner in file.OwnersBefore) writer.Write(owner);
				writer.Write(file.OwnersAfter.Count); foreach (string owner in file.OwnersAfter) writer.Write(owner);
			}
			writer.Write(correction.Destinations.Count);
			foreach (CollectionInstallRootDestination destination in correction.Destinations)
			{
				writer.Write(destination.Before.Target.RelativePath); WriteContent(writer, destination.Before);
				writer.Write(destination.CurrentOwnerKey != null); if (destination.CurrentOwnerKey != null) writer.Write(destination.CurrentOwnerKey);
				writer.Write(destination.PreserveWinner);
			}
		}

		/// <summary>Reads a correction without interpreting the current GameMode configuration.</summary>
		internal static CollectionInstallRootCorrection Read(BinaryReader reader)
		{
			if (!reader.ReadBoolean()) return null;
			string key = reader.ReadString(); ModInstallMethod method = (ModInstallMethod)reader.ReadInt32();
			var files = new List<CollectionInstallRootFileRemoval>();
			for (int i = 0, count = ReadCount(reader); i < count; i++)
			{
				var target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, reader.ReadString());
				CollectionNativeFileContentEvidence before = ReadContent(reader, target), after = ReadContent(reader, target);
				var ownersBefore = new List<string>(); for (int j = 0, c = ReadCount(reader); j < c; j++) ownersBefore.Add(reader.ReadString());
				var ownersAfter = new List<string>(); for (int j = 0, c = ReadCount(reader); j < c; j++) ownersAfter.Add(reader.ReadString());
				files.Add(new CollectionInstallRootFileRemoval(before, after, ownersBefore, ownersAfter));
			}
			var destinations = new List<CollectionInstallRootDestination>();
			for (int i = 0, count = ReadCount(reader); i < count; i++)
			{
				var target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, reader.ReadString());
				CollectionNativeFileContentEvidence before = ReadContent(reader, target);
				string owner = reader.ReadBoolean() ? reader.ReadString() : null;
				destinations.Add(new CollectionInstallRootDestination(before, owner, reader.ReadBoolean()));
			}
			return new CollectionInstallRootCorrection(key, method, files, destinations);
		}

		/// <summary>Encodes optional frozen folder correction data in extensible JSON records.</summary>
		internal static byte[] Serialize(CollectionInstallRootCorrection correction)
		{
			if (correction == null) return null;
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream))
			{ Write(writer, correction); writer.Flush(); return stream.ToArray(); }
		}

		/// <summary>Decodes an optional retained correction while rejecting trailing data.</summary>
		internal static CollectionInstallRootCorrection Deserialize(byte[] bytes)
		{
			if (bytes == null) return null;
			if (bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid folder correction payload length.");
			using (var stream = new MemoryStream(bytes, false))
			using (var reader = new BinaryReader(stream))
			{
				CollectionInstallRootCorrection result = Read(reader);
				if (stream.Position != stream.Length || result == null) throw new InvalidDataException("Invalid retained folder correction.");
				return result;
			}
		}

		/// <summary>Writes the content identity of an existing or absent file.</summary>
		private static void WriteContent(BinaryWriter writer, CollectionNativeFileContentEvidence content)
		{
			writer.Write(content.Existed);
			if (content.Existed) { writer.Write(content.ContentHash.Value); writer.Write(content.ByteLength); }
		}

		/// <summary>Reads one frozen file-content identity.</summary>
		private static CollectionNativeFileContentEvidence ReadContent(BinaryReader reader, ModDeploymentTarget target)
		{
			return reader.ReadBoolean() ? new CollectionNativeFileContentEvidence(target, true, CollectionContentHash.FromSha256(reader.ReadString()), reader.ReadInt64()) :
				new CollectionNativeFileContentEvidence(target, false, null, 0);
		}

		/// <summary>Bounds retained record counts before allocating collections.</summary>
		private static int ReadCount(BinaryReader reader)
		{
			int count = reader.ReadInt32();
			if (count < 0 || count > 100000) throw new InvalidDataException("Invalid folder correction record count.");
			return count;
		}
	}
}
