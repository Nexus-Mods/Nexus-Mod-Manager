using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Operation-local handoff of exact reviewed basic mappings to the native worker.
	/// This is not ownership authorization; the caller retains its existing native mutation lease.
	/// </summary>
	public sealed class PreparedBasicInstallMapping
	{
		private readonly IMod _mod;
		private readonly IGameMode _gameMode;
		private readonly string _modeId;
		private readonly string _archivePath;
		private readonly string _physicalRoot;
		private readonly BasicInstallPlan _plan;

		/// <summary>Binds the immutable native file plan to the current archive, mode and physical Data root.</summary>
		private PreparedBasicInstallMapping(IMod mod, IGameMode gameMode, BasicInstallPlan plan)
		{
			_mod = mod;
			_gameMode = gameMode;
			_modeId = gameMode.ModeId;
			_archivePath = mod.Filename;
			_physicalRoot = GetDataRoot(gameMode);
			_plan = plan;
		}

		/// <summary>Runs the configured basic mapping once after native file selection, using existing ignore rules.</summary>
		public static PreparedBasicInstallMapping Prepare(IMod mod, IGameMode gameMode, ModInstallContext context,
			IEnumerable<KeyValuePair<string, string>> selectedFiles)
		{
			if (mod == null) throw new ArgumentNullException(nameof(mod));
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			var provider = gameMode as IReviewedBasicInstallMappingProvider;
			if (provider == null) throw new InvalidOperationException("The game mode does not expose reviewed basic mapping.");
			IEnumerable<KeyValuePair<string, string>> selected = selectedFiles ??
				(mod.GetFileList() ?? throw new InvalidDataException("The archive file inventory is unavailable."))
				.Select(path => new KeyValuePair<string, string>(path, null));
			selected = selected.Where(file => !ModInstallFileFilter.IsIgnored(file.Key) && !ModInstallFileFilter.IsIgnored(file.Value));
			return Capture(mod, gameMode, context, provider.MapBasicInstallFiles(mod, context, selected));
		}

		/// <summary>Captures mappings already reviewed by the caller without repeating new-install or upgrade classification.</summary>
		public static PreparedBasicInstallMapping Capture(IMod mod, IGameMode gameMode, ModInstallContext context,
			IEnumerable<KeyValuePair<string, string>> reviewedFiles)
		{
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			if (!(gameMode is IReviewedBasicInstallMappingProvider) || gameMode.RequiresSpecialFileInstallation || gameMode.RequiresModFileMerge ||
				(gameMode as IDeterministicPackageFilePlanProvider)?.HasPackageFilePlan == true)
				throw new InvalidOperationException("Prepared basic mappings require an ordinary reviewed file-only game mode.");
			ManagedContentInstallSelection selection = ManagedContentInstallSelection.Create(mod, context, reviewedFiles, gameMode.ModeId);
			var files = new List<BasicInstallPlanFile>();
			var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var targets = new HashSet<ModDeploymentTarget>();
			foreach (KeyValuePair<string, string> file in selection.Files)
			{
				string[] parts;
				if (!ManagedContentPath.TryParse(file.Value, out parts) ||
					!String.Equals(file.Value, String.Join("\\", parts), StringComparison.Ordinal) ||
					ModInstallFileFilter.IsIgnored(file.Key) || ModInstallFileFilter.IsIgnored(file.Value))
					throw new InvalidDataException("A prepared basic destination must be explicit, canonical and installable: " + file.Value);
				string gamePath = BasicInstallPlanBuilder.GetAdjustedPath(gameMode, mod, context.InstallRoot, file.Value, ModPathContext.GameInstall);
				string virtualPath = context.Method == ModInstallMethod.Virtual
					? BasicInstallPlanBuilder.GetAdjustedPath(gameMode, mod, context.InstallRoot, file.Value, ModPathContext.VirtualStorage) : null;
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(gameMode, mod, file.Value, context.InstallRoot);
				if (!sources.Add(file.Key.Replace('/', '\\')) || !targets.Add(target) ||
					(context.Method == ModInstallMethod.Virtual && String.IsNullOrWhiteSpace(virtualPath)))
					throw new InvalidDataException("A prepared basic mapping has a repeated source/target or an unavailable Virtual path.");
				files.Add(new BasicInstallPlanFile(file.Key, file.Value, gamePath, virtualPath, target));
			}
			return new PreparedBasicInstallMapping(mod, gameMode, new BasicInstallPlan(context, files));
		}

		/// <summary>Rechecks archive/context and path projections before either native worker uses the frozen mappings.</summary>
		public IReadOnlyList<KeyValuePair<string, string>> GetFiles(IMod mod, IGameMode gameMode, ModInstallContext context)
		{
			if (context == null || !Object.ReferenceEquals(mod, _mod) || !Object.ReferenceEquals(gameMode, _gameMode) ||
				!String.Equals(gameMode.ModeId, _modeId, StringComparison.Ordinal) ||
				gameMode.RequiresSpecialFileInstallation || gameMode.RequiresModFileMerge ||
				(gameMode as IDeterministicPackageFilePlanProvider)?.HasPackageFilePlan == true ||
				context.Method != _plan.InstallContext.Method || context.InstallRoot != _plan.InstallContext.InstallRoot ||
				!String.Equals(mod.Filename, _archivePath, StringComparison.OrdinalIgnoreCase) ||
				!String.Equals(GetDataRoot(gameMode), _physicalRoot, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Prepared basic files belong to a different archive, game root or install context.");
			var mappings = new List<KeyValuePair<string, string>>(_plan.Files.Count);
			foreach (BasicInstallPlanFile file in _plan.Files)
			{
				string gamePath = BasicInstallPlanBuilder.GetAdjustedPath(gameMode, mod, context.InstallRoot, file.DestinationPath, ModPathContext.GameInstall);
				string virtualPath = context.Method == ModInstallMethod.Virtual
					? BasicInstallPlanBuilder.GetAdjustedPath(gameMode, mod, context.InstallRoot, file.DestinationPath, ModPathContext.VirtualStorage) : null;
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(gameMode, mod, file.DestinationPath, context.InstallRoot);
				if (!String.Equals(gamePath, file.GameInstallPath, StringComparison.Ordinal) ||
					!String.Equals(virtualPath, file.VirtualStoragePath, StringComparison.Ordinal) || !target.Equals(file.DeploymentTarget))
					throw new InvalidDataException("A prepared basic destination changed during native path projection: " + file.DestinationPath);
				mappings.Add(new KeyValuePair<string, string>(file.SourcePath, file.DestinationPath));
			}
			// This only rechecks source membership; it does not call the game layout mapper again.
			return ManagedContentInstallSelection.Create(mod, context, mappings, gameMode.ModeId).Files;
		}

		/// <summary>Reads the configured native Data root without creating or enumerating any directories.</summary>
		private static string GetDataRoot(IGameMode gameMode)
		{
			string root = ModDeploymentTargetResolver.GetPhysicalRootPath(gameMode, ModDeploymentRoot.Data);
			if (!Path.IsPathRooted(root)) throw new InvalidDataException("Prepared basic mapping requires an absolute native Data root.");
			return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
	}
}
