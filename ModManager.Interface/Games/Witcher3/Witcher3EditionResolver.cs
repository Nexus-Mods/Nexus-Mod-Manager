using System;

namespace Nexus.Client.Games.Witcher3
{
	/// <summary>Edition family identified from executable evidence, not from the selected NMM ModeId.</summary>
	public enum Witcher3Edition
	{
		Ambiguous = 0,
		Legacy = 1,
		Remastered = 2
	}

	/// <summary>Why the evidence did or did not establish one edition.</summary>
	public enum Witcher3EditionReason
	{
		MissingExecutable = 0,
		UnversionedDx12Only = 1,
		ConflictingEvidence = 2,
		UnreadableVersion = 3,
		LegacyDx11Executable = 4,
		LegacyExecutableVersion = 5,
		RemasteredExecutableVersion = 6
	}

	/// <summary>
	/// Immutable, read-only observations from one game installation. Missing version information
	/// is not a claim that the executable belongs to an earlier game release.
	/// </summary>
	public sealed class Witcher3EditionEvidence
	{
		public Witcher3EditionEvidence(bool hasDx11Executable, bool hasDx12Executable,
			int? dx11MajorVersion, int? dx12MajorVersion, bool hasUnreadableVersion = false)
		{
			if (dx11MajorVersion.HasValue && (!hasDx11Executable || dx11MajorVersion.Value < 1))
				throw new ArgumentOutOfRangeException(nameof(dx11MajorVersion));
			if (dx12MajorVersion.HasValue && (!hasDx12Executable || dx12MajorVersion.Value < 1))
				throw new ArgumentOutOfRangeException(nameof(dx12MajorVersion));

			HasDx11Executable = hasDx11Executable;
			HasDx12Executable = hasDx12Executable;
			Dx11MajorVersion = dx11MajorVersion;
			Dx12MajorVersion = dx12MajorVersion;
			HasUnreadableVersion = hasUnreadableVersion;
		}

		public bool HasDx11Executable { get; }
		public bool HasDx12Executable { get; }
		public int? Dx11MajorVersion { get; }
		public int? Dx12MajorVersion { get; }
		public bool HasUnreadableVersion { get; }
	}

	/// <summary>A conservative edition decision suitable for gating future mutating features.</summary>
	public sealed class Witcher3EditionResolution
	{
		internal Witcher3EditionResolution(Witcher3Edition edition, Witcher3EditionReason reason)
		{
			Edition = edition;
			Reason = reason;
		}

		public Witcher3Edition Edition { get; }
		public Witcher3EditionReason Reason { get; }
		public bool IsConclusive => Edition != Witcher3Edition.Ambiguous;
	}

	/// <summary>
	/// Pure edition classification. A 4.x installation may contain a DX12 executable; that
	/// executable alone must never classify it as Remastered. Do not use this to infer
	/// compatibility of installed mods or ownership of a physical game directory.
	/// </summary>
	public static class Witcher3EditionResolver
	{
		public const string Dx11Executable = @"bin\x64\witcher3.exe";
		public const string Dx12Executable = @"bin\x64_dx12\witcher3.exe";
		public const int RemasteredMajorVersion = 5;

		public static Witcher3EditionResolution Resolve(Witcher3EditionEvidence evidence)
		{
			if (evidence == null)
				throw new ArgumentNullException(nameof(evidence));

			if (!evidence.HasDx11Executable && !evidence.HasDx12Executable)
				return Result(Witcher3Edition.Ambiguous, Witcher3EditionReason.MissingExecutable);
			if (evidence.HasUnreadableVersion)
				return Result(Witcher3Edition.Ambiguous, Witcher3EditionReason.UnreadableVersion);

			// An executable of a newer edition alongside the old DX11 binary means that the
			// root could be an incomplete upgrade or a mixed install. Never choose for the user.
			if ((evidence.Dx11MajorVersion ?? 0) >= RemasteredMajorVersion ||
				(evidence.HasDx11Executable && (evidence.Dx12MajorVersion ?? 0) >= RemasteredMajorVersion))
				return Result(Witcher3Edition.Ambiguous, Witcher3EditionReason.ConflictingEvidence);

			if ((evidence.Dx12MajorVersion ?? 0) >= RemasteredMajorVersion)
				return Result(Witcher3Edition.Remastered, Witcher3EditionReason.RemasteredExecutableVersion);

			if (evidence.HasDx11Executable)
				return Result(Witcher3Edition.Legacy, Witcher3EditionReason.LegacyDx11Executable);

			if (evidence.Dx12MajorVersion.HasValue)
				return Result(Witcher3Edition.Legacy, Witcher3EditionReason.LegacyExecutableVersion);

			// The absence of DX11 is useful corroboration, but not sufficient evidence
			// that an unversioned DX12-only installation is a valid 5.x game.
			return Result(Witcher3Edition.Ambiguous, Witcher3EditionReason.UnversionedDx12Only);
		}

		private static Witcher3EditionResolution Result(Witcher3Edition edition, Witcher3EditionReason reason)
		{
			return new Witcher3EditionResolution(edition, reason);
		}
	}
}
