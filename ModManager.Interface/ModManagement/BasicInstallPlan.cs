using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Identifies why the deterministic BasicInstall planner cannot represent a native basic-install path safely.
	/// </summary>
	public enum BasicInstallPlanUnsupportedReasonKind
	{
		None = 0,
		SpecialFileInstallation = 1,
		ModFileMerge = 2,
		NoInstallableFiles = 3,
		ReplicatedSource = 4,
		DestinationCollision = 5,
		UnrepresentableVirtualStoragePath = 6
	}

	/// <summary>
	/// Describes one exact archive source and logical native destination selected by deterministic BasicInstall planning.
	/// </summary>
	public sealed class BasicInstallPlanFile
	{
		/// <summary>
		/// Initializes one immutable BasicInstall file mapping and its characterized native path projections.
		/// </summary>
		public BasicInstallPlanFile(string sourcePath, string destinationPath, string gameInstallPath,
			string virtualStoragePath, ModDeploymentTarget deploymentTarget)
		{
			if (String.IsNullOrWhiteSpace(sourcePath))
				throw new ArgumentException("BasicInstall source path must not be empty.", nameof(sourcePath));
			if (String.IsNullOrWhiteSpace(destinationPath))
				throw new ArgumentException("BasicInstall destination path must not be empty.", nameof(destinationPath));
			if (String.IsNullOrWhiteSpace(gameInstallPath))
				throw new ArgumentException("BasicInstall game-install path must not be empty.", nameof(gameInstallPath));
			if (deploymentTarget == null)
				throw new ArgumentNullException(nameof(deploymentTarget));

			SourcePath = sourcePath;
			DestinationPath = destinationPath;
			GameInstallPath = gameInstallPath;
			VirtualStoragePath = virtualStoragePath;
			DeploymentTarget = deploymentTarget;
		}

		/// <summary>
		/// Gets the exact archive-relative source path.
		/// </summary>
		public string SourcePath { get; }

		/// <summary>
		/// Gets the logical destination passed to the native file-install operation.
		/// </summary>
		public string DestinationPath { get; }

		/// <summary>
		/// Gets the game-mode-adjusted path used by BasicInstall validation and readme filtering.
		/// </summary>
		public string GameInstallPath { get; }

		/// <summary>
		/// Gets the Virtual-storage-relative staging path, or <c>null</c> for Direct installation.
		/// </summary>
		public string VirtualStoragePath { get; }

		/// <summary>
		/// Gets the canonical native deployment target produced for this destination and install root.
		/// </summary>
		public ModDeploymentTarget DeploymentTarget { get; }
	}

	/// <summary>
	/// Describes one exact generated file produced by a side-effect-free game-specific BasicInstall merge plan.
	/// </summary>
	public sealed class BasicInstallGeneratedFile
	{
		/// <summary>Creates one immutable generated BasicInstall output.</summary>
		public BasicInstallGeneratedFile(string sourcePath, string destinationPath, byte[] data)
		{
			if (String.IsNullOrWhiteSpace(sourcePath))
				throw new ArgumentException("Generated BasicInstall output requires source provenance.", nameof(sourcePath));
			if (String.IsNullOrWhiteSpace(destinationPath))
				throw new ArgumentException("Generated BasicInstall output requires a destination path.", nameof(destinationPath));
			if (data == null || data.Length == 0)
				throw new ArgumentException("Generated BasicInstall output requires non-empty bytes.", nameof(data));
			SourcePath = sourcePath;
			DestinationPath = destinationPath;
			Data = (byte[])data.Clone();
		}

		public string SourcePath { get; }
		public string DestinationPath { get; }
		public byte[] Data { get; }
	}

	/// <summary>
	/// Describes one exact game-specific value produced by side-effect-free special-install planning.
	/// </summary>
	public sealed class BasicInstallGameSpecificValue
	{
		private readonly byte[] m_bteValue;

		/// <summary>Creates one immutable game-specific value effect.</summary>
		public BasicInstallGameSpecificValue(string key, byte[] value)
		{
			if (String.IsNullOrWhiteSpace(key))
				throw new ArgumentException("Generated BasicInstall game-specific value requires a key.", nameof(key));
			if (value == null || value.Length == 0)
				throw new ArgumentException("Generated BasicInstall game-specific value requires non-empty bytes.", nameof(value));
			Key = key;
			m_bteValue = (byte[])value.Clone();
		}

		public string Key { get; }
		public byte[] Value { get { return (byte[])m_bteValue.Clone(); } }
	}

	/// <summary>
	/// Stores the immutable deterministic subset of one native BasicInstall operation.
	/// </summary>
	public sealed class BasicInstallPlan
	{
		private readonly ReadOnlyCollection<BasicInstallPlanFile> m_rocFiles;
		private readonly ReadOnlyCollection<BasicInstallGeneratedFile> m_rocGeneratedFiles;
		private readonly ReadOnlyCollection<BasicInstallGameSpecificValue> m_rocGameSpecificValues;

		/// <summary>
		/// Initializes a deterministic BasicInstall plan for one exact native install context.
		/// </summary>
		public BasicInstallPlan(ModInstallContext installContext, IEnumerable<BasicInstallPlanFile> files)
			: this(installContext, files, null, null)
		{
		}

		/// <summary>Initializes a deterministic BasicInstall plan including exact generated merge outputs.</summary>
		public BasicInstallPlan(ModInstallContext installContext, IEnumerable<BasicInstallPlanFile> files,
			IEnumerable<BasicInstallGeneratedFile> generatedFiles)
			: this(installContext, files, generatedFiles, null)
		{
		}

		/// <summary>Initializes a deterministic BasicInstall plan including generated files and game-specific values.</summary>
		public BasicInstallPlan(ModInstallContext installContext, IEnumerable<BasicInstallPlanFile> files,
			IEnumerable<BasicInstallGeneratedFile> generatedFiles, IEnumerable<BasicInstallGameSpecificValue> gameSpecificValues)
		{
			InstallContext = installContext ?? throw new ArgumentNullException(nameof(installContext));
			if (files == null)
				throw new ArgumentNullException(nameof(files));

			var copiedFiles = new List<BasicInstallPlanFile>();
			foreach (BasicInstallPlanFile file in files)
			{
				if (file == null)
					throw new ArgumentException("BasicInstall plans cannot contain null file mappings.", nameof(files));
				copiedFiles.Add(file);
			}

			var copiedGenerated = new List<BasicInstallGeneratedFile>();
			if (generatedFiles != null)
			{
				foreach (BasicInstallGeneratedFile file in generatedFiles)
				{
					if (file == null)
						throw new ArgumentException("BasicInstall plans cannot contain null generated files.", nameof(generatedFiles));
					copiedGenerated.Add(file);
				}
			}

			var copiedGameValues = new List<BasicInstallGameSpecificValue>();
			if (gameSpecificValues != null)
			{
				foreach (BasicInstallGameSpecificValue value in gameSpecificValues)
				{
					if (value == null)
						throw new ArgumentException("BasicInstall plans cannot contain null game-specific values.", nameof(gameSpecificValues));
					copiedGameValues.Add(value);
				}
			}
			if (copiedGameValues.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != copiedGameValues.Count)
				throw new ArgumentException("BasicInstall plans cannot contain duplicate game-specific value keys.", nameof(gameSpecificValues));

			if (copiedFiles.Count == 0 && copiedGenerated.Count == 0)
				throw new ArgumentException("A deterministic BasicInstall plan requires at least one installable or generated file.", nameof(files));

			m_rocFiles = new ReadOnlyCollection<BasicInstallPlanFile>(copiedFiles);
			m_rocGeneratedFiles = new ReadOnlyCollection<BasicInstallGeneratedFile>(copiedGenerated);
			m_rocGameSpecificValues = new ReadOnlyCollection<BasicInstallGameSpecificValue>(copiedGameValues);
		}

		/// <summary>
		/// Gets the exact Direct/Virtual method and install root used while planning.
		/// </summary>
		public ModInstallContext InstallContext { get; }

		/// <summary>
		/// Gets the exact selected file mappings in native archive order.
		/// </summary>
		public IReadOnlyList<BasicInstallPlanFile> Files
		{
			get { return m_rocFiles; }
		}

		/// <summary>Gets exact generated game-specific merge outputs produced during planning.</summary>
		public IReadOnlyList<BasicInstallGeneratedFile> GeneratedFiles
		{
			get { return m_rocGeneratedFiles; }
		}

		/// <summary>Gets exact game-specific values produced by deterministic special-install planning.</summary>
		public IReadOnlyList<BasicInstallGameSpecificValue> GameSpecificValues
		{
			get { return m_rocGameSpecificValues; }
		}

		/// <summary>
		/// Converts the bounded BasicInstall mappings into the existing C5 simple exact-file recipe contract.
		/// </summary>
		public ModInstallationSimpleFileRecipe CreateSimpleFileRecipe()
		{
			IEnumerable<ModInstallationSimpleFileMapping> mappings = m_rocFiles.Select(file =>
				new ModInstallationSimpleFileMapping(file.SourcePath, file.DestinationPath))
				.Concat(m_rocGeneratedFiles.Select(file => new ModInstallationSimpleFileMapping(file.SourcePath, file.DestinationPath)));
			return new ModInstallationSimpleFileRecipe(mappings);
		}
	}

	/// <summary>
	/// Reports either one bounded BasicInstall plan or the precise unsupported native behavior that blocked planning.
	/// </summary>
	public sealed class BasicInstallPlanResult
	{
		private BasicInstallPlanResult(BasicInstallPlan plan, BasicInstallPlanUnsupportedReasonKind unsupportedReason, string message)
		{
			Plan = plan;
			UnsupportedReason = unsupportedReason;
			Message = message;
		}

		/// <summary>
		/// Gets whether the ordinary BasicInstall behavior was reduced to an exact immutable plan.
		/// </summary>
		public bool IsSupported
		{
			get { return Plan != null; }
		}

		/// <summary>
		/// Gets the deterministic plan when <see cref="IsSupported"/> is <c>true</c>.
		/// </summary>
		public BasicInstallPlan Plan { get; }

		/// <summary>
		/// Gets the classified unsupported behavior when planning failed closed.
		/// </summary>
		public BasicInstallPlanUnsupportedReasonKind UnsupportedReason { get; }

		/// <summary>
		/// Gets a diagnostic explanation suitable for later capability reporting.
		/// </summary>
		public string Message { get; }

		/// <summary>
		/// Creates a supported deterministic result.
		/// </summary>
		public static BasicInstallPlanResult Supported(BasicInstallPlan plan)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			return new BasicInstallPlanResult(plan, BasicInstallPlanUnsupportedReasonKind.None, null);
		}

		/// <summary>
		/// Creates a fail-closed unsupported result.
		/// </summary>
		public static BasicInstallPlanResult Unsupported(BasicInstallPlanUnsupportedReasonKind reason, string message)
		{
			if (reason == BasicInstallPlanUnsupportedReasonKind.None)
				throw new ArgumentOutOfRangeException(nameof(reason));
			if (String.IsNullOrWhiteSpace(message))
				throw new ArgumentException("An unsupported BasicInstall result requires an explanation.", nameof(message));
			return new BasicInstallPlanResult(null, reason, message);
		}
	}

	/// <summary>
	/// Expands the characterized, mutation-free subset of native <see cref="BasicInstallTask"/> behavior into exact file mappings.
	/// </summary>
	/// <remarks>
	/// Special-file installers and game-specific merge behavior remain fail-closed unless the active game mode exposes
	/// an explicit side-effect-free deterministic planning contract for the exact effect being reviewed.
	/// </remarks>
	public sealed class BasicInstallPlanBuilder
	{
		/// <summary>
		/// Plans the normal full-archive BasicInstall path without staging, deploying or otherwise mutating native state.
		/// </summary>
		public BasicInstallPlanResult Build(IMod mod, IGameMode gameMode, ModInstallContext installContext, bool skipReadme)
		{
			return Build(mod, gameMode, installContext, skipReadme, null, null);
		}

		/// <summary>
		/// Plans BasicInstall using the optional native source/destination selection supplied by the caller.
		/// </summary>
		public BasicInstallPlanResult Build(IMod mod, IGameMode gameMode, ModInstallContext installContext, bool skipReadme,
			IEnumerable<KeyValuePair<string, string>> filesToInstall)
		{
			return Build(mod, gameMode, installContext, skipReadme, filesToInstall, null);
		}

		/// <summary>Plans BasicInstall with the exact active-mod baseline required by deterministic game-specific merge providers.</summary>
		public BasicInstallPlanResult Build(IMod mod, IGameMode gameMode, ModInstallContext installContext, bool skipReadme,
			IEnumerable<KeyValuePair<string, string>> filesToInstall, IList<IMod> activeMods)
		{
			return Build(mod, gameMode, installContext, skipReadme, filesToInstall, activeMods, true);
		}

		/// <summary>Plans BasicInstall while optionally deferring publication of a deterministic merged output to a later reviewed member.</summary>
		public BasicInstallPlanResult Build(IMod mod, IGameMode gameMode, ModInstallContext installContext, bool skipReadme,
			IEnumerable<KeyValuePair<string, string>> filesToInstall, IList<IMod> activeMods, bool includeDeterministicModFileMergeOutput)
		{
			if (mod == null)
				throw new ArgumentNullException(nameof(mod));
			if (gameMode == null)
				throw new ArgumentNullException(nameof(gameMode));
			if (installContext == null)
				throw new ArgumentNullException(nameof(installContext));

			List<string> archiveFileList = mod.GetFileList();
			List<string> archiveFiles = archiveFileList == null
				? new List<string>()
				: new List<string>(archiveFileList);

			List<KeyValuePair<string, string>> files;
			var specialGameValues = new List<BasicInstallGameSpecificValue>();
			if (gameMode.RequiresSpecialFileInstallation && gameMode.IsSpecialFile(archiveFiles))
			{
				IEnumerable<string> deterministicSpecialFiles;
				IDeterministicSpecialFileInstallPlanProvider provider = gameMode as IDeterministicSpecialFileInstallPlanProvider;
				try
				{
					deterministicSpecialFiles = provider == null ? null : provider.GetDeterministicSpecialFileInstallPlan(mod);
				}
				catch (InvalidDataException ex)
				{
					return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.SpecialFileInstallation, ex.Message);
				}

				if (deterministicSpecialFiles == null)
				{
					return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.SpecialFileInstallation,
						"The game requires SpecialFileInstall behavior for this archive, but it does not expose a side-effect-free exact file selection for deterministic planning.");
				}

				files = deterministicSpecialFiles.Select(path => new KeyValuePair<string, string>(path, null)).ToList();

				IDeterministicSpecialFileGameValuePlanProvider gameValueProvider = gameMode as IDeterministicSpecialFileGameValuePlanProvider;
				if (gameValueProvider != null)
				{
					try
					{
						IEnumerable<BasicInstallGameSpecificValue> plannedValues = gameValueProvider.GetDeterministicSpecialFileGameValues(mod);
						if (plannedValues == null)
							return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.SpecialFileInstallation,
								"The game-specific SpecialFileInstall effects could not be represented deterministically.");
						specialGameValues.AddRange(plannedValues);
					}
					catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
					{
						return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.SpecialFileInstallation, ex.Message);
					}
				}
			}
			else
			{
				files = filesToInstall == null
					? archiveFiles.Select(path => new KeyValuePair<string, string>(path, null)).ToList()
					: new List<KeyValuePair<string, string>>(filesToInstall);
			}

			var generatedFiles = new List<BasicInstallGeneratedFile>();
			bool deferredDeterministicMergeOutput = false;
			if (gameMode.RequiresModFileMerge)
			{
				IDeterministicModFileMergePlanProvider mergeProvider = gameMode as IDeterministicModFileMergePlanProvider;
				if (mergeProvider == null || activeMods == null)
				{
					return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.ModFileMerge,
						"The game requires ModFileMerge behavior, but the current planning context cannot reproduce it from an exact active-mod baseline.");
				}
				if (installContext.InstallRoot != ModInstallRoot.Data)
				{
					return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.ModFileMerge,
						"The characterized deterministic ModFileMerge path currently requires the normal Data install root.");
				}

				DeterministicModFileMergePlan mergePlan;
				try
				{
					mergePlan = mergeProvider.GetDeterministicModFileMergePlan(activeMods, mod);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
				{
					return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.ModFileMerge, ex.Message);
				}

				if (mergePlan != null)
				{
					files = files.Where(file => !StringComparer.OrdinalIgnoreCase.Equals(file.Key, mergePlan.SourcePath)).ToList();
					if (includeDeterministicModFileMergeOutput)
						generatedFiles.Add(new BasicInstallGeneratedFile(mergePlan.SourcePath, mergePlan.DestinationPath, mergePlan.Data));
					else
						deferredDeterministicMergeOutput = true;
				}
			}

			if (installContext.InstallRoot == ModInstallRoot.GameRoot)
				files = NormalizeGameRootFileMappings(files);

			files = files.Where(file => !ModInstallFileFilter.IsIgnored(file.Key) && !ModInstallFileFilter.IsIgnored(file.Value)).ToList();

			var plannedFiles = new List<BasicInstallPlanFile>();
			var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var destinationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string> file in files)
			{
				string destination = String.IsNullOrWhiteSpace(file.Value) ? file.Key : file.Value;
				string gameInstallPath = GetAdjustedPath(gameMode, mod, installContext.InstallRoot, destination, ModPathContext.GameInstall);
				if (String.IsNullOrEmpty(gameInstallPath))
					continue;

				if (installContext.InstallRoot == ModInstallRoot.GameRoot)
					destination = gameInstallPath;

				if (ShouldSkipReadme(skipReadme, file.Key, gameInstallPath, gameMode.PluginDirectory))
					continue;

				string virtualStoragePath = null;
				if (installContext.Method == ModInstallMethod.Virtual)
				{
					virtualStoragePath = GetAdjustedPath(gameMode, mod, installContext.InstallRoot, destination, ModPathContext.VirtualStorage);
					if (String.IsNullOrEmpty(virtualStoragePath))
					{
						return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.UnrepresentableVirtualStoragePath,
							String.Format("The BasicInstall destination '{0}' does not resolve to a usable Virtual staging path.", destination));
					}
				}

				var simpleMapping = new ModInstallationSimpleFileMapping(file.Key, destination);
				if (!sourcePaths.Add(simpleMapping.SourcePath))
				{
					return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.ReplicatedSource,
						String.Format("BasicInstall source '{0}' would be installed more than once and cannot use the simple exact-file C5 adapter.", simpleMapping.SourcePath));
				}
				if (!destinationPaths.Add(simpleMapping.DestinationPath))
				{
					return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.DestinationCollision,
						String.Format("Several BasicInstall sources resolve to destination '{0}' and cannot use the simple exact-file C5 adapter.", simpleMapping.DestinationPath));
				}

				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(gameMode, mod, destination, installContext.InstallRoot);
				plannedFiles.Add(new BasicInstallPlanFile(simpleMapping.SourcePath, simpleMapping.DestinationPath,
					gameInstallPath, virtualStoragePath, target));
			}

			if (plannedFiles.Count == 0 && generatedFiles.Count == 0)
			{
				if (deferredDeterministicMergeOutput)
				{
					return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.ModFileMerge,
						"This projected merge contributor has no independent native file effect; deferring its only merged output would leave no executable registration recipe.");
				}
				return BasicInstallPlanResult.Unsupported(BasicInstallPlanUnsupportedReasonKind.NoInstallableFiles,
					"The BasicInstall archive has no files that can be represented as deterministic explicit native file operations.");
			}

			return BasicInstallPlanResult.Supported(new BasicInstallPlan(installContext, plannedFiles, generatedFiles, specialGameValues));
		}

		/// <summary>
		/// Applies the existing GameRoot wrapper recognition and safe relative-path normalization used by BasicInstallTask.
		/// </summary>
		internal static List<KeyValuePair<string, string>> NormalizeGameRootFileMappings(List<KeyValuePair<string, string>> files)
		{
			if (files == null || files.Count == 0)
				return files;

			string commonTopFolder = null;
			foreach (KeyValuePair<string, string> file in files)
			{
				string sourcePath = file.Key;
				if (IsUnsafeGameRootArchivePath(sourcePath))
					throw new InvalidDataException(String.Format("Game-root install path '{0}' cannot be installed safely.", sourcePath));

				string topFolder = GetTopLevelFolder(sourcePath);
				if (String.IsNullOrEmpty(topFolder))
					return NormalizeGameRootFileMappings(files, false);

				if (commonTopFolder == null)
					commonTopFolder = topFolder;
				else if (!commonTopFolder.Equals(topFolder, StringComparison.OrdinalIgnoreCase))
					return NormalizeGameRootFileMappings(files, false);
			}

			bool hasRecognizableRootContent = files.Any(file => IsRecognizableGameRootContent(StripTopLevelFolder(file.Key)));
			return NormalizeGameRootFileMappings(files, hasRecognizableRootContent);
		}

		/// <summary>
		/// Resolves one BasicInstall path through the same GameRoot or game-mode path adjustment used by native execution.
		/// </summary>
		internal static string GetAdjustedPath(IGameMode gameMode, IMod mod, ModInstallRoot installRoot, string path, ModPathContext context)
		{
			if (installRoot == ModInstallRoot.GameRoot)
				return NormalizeGameRootRelativePath(path);

			if (context == ModPathContext.GameInstall)
				return gameMode.GetModFormatAdjustedPath(mod.Format, path, mod, context);

			return gameMode.GetModFormatAdjustedPath(mod.Format, path, context);
		}

		/// <summary>
		/// Applies the native BasicInstall readme-suppression predicate without mutating installation state.
		/// </summary>
		internal static bool ShouldSkipReadme(bool skipReadme, string sourcePath, string gameInstallPath, string pluginDirectory)
		{
			string sourceDirectory = Path.GetDirectoryName(sourcePath);
			string targetDirectory = Path.GetDirectoryName(gameInstallPath);
			return skipReadme && Readme.IsValidReadme(sourcePath) &&
				(String.IsNullOrEmpty(sourceDirectory) ||
					(!String.IsNullOrEmpty(targetDirectory) &&
					 targetDirectory.Equals(Path.GetFileName(pluginDirectory), StringComparison.CurrentCultureIgnoreCase)));
		}

		private static List<KeyValuePair<string, string>> NormalizeGameRootFileMappings(List<KeyValuePair<string, string>> files, bool stripCommonWrapper)
		{
			return files.Select(file =>
			{
				string destination = stripCommonWrapper ? StripTopLevelFolder(file.Key) : file.Key;
				return new KeyValuePair<string, string>(file.Key, NormalizeGameRootRelativePath(destination));
			}).ToList();
		}

		private static string GetTopLevelFolder(string path)
		{
			if (String.IsNullOrWhiteSpace(path))
				return null;

			string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
			int separatorIndex = normalizedPath.IndexOf(Path.DirectorySeparatorChar);
			return separatorIndex <= 0 ? null : normalizedPath.Substring(0, separatorIndex);
		}

		private static string StripTopLevelFolder(string path)
		{
			if (String.IsNullOrWhiteSpace(path))
				return path;

			string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
			int separatorIndex = normalizedPath.IndexOf(Path.DirectorySeparatorChar);
			return separatorIndex < 0 || separatorIndex + 1 >= normalizedPath.Length ? normalizedPath : normalizedPath.Substring(separatorIndex + 1);
		}

		private static bool IsRecognizableGameRootContent(string path)
		{
			if (String.IsNullOrWhiteSpace(path))
				return false;

			string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
			if (normalizedPath.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
				normalizedPath.StartsWith("Data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			string fileName = Path.GetFileName(normalizedPath);
			return fileName.Equals("skse64_loader.exe", StringComparison.OrdinalIgnoreCase) ||
				(fileName.StartsWith("skse64_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) ||
				fileName.Equals("f4se_loader.exe", StringComparison.OrdinalIgnoreCase) ||
				(fileName.StartsWith("f4se_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) ||
				fileName.Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase) ||
				fileName.Equals("enbseries.ini", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsUnsafeGameRootArchivePath(string path)
		{
			if (String.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
				return true;

			string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return normalizedPath.Split(Path.DirectorySeparatorChar).Any(part => part == "..");
		}

		private static string NormalizeGameRootRelativePath(string path)
		{
			if (String.IsNullOrWhiteSpace(path))
				return String.Empty;

			if (Path.IsPathRooted(path))
				throw new InvalidDataException(String.Format("Game-root install path '{0}' is rooted and cannot be installed safely.", path));

			var pathParts = new List<string>();
			foreach (string part in path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar))
			{
				if (String.IsNullOrEmpty(part) || part == ".")
					continue;
				if (part == "..")
					throw new InvalidDataException(String.Format("Game-root install path '{0}' escapes the selected game root.", path));
				pathParts.Add(part);
			}

			return String.Join(Path.DirectorySeparatorChar.ToString(), pathParts.ToArray());
		}
	}
}
