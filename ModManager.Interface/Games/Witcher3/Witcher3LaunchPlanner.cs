using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.Witcher3
{
	/// <summary>The reason a launch was refused after reviewing the current game root.</summary>
	public enum Witcher3LaunchBlockReason
	{
		None = 0,
		InvalidGameRoot = 1,
		ExecutableRootMismatch = 2,
		WrongOrAmbiguousEdition = 3,
		ExecutableMissing = 4,
		InspectionFailed = 5
	}

	/// <summary>A path-only launch decision. This does not authorize native mod mutations.</summary>
	public sealed class Witcher3LaunchDecision
	{
		internal Witcher3LaunchDecision(Witcher3LaunchBlockReason reason, string executablePath)
		{
			BlockReason = reason;
			ExecutablePath = executablePath;
		}

		public Witcher3LaunchBlockReason BlockReason { get; }
		public string ExecutablePath { get; }
		public bool CanLaunch => BlockReason == Witcher3LaunchBlockReason.None &&
			!String.IsNullOrWhiteSpace(ExecutablePath);

		public string GetUserMessage()
		{
			switch (BlockReason)
			{
				case Witcher3LaunchBlockReason.InvalidGameRoot:
					return LanguageManager.Get("GameModes.Witcher3.Launch.InvalidRoot",
						"The Witcher 3 installation path is invalid. Re-select the game installation before launching.");
				case Witcher3LaunchBlockReason.ExecutableRootMismatch:
					return LanguageManager.Get("GameModes.Witcher3.Launch.RootMismatch",
						"The Witcher 3 executable path no longer matches the selected game installation. Re-run game detection.");
				case Witcher3LaunchBlockReason.WrongOrAmbiguousEdition:
					return LanguageManager.Get("GameModes.Witcher3.Launch.EditionMismatch",
						"The Witcher 3 installation no longer matches this NMM game edition. Check the game version and re-run detection.");
				case Witcher3LaunchBlockReason.ExecutableMissing:
					return LanguageManager.Get("GameModes.Witcher3.Launch.ExecutableMissing",
						"The Witcher 3 executable is missing. Verify the game installation or re-run detection.");
				case Witcher3LaunchBlockReason.InspectionFailed:
					return LanguageManager.Get("GameModes.Witcher3.Launch.InspectionFailed",
						"NMM could not verify this Witcher 3 installation. Check file access and re-run detection.");
				default:
					return String.Empty;
			}
		}
	}

	/// <summary>
	/// Read-only, edition-specific launch selection. The configured installation and executable
	/// roots must agree. Each real launch re-probes executable version evidence: a DX12 binary
	/// existing in a 4.x installation must never be mistaken for Remastered 5.x.
	/// </summary>
	public static class Witcher3LaunchPlanner
	{
		public static Witcher3LaunchDecision ReviewCurrentInstallation(string gameRoot,
			string executableRoot, Witcher3Edition requestedEdition)
		{
			Witcher3LaunchDecision rootDecision = ReviewRoots(gameRoot, executableRoot, requestedEdition);
			if (rootDecision != null)
				return rootDecision;

			try
			{
				Witcher3InstallationObservation current = Witcher3InstallationProbe.Inspect(gameRoot);
				Witcher3LaunchDecision result = Evaluate(gameRoot, executableRoot,
					requestedEdition, current.Evidence);
				if (result.CanLaunch && !File.Exists(result.ExecutablePath))
					return Block(Witcher3LaunchBlockReason.ExecutableMissing);
				return result;
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is SecurityException || error is ArgumentException || error is NotSupportedException ||
				error is Win32Exception)
			{
				return Block(Witcher3LaunchBlockReason.InspectionFailed);
			}
		}

		/// <summary>
		/// Pure evidence-to-executable mapping for deterministic characterization tests.
		/// Real launching must call ReviewCurrentInstallation, never trust cached evidence.
		/// </summary>
		public static Witcher3LaunchDecision Evaluate(string gameRoot, string executableRoot,
			Witcher3Edition requestedEdition, Witcher3EditionEvidence evidence)
		{
			Witcher3LaunchDecision rootDecision = ReviewRoots(gameRoot, executableRoot, requestedEdition);
			if (rootDecision != null)
				return rootDecision;
			if (evidence == null)
				return Block(Witcher3LaunchBlockReason.InspectionFailed);
			if (!evidence.HasDx11Executable && !evidence.HasDx12Executable)
				return Block(Witcher3LaunchBlockReason.ExecutableMissing);
			if (Witcher3EditionResolver.Resolve(evidence).Edition != requestedEdition)
				return Block(Witcher3LaunchBlockReason.WrongOrAmbiguousEdition);

			string executable = requestedEdition == Witcher3Edition.Remastered
				? Witcher3EditionResolver.Dx12Executable
				: evidence.HasDx11Executable ? Witcher3EditionResolver.Dx11Executable
					: Witcher3EditionResolver.Dx12Executable;
			if (requestedEdition == Witcher3Edition.Remastered ? !evidence.HasDx12Executable :
				!evidence.HasDx11Executable && !evidence.HasDx12Executable)
				return Block(Witcher3LaunchBlockReason.ExecutableMissing);

			return new Witcher3LaunchDecision(Witcher3LaunchBlockReason.None,
				Path.Combine(Path.GetFullPath(gameRoot), executable));
		}

		private static Witcher3LaunchDecision ReviewRoots(string gameRoot, string executableRoot,
			Witcher3Edition requestedEdition)
		{
			if (requestedEdition != Witcher3Edition.Legacy && requestedEdition != Witcher3Edition.Remastered)
				throw new ArgumentOutOfRangeException(nameof(requestedEdition));
			if (String.IsNullOrWhiteSpace(gameRoot))
				return Block(Witcher3LaunchBlockReason.InvalidGameRoot);
			if (String.IsNullOrWhiteSpace(executableRoot))
				return Block(Witcher3LaunchBlockReason.ExecutableRootMismatch);

			try
			{
				if (!Path.IsPathRooted(gameRoot))
					return Block(Witcher3LaunchBlockReason.InvalidGameRoot);
				if (!Path.IsPathRooted(executableRoot))
					return Block(Witcher3LaunchBlockReason.ExecutableRootMismatch);
				string installation = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string executable = Path.GetFullPath(executableRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				if (!String.Equals(installation, executable, StringComparison.OrdinalIgnoreCase))
					return Block(Witcher3LaunchBlockReason.ExecutableRootMismatch);
			}
			catch (Exception error) when (error is ArgumentException || error is NotSupportedException ||
				error is PathTooLongException || error is SecurityException)
			{
				return Block(Witcher3LaunchBlockReason.InvalidGameRoot);
			}
			return null;
		}

		private static Witcher3LaunchDecision Block(Witcher3LaunchBlockReason reason)
		{
			return new Witcher3LaunchDecision(reason, null);
		}
	}
}
