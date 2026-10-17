using System;
using Nexus.Client.Games.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Read-only edition classification; no installed game or network account is required.</summary>
	[TestFixture]
	public class Witcher3EditionResolverTests
	{
		[TestCase(1)]
		[TestCase(3)]
		[TestCase(4)]
		public void LegacyDx11WithDx12_DoesNotBecomeRemastered(int dx12Major)
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(true, true, 4, dx12Major));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Legacy));
			Assert.That(result.IsConclusive, Is.True);
		}

		[Test]
		public void LegacyDx11WithoutVersion_RemainsLegacy()
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(true, false, null, null));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Legacy));
		}

		[Test]
		public void RemasteredDx12OnlyWithVersionFive_IsRemastered()
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(false, true, null, 5));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Remastered));
			Assert.That(result.Reason, Is.EqualTo(Witcher3EditionReason.RemasteredExecutableVersion));
		}

		[Test]
		public void LegacyDx12OnlyWithVersionFour_IsNotRemastered()
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(false, true, null, 4));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Legacy));
		}

		[Test]
		public void UnversionedDx12Only_IsAmbiguous()
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(false, true, null, null));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Ambiguous));
			Assert.That(result.Reason, Is.EqualTo(Witcher3EditionReason.UnversionedDx12Only));
			Assert.That(result.IsConclusive, Is.False);
		}

		[Test]
		public void StaleDx11AlongsideVersionFiveDx12_IsAmbiguous()
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(true, true, 4, 5));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Ambiguous));
			Assert.That(result.Reason, Is.EqualTo(Witcher3EditionReason.ConflictingEvidence));
		}

		[Test]
		public void NewDx11ExecutableWithoutDx12_IsNotAcceptedAsLegacy()
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(true, false, 5, null));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Ambiguous));
		}

		[Test]
		public void MissingExecutables_IsAmbiguous()
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(false, false, null, null));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Ambiguous));
			Assert.That(result.Reason, Is.EqualTo(Witcher3EditionReason.MissingExecutable));
		}

		[Test]
		public void UnreadableVersion_IsNotSilentlyInterpretedAsLegacy()
		{
			Witcher3EditionResolution result = Witcher3EditionResolver.Resolve(
				new Witcher3EditionEvidence(true, true, null, 4, true));
			Assert.That(result.Edition, Is.EqualTo(Witcher3Edition.Ambiguous));
			Assert.That(result.Reason, Is.EqualTo(Witcher3EditionReason.UnreadableVersion));
		}

		[Test]
		public void InvalidEvidenceAndNullInput_AreRejected()
		{
			Assert.Throws<ArgumentNullException>(() => Witcher3EditionResolver.Resolve(null));
			Assert.Throws<ArgumentOutOfRangeException>(() => new Witcher3EditionEvidence(false, true, 4, 4));
			Assert.Throws<ArgumentOutOfRangeException>(() => new Witcher3EditionEvidence(false, true, null, 0));
		}
	}
}
