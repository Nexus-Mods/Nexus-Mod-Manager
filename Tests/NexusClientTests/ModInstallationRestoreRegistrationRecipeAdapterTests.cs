using System;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies the C7.10a registration-only native restore recipe boundary.</summary>
	[TestFixture]
	public class ModInstallationRestoreRegistrationRecipeAdapterTests
	{
		/// <summary>Local restore translates to exactly one side-effect-free registration marker.</summary>
		[Test]
		public void Translate_LocalRestoreProducesRegistrationOnlyPlan()
		{
			ModInstallationRecipeInput input = CreateInput(ModOperationOrigin.LocalRestore, new ModInstallationRecipePath[0]);

			ModInstallationRecipeInput translated = new ModInstallationRestoreRegistrationRecipeAdapter().Translate(input);

			Assert.That(translated.OperationIdentity, Is.SameAs(input.OperationIdentity));
			Assert.That(translated.Validation, Is.SameAs(input.Validation));
			Assert.That(translated.NativeOperations.Count, Is.EqualTo(1));
			Assert.That(translated.NativeOperations.Single(), Is.TypeOf<RestoreNativeRegistrationOperation>());
		}

		/// <summary>Ordinary additive Collection recipes cannot use the restore-only registration primitive.</summary>
		[Test]
		public void Translate_RejectsCollectionOrigin()
		{
			ModInstallationRecipeInput input = CreateInput(ModOperationOrigin.Collection, new ModInstallationRecipePath[0]);

			Assert.Throws<InvalidDataException>(() => new ModInstallationRestoreRegistrationRecipeAdapter().Translate(input));
		}

		/// <summary>Registration-only restore recipes cannot smuggle file mutations through declared paths.</summary>
		[Test]
		public void Translate_RejectsDeclaredPaths()
		{
			ModInstallationRecipeInput input = CreateInput(ModOperationOrigin.LocalRestore, new[]
			{
				new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"Data\unexpected.bin")
			});

			Assert.Throws<InvalidDataException>(() => new ModInstallationRestoreRegistrationRecipeAdapter().Translate(input));
		}

		/// <summary>Creates one exact validation/input pair for restore-registration adapter tests.</summary>
		private static ModInstallationRecipeInput CreateInput(ModOperationOrigin origin, ModInstallationRecipePath[] paths)
		{
			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var operation = ModOperationIdentity.CreateNew(origin,
				new ModOperationFingerprint("target-sha256:" + new string('a', 64), context, "recipe:c7.10a-registration-v1"));
			var validation = new ModInstallationRecipeValidation(
				ModInstallationRestoreRegistrationRecipeAdapter.AdapterId,
				ModInstallationRestoreRegistrationRecipeAdapter.AdapterVersion, context,
				new ModInstallationRecipeExpectedContent(new string('b', 64), 4096),
				new[] { new ModInstallationRecipeCapability(ModInstallationRestoreRegistrationRecipeAdapter.CapabilityId,
					ModInstallationRestoreRegistrationRecipeAdapter.CapabilityVersion) }, paths);
			return new ModInstallationRecipeInput(operation, validation);
		}
	}
}
