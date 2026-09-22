using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies the immutable native installation-recipe input and C5.3 validation-boundary invariants.
	/// </summary>
	[TestFixture]
	public class ModInstallationRecipeInputTests
	{
		/// <summary>
		/// Verifies that supported recipe operation scopes preserve one coherent target, method/root and validation record.
		/// </summary>
		[TestCase(ModOperationOrigin.Collection, ModInstallMethod.Virtual, ModInstallRoot.Data)]
		[TestCase(ModOperationOrigin.LocalRestore, ModInstallMethod.Direct, ModInstallRoot.GameRoot)]
		[TestCase(ModOperationOrigin.Recovery, ModInstallMethod.Virtual, ModInstallRoot.GameRoot)]
		public void Constructor_CapturesValidatedNativeOperationContext(ModOperationOrigin p_mooOrigin,
			ModInstallMethod p_mimMethod, ModInstallRoot p_mirRoot)
		{
			ModOperationIdentity operation = CreateOperation(p_mooOrigin, p_mimMethod, p_mirRoot, "recipe:exact-v1");
			ModInstallationRecipeValidation validation = CreateValidation(p_mimMethod, p_mirRoot);

			var input = new ModInstallationRecipeInput(operation, validation);

			Assert.That(input.OperationIdentity, Is.SameAs(operation));
			Assert.That(input.TargetFingerprint, Is.EqualTo(operation.Fingerprint.TargetFingerprint));
			Assert.That(input.InstallContext.Method, Is.EqualTo(p_mimMethod));
			Assert.That(input.InstallContext.InstallRoot, Is.EqualTo(p_mirRoot));
			Assert.That(input.RecipeFingerprint, Is.EqualTo("recipe:exact-v1"));
			Assert.That(input.Validation, Is.SameAs(validation));
		}

		/// <summary>
		/// Verifies that an explicit recipe request cannot silently fall back to an ordinary recipe-less operation.
		/// </summary>
		[Test]
		public void Constructor_RejectsMissingRecipeIdentity()
		{
			ModOperationIdentity operation = CreateOperation(ModOperationOrigin.Collection, ModInstallMethod.Virtual, ModInstallRoot.Data, null);
			ModInstallationRecipeValidation validation = CreateValidation(ModInstallMethod.Virtual, ModInstallRoot.Data);

			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeInput(operation, validation));
		}

		/// <summary>
		/// Verifies that recipe identity is canonical rather than silently trimmed or normalized at the native boundary.
		/// </summary>
		[Test]
		public void Constructor_RejectsNonCanonicalRecipeIdentity()
		{
			ModOperationIdentity operation = CreateOperation(ModOperationOrigin.Collection, ModInstallMethod.Virtual, ModInstallRoot.Data, " recipe:v1 ");
			ModInstallationRecipeValidation validation = CreateValidation(ModInstallMethod.Virtual, ModInstallRoot.Data);

			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeInput(operation, validation));
		}

		/// <summary>
		/// Verifies recipe-bearing native input is limited to the explicitly supported Collection-family operation scopes.
		/// </summary>
		[TestCase(ModOperationOrigin.Manual)]
		[TestCase(ModOperationOrigin.Profile)]
		public void Constructor_RejectsNonRecipeOperationScope(ModOperationOrigin p_mooOrigin)
		{
			ModOperationIdentity operation = CreateOperation(p_mooOrigin, ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe:v1");
			ModInstallationRecipeValidation validation = CreateValidation(ModInstallMethod.Virtual, ModInstallRoot.Data);

			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeInput(operation, validation));
		}

		/// <summary>
		/// Verifies the recipe validation method/root must match the immutable native operation fingerprint exactly.
		/// </summary>
		[TestCase(ModInstallMethod.Direct, ModInstallRoot.Data)]
		[TestCase(ModInstallMethod.Virtual, ModInstallRoot.GameRoot)]
		public void Constructor_RejectsMismatchedValidatedInstallContext(ModInstallMethod p_mimValidationMethod,
			ModInstallRoot p_mirValidationRoot)
		{
			ModOperationIdentity operation = CreateOperation(ModOperationOrigin.Collection, ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe:v1");
			ModInstallationRecipeValidation validation = CreateValidation(p_mimValidationMethod, p_mirValidationRoot);

			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeInput(operation, validation));
		}

		/// <summary>
		/// Verifies the input refuses a missing validation record.
		/// </summary>
		[Test]
		public void Constructor_RejectsNullValidation()
		{
			ModOperationIdentity operation = CreateOperation(ModOperationOrigin.Collection, ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe:v1");

			Assert.Throws<ArgumentNullException>(() => new ModInstallationRecipeInput(operation, null));
		}

		/// <summary>
		/// Verifies the null operation guard at the native recipe boundary.
		/// </summary>
		[Test]
		public void Constructor_RejectsNullOperationIdentity()
		{
			ModInstallationRecipeValidation validation = CreateValidation(ModInstallMethod.Virtual, ModInstallRoot.Data);

			Assert.Throws<ArgumentNullException>(() => new ModInstallationRecipeInput(null, validation));
		}

		/// <summary>
		/// Verifies a reviewed translated recipe can be rebound to the final Collection-family child attempt without changing semantics.
		/// </summary>
		[TestCase(ModOperationOrigin.Collection)]
		[TestCase(ModOperationOrigin.LocalRestore)]
		[TestCase(ModOperationOrigin.Recovery)]
		public void ForOperationIdentity_RebindsOnlyOperationAttempt(ModOperationOrigin p_mooOrigin)
		{
			ModInstallationRecipeInput input = CreateTranslatedInput();
			var reboundIdentity = new ModOperationIdentity(Guid.NewGuid(), Guid.NewGuid(), p_mooOrigin, input.OperationIdentity.Fingerprint);

			ModInstallationRecipeInput rebound = input.ForOperationIdentity(reboundIdentity);

			Assert.That(rebound, Is.Not.SameAs(input));
			Assert.That(rebound.OperationIdentity, Is.SameAs(reboundIdentity));
			Assert.That(input.OperationIdentity, Is.Not.SameAs(reboundIdentity));
			Assert.That(rebound.Validation, Is.SameAs(input.Validation));
			Assert.That(rebound.TargetFingerprint, Is.EqualTo(input.TargetFingerprint));
			Assert.That(rebound.InstallContext.Method, Is.EqualTo(input.InstallContext.Method));
			Assert.That(rebound.InstallContext.InstallRoot, Is.EqualTo(input.InstallContext.InstallRoot));
			Assert.That(rebound.RecipeFingerprint, Is.EqualTo(input.RecipeFingerprint));
			Assert.That(rebound.NativeOperations.Count, Is.EqualTo(input.NativeOperations.Count));
			for (int index = 0; index < input.NativeOperations.Count; index++)
				Assert.That(rebound.NativeOperations[index], Is.SameAs(input.NativeOperations[index]));
		}

		/// <summary>
		/// Verifies one translated recipe instance cannot be rebound repeatedly to different native attempts.
		/// </summary>
		[Test]
		public void ForOperationIdentity_RejectsSecondRebind()
		{
			ModInstallationRecipeInput input = CreateTranslatedInput();
			ModInstallationRecipeInput rebound = input.ForOperationIdentity(input.OperationIdentity.CreateNextAttempt());

			Assert.Throws<InvalidOperationException>(() => rebound.ForOperationIdentity(rebound.OperationIdentity.CreateNextAttempt()));
		}

		/// <summary>
		/// Verifies operation rebinding is available only after a supported adapter has produced the reviewed native plan.
		/// </summary>
		[Test]
		public void ForOperationIdentity_RejectsUntranslatedRecipe()
		{
			ModOperationIdentity operation = CreateOperation(ModOperationOrigin.Collection, ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe:v1");
			var input = new ModInstallationRecipeInput(operation, CreateValidation(ModInstallMethod.Virtual, ModInstallRoot.Data));

			Assert.Throws<InvalidOperationException>(() => input.ForOperationIdentity(operation.CreateNextAttempt()));
		}

		/// <summary>
		/// Verifies rebinding remains limited to the Collection-family operation scopes admitted by explicit recipe input.
		/// </summary>
		[TestCase(ModOperationOrigin.Manual)]
		[TestCase(ModOperationOrigin.Profile)]
		public void ForOperationIdentity_RejectsNonRecipeOperationScope(ModOperationOrigin p_mooOrigin)
		{
			ModInstallationRecipeInput input = CreateTranslatedInput();
			var identity = new ModOperationIdentity(Guid.NewGuid(), Guid.NewGuid(), p_mooOrigin, input.OperationIdentity.Fingerprint);

			Assert.Throws<ArgumentException>(() => input.ForOperationIdentity(identity));
		}

		/// <summary>
		/// Verifies a final child identity cannot alter any semantic fingerprint component of the reviewed translated recipe.
		/// </summary>
		[TestCase("target", ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe:c6.15.6-v1")]
		[TestCase("same", ModInstallMethod.Direct, ModInstallRoot.Data, "recipe:c6.15.6-v1")]
		[TestCase("same", ModInstallMethod.Virtual, ModInstallRoot.GameRoot, "recipe:c6.15.6-v1")]
		[TestCase("same", ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe:changed")]
		public void ForOperationIdentity_RejectsSemanticFingerprintChanges(string p_strTargetVariant,
			ModInstallMethod p_mimMethod, ModInstallRoot p_mirRoot, string p_strRecipeFingerprint)
		{
			ModInstallationRecipeInput input = CreateTranslatedInput();
			string target = p_strTargetVariant == "same" ? input.TargetFingerprint : "target-sha256:" + new string('f', 64);
			var fingerprint = new ModOperationFingerprint(target, new ModInstallContext(p_mimMethod, p_mirRoot), p_strRecipeFingerprint);
			var identity = new ModOperationIdentity(Guid.NewGuid(), Guid.NewGuid(), ModOperationOrigin.Collection, fingerprint);

			Assert.Throws<ArgumentException>(() => input.ForOperationIdentity(identity));
		}

		/// <summary>
		/// Verifies a missing final native operation identity is rejected explicitly.
		/// </summary>
		[Test]
		public void ForOperationIdentity_RejectsNullIdentity()
		{
			ModInstallationRecipeInput input = CreateTranslatedInput();

			Assert.Throws<ArgumentNullException>(() => input.ForOperationIdentity(null));
		}

		/// <summary>
		/// Verifies recipe paths are canonical relative Windows paths before an adapter can translate them.
		/// </summary>
		[Test]
		public void RecipePath_NormalizesForwardSeparators()
		{
			var path = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, "meshes/actors/body.nif");

			Assert.That(path.Path, Is.EqualTo(@"meshes\actors\body.nif"));
		}

		/// <summary>
		/// Verifies rooted, traversal, ADS, ambiguous and reserved-device paths fail closed before translation.
		/// </summary>
		[TestCase(@"..\evil.txt")]
		[TestCase(@"folder\..\evil.txt")]
		[TestCase(@"C:\evil.txt")]
		[TestCase(@"\server\share\evil.txt")]
		[TestCase(@"folder\file.txt:stream")]
		[TestCase(@"CON\file.txt")]
		[TestCase(@"folder\LPT1.txt")]
		[TestCase(@"folder\file.")]
		[TestCase(@"folder\\file.txt")]
		public void RecipePath_RejectsUnsafeOrAmbiguousPath(string p_strPath)
		{
			Assert.Throws<InvalidDataException>(() =>
				new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, p_strPath));
		}

		/// <summary>
		/// Verifies expected immutable content uses one exact canonical SHA-256 and non-negative byte length.
		/// </summary>
		[Test]
		public void ExpectedContent_RejectsInvalidIdentity()
		{
			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeExpectedContent(new string('A', 64), 10));
			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeExpectedContent(new string('a', 63), 10));
			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeExpectedContent(new string('g', 64), 10));
			Assert.Throws<ArgumentOutOfRangeException>(() => new ModInstallationRecipeExpectedContent(new string('a', 64), -1));
		}

		/// <summary>
		/// Verifies adapter/capability contract versions are explicit, positive and unambiguous.
		/// </summary>
		[Test]
		public void Validation_RejectsInvalidAdapterOrCapabilityVersions()
		{
			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var content = new ModInstallationRecipeExpectedContent(new string('b', 64), 4096);
			var capability = new ModInstallationRecipeCapability("simple-file", 1);

			Assert.Throws<ArgumentOutOfRangeException>(() => new ModInstallationRecipeCapability("simple-file", 0));
			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeCapability(" simple-file ", 1));
			Assert.Throws<ArgumentOutOfRangeException>(() => new ModInstallationRecipeValidation(
				"nmm-ce.test", 0, context, content, new[] { capability }, new ModInstallationRecipePath[0]));
			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeValidation(
				" nmm-ce.test ", 1, context, content, new[] { capability }, new ModInstallationRecipePath[0]));
			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeValidation(
				"nmm-ce.test", 1, context, content,
				new[] { capability, new ModInstallationRecipeCapability("simple-file", 2) }, new ModInstallationRecipePath[0]));
			Assert.Throws<ArgumentException>(() => new ModInstallationRecipeValidation(
				"nmm-ce.test", 1, context, content, new ModInstallationRecipeCapability[0], new ModInstallationRecipePath[0]));
		}

		/// <summary>
		/// Verifies mutable caller collections are copied and cannot alter validated recipe metadata after construction.
		/// </summary>
		[Test]
		public void Validation_SnapshotsCallerCollections()
		{
			var capabilities = new List<ModInstallationRecipeCapability>
			{
				new ModInstallationRecipeCapability("simple-file", 1)
			};
			var paths = new List<ModInstallationRecipePath>
			{
				new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, @"meshes\body.nif"),
				new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"meshes\body.nif")
			};
			var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot);
			var content = new ModInstallationRecipeExpectedContent(new string('c', 64), 8192);

			var validation = new ModInstallationRecipeValidation("nmm-ce.test", 1, context, content, capabilities, paths);
			capabilities.Clear();
			paths.Clear();

			Assert.That(validation.AdapterId, Is.EqualTo("nmm-ce.test"));
			Assert.That(validation.AdapterVersion, Is.EqualTo(1));
			Assert.That(validation.InstallContext.Method, Is.EqualTo(ModInstallMethod.Direct));
			Assert.That(validation.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.GameRoot));
			Assert.That(validation.ExpectedContent, Is.SameAs(content));
			Assert.That(validation.Capabilities.Count, Is.EqualTo(1));
			Assert.That(validation.Paths.Count, Is.EqualTo(2));
		}

		/// <summary>
		/// Verifies every public C5 recipe-validation value type remains immutable after construction.
		/// </summary>
		[Test]
		public void Contract_HasNoPublicPropertySetters()
		{
			Type[] types =
			{
				typeof(ModInstallationRecipeInput),
				typeof(ModInstallationRecipeValidation),
				typeof(ModInstallationRecipeExpectedContent),
				typeof(ModInstallationRecipeCapability),
				typeof(ModInstallationRecipePath)
			};

			foreach (Type type in types)
			{
				Assert.That(type.GetProperties().Any(property => property.SetMethod != null && property.SetMethod.IsPublic),
					Is.False, type.FullName);
			}
		}

		/// <summary>
		/// Creates a translated one-file recipe suitable for operation-identity rebinding tests.
		/// </summary>
		private static ModInstallationRecipeInput CreateTranslatedInput()
		{
			const string source = @"meshes\body.nif";
			const string destination = @"meshes\body.nif";
			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var recipe = new ModInstallationSimpleFileRecipe(new[]
			{
				new ModInstallationSimpleFileMapping(source, destination)
			});
			var operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('a', 64), context, "recipe:c6.15.6-v1"));
			var validation = new ModInstallationRecipeValidation(
				ModInstallationSimpleFileRecipeAdapter.AdapterId, ModInstallationSimpleFileRecipeAdapter.AdapterVersion, context,
				new ModInstallationRecipeExpectedContent(new string('b', 64), 4096),
				new[]
				{
					new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId,
						ModInstallationSimpleFileRecipeAdapter.CapabilityVersion)
				},
				new[]
				{
					new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, source),
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destination)
				});

			return new ModInstallationSimpleFileRecipeAdapter().Translate(new ModInstallationRecipeInput(operation, validation), recipe);
		}

		/// <summary>
		/// Creates validated non-executable recipe metadata for invariant tests.
		/// </summary>
		private static ModInstallationRecipeValidation CreateValidation(ModInstallMethod p_mimMethod, ModInstallRoot p_mirRoot)
		{
			var context = new ModInstallContext(p_mimMethod, p_mirRoot);
			var content = new ModInstallationRecipeExpectedContent(new string('b', 64), 4096);
			return new ModInstallationRecipeValidation(
				"nmm-ce.test", 1, context, content,
				new[] { new ModInstallationRecipeCapability("simple-file", 1) },
				new[]
				{
					new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, @"meshes\body.nif"),
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"meshes\body.nif")
				});
		}

		/// <summary>
		/// Creates a native operation identity for recipe-input invariant tests.
		/// </summary>
		private static ModOperationIdentity CreateOperation(ModOperationOrigin p_mooOrigin, ModInstallMethod p_mimMethod,
			ModInstallRoot p_mirRoot, string p_strRecipeFingerprint)
		{
			var context = new ModInstallContext(p_mimMethod, p_mirRoot);
			var fingerprint = new ModOperationFingerprint("target-sha256:" + new string('a', 64), context, p_strRecipeFingerprint);
			return ModOperationIdentity.CreateNew(p_mooOrigin, fingerprint);
		}
	}
}
