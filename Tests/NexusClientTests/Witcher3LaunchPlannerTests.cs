using System;
using System.IO;
using Nexus.Client.Games.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3LaunchPlannerTests
	{
		[Test]
		public void LegacyWithDx11AndDx12_PrefersActualDx11Executable()
		{
			string root = FixtureRoot();
			Witcher3LaunchDecision result = Witcher3LaunchPlanner.Evaluate(root, root,
				Witcher3Edition.Legacy, Evidence(true, true, 4, 4));
			Assert.That(result.CanLaunch, Is.True);
			Assert.That(result.ExecutablePath,
				Is.EqualTo(Path.Combine(root, Witcher3EditionResolver.Dx11Executable)));
		}

		[Test]
		public void LegacyDx12OnlyWithConfirmedFourX_UsesDx12RatherThanMissingDx11()
		{
			string root = FixtureRoot();
			Witcher3LaunchDecision result = Witcher3LaunchPlanner.Evaluate(root, root,
				Witcher3Edition.Legacy, Evidence(false, true, null, 4));
			Assert.That(result.CanLaunch, Is.True);
			Assert.That(result.ExecutablePath,
				Is.EqualTo(Path.Combine(root, Witcher3EditionResolver.Dx12Executable)));
		}

		[Test]
		public void RemasteredWithFiveXAndNoDx11_UsesOnlyDx12()
		{
			string root = FixtureRoot();
			Witcher3LaunchDecision result = Witcher3LaunchPlanner.Evaluate(root, root,
				Witcher3Edition.Remastered, Evidence(false, true, null, 5));
			Assert.That(result.CanLaunch, Is.True);
			Assert.That(result.ExecutablePath,
				Is.EqualTo(Path.Combine(root, Witcher3EditionResolver.Dx12Executable)));
		}

		[Test]
		public void LegacyModeMustNotLaunchFiveXAfterGameUpgraded()
		{
			string root = FixtureRoot();
			Witcher3LaunchDecision result = Witcher3LaunchPlanner.Evaluate(root, root,
				Witcher3Edition.Legacy, Evidence(false, true, null, 5));
			AssertBlocked(result, Witcher3LaunchBlockReason.WrongOrAmbiguousEdition);
		}

		[Test]
		public void RemasteredModeMustNotLaunchFourXDx12()
		{
			string root = FixtureRoot();
			Witcher3LaunchDecision result = Witcher3LaunchPlanner.Evaluate(root, root,
				Witcher3Edition.Remastered, Evidence(true, true, 4, 4));
			AssertBlocked(result, Witcher3LaunchBlockReason.WrongOrAmbiguousEdition);
		}

		[TestCase(Witcher3Edition.Legacy)]
		[TestCase(Witcher3Edition.Remastered)]
		public void MixedDx11AndFiveXDx12_NeverLaunches(Witcher3Edition requested)
		{
			string root = FixtureRoot();
			AssertBlocked(Witcher3LaunchPlanner.Evaluate(root, root, requested,
				Evidence(true, true, 4, 5)), Witcher3LaunchBlockReason.WrongOrAmbiguousEdition);
		}

		[TestCase(Witcher3Edition.Legacy)]
		[TestCase(Witcher3Edition.Remastered)]
		public void UnversionedDx12Only_NeverLaunchesUnderEditionGate(Witcher3Edition requested)
		{
			string root = FixtureRoot();
			AssertBlocked(Witcher3LaunchPlanner.Evaluate(root, root, requested,
				Evidence(false, true, null, null)), Witcher3LaunchBlockReason.WrongOrAmbiguousEdition);
		}

		[TestCase(Witcher3Edition.Legacy)]
		[TestCase(Witcher3Edition.Remastered)]
		public void UnreadableExecutableVersion_NeverLaunches(Witcher3Edition requested)
		{
			string root = FixtureRoot();
			AssertBlocked(Witcher3LaunchPlanner.Evaluate(root, root, requested,
				new Witcher3EditionEvidence(false, true, null, 5, true)),
				Witcher3LaunchBlockReason.WrongOrAmbiguousEdition);
		}

		[Test]
		public void StaleExecutableRoot_BlocksEvenIfItsExeLooksValid()
		{
			string root = FixtureRoot();
			string otherRoot = Path.Combine(root, "other");
			Witcher3LaunchDecision result = Witcher3LaunchPlanner.Evaluate(root, otherRoot,
				Witcher3Edition.Remastered, Evidence(false, true, null, 5));
			AssertBlocked(result, Witcher3LaunchBlockReason.ExecutableRootMismatch);
		}

		[Test]
		public void NormalizedEquivalentPaths_AreAccepted()
		{
			string root = FixtureRoot();
			Witcher3LaunchDecision result = Witcher3LaunchPlanner.Evaluate(root,
				root + Path.DirectorySeparatorChar, Witcher3Edition.Remastered,
				Evidence(false, true, null, 5));
			Assert.That(result.CanLaunch, Is.True);
		}

		[Test]
		public void MissingOrRelativeGameRoot_FailsClosed()
		{
			string root = FixtureRoot();
			AssertBlocked(Witcher3LaunchPlanner.Evaluate(null, root,
				Witcher3Edition.Legacy, Evidence(true, false, 4, null)),
				Witcher3LaunchBlockReason.InvalidGameRoot);
			AssertBlocked(Witcher3LaunchPlanner.Evaluate("relative-game", root,
				Witcher3Edition.Legacy, Evidence(true, false, 4, null)),
				Witcher3LaunchBlockReason.InvalidGameRoot);
			AssertBlocked(Witcher3LaunchPlanner.Evaluate(root, null,
				Witcher3Edition.Legacy, Evidence(true, false, 4, null)),
				Witcher3LaunchBlockReason.ExecutableRootMismatch);
		}

		[Test]
		public void MissingExecutableEvidence_DoesNotInventALaunchTarget()
		{
			string root = FixtureRoot();
			AssertBlocked(Witcher3LaunchPlanner.Evaluate(root, root,
				Witcher3Edition.Legacy, Evidence(false, false, null, null)),
				Witcher3LaunchBlockReason.ExecutableMissing);
		}

		[Test]
		public void AbsentEvidence_DoesNotInventALaunchTarget()
		{
			string root = FixtureRoot();
			AssertBlocked(Witcher3LaunchPlanner.Evaluate(root, root,
				Witcher3Edition.Remastered, null), Witcher3LaunchBlockReason.InspectionFailed);
		}

		[Test]
		public void RealLaunchReview_ReprobesAndRejectsMissingGameWithoutCreatingFiles()
		{
			string root = Path.Combine(Path.GetTempPath(), "w3r9b-missing-" + Guid.NewGuid().ToString("N"));
			Assert.That(Directory.Exists(root), Is.False);
			AssertBlocked(Witcher3LaunchPlanner.ReviewCurrentInstallation(root, root,
				Witcher3Edition.Remastered), Witcher3LaunchBlockReason.ExecutableMissing);
			Assert.That(Directory.Exists(root), Is.False);
		}

		[Test]
		public void RealLaunchReview_UnversionedDx12Fixture_IsBlockedOnEachAttempt()
		{
			string root = Path.Combine(Path.GetTempPath(), "w3r9b-probe-" + Guid.NewGuid().ToString("N"));
			string bin = Path.Combine(root, "bin", "x64_dx12");
			Directory.CreateDirectory(bin);
			try
			{
				string executable = Path.Combine(bin, "witcher3.exe");
				File.WriteAllBytes(executable, new byte[] { 1, 2, 3 });
				AssertBlocked(Witcher3LaunchPlanner.ReviewCurrentInstallation(root, root,
					Witcher3Edition.Remastered), Witcher3LaunchBlockReason.WrongOrAmbiguousEdition);
				File.Delete(executable);
				AssertBlocked(Witcher3LaunchPlanner.ReviewCurrentInstallation(root, root,
					Witcher3Edition.Remastered), Witcher3LaunchBlockReason.ExecutableMissing);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void NoThirdEditionMayBeRequested()
		{
			string root = FixtureRoot();
			Assert.Throws<ArgumentOutOfRangeException>(() => Witcher3LaunchPlanner.Evaluate(root, root,
				Witcher3Edition.Ambiguous, Evidence(false, true, null, 5)));
		}

		private static Witcher3EditionEvidence Evidence(bool dx11, bool dx12, int? major11, int? major12)
		{
			return new Witcher3EditionEvidence(dx11, dx12, major11, major12);
		}

		private static string FixtureRoot()
		{
			return Path.Combine(Path.GetTempPath(), "w3r9b-launch-policy-fixture");
		}

		private static void AssertBlocked(Witcher3LaunchDecision result, Witcher3LaunchBlockReason reason)
		{
			Assert.That(result.CanLaunch, Is.False);
			Assert.That(result.BlockReason, Is.EqualTo(reason));
			Assert.That(result.ExecutablePath, Is.Null);
		}
	}
}
