using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C5.4 simple exact-file recipe translation without executing native installation operations.
	/// </summary>
	[TestFixture]
	public class ModInstallationSimpleFileRecipeAdapterTests
	{
		/// <summary>
		/// Verifies exact mappings become existing typed archive-file operations in deterministic recipe order.
		/// </summary>
		[TestCase(ModInstallMethod.Virtual, ModInstallRoot.Data)]
		[TestCase(ModInstallMethod.Direct, ModInstallRoot.GameRoot)]
		public void Translate_ProducesOrderedNativeFileOperations(ModInstallMethod p_mimMethod, ModInstallRoot p_mirRoot)
		{
			var recipe = new ModInstallationSimpleFileRecipe(new[]
			{
				new ModInstallationSimpleFileMapping(@"meshes\body.nif", @"meshes\actors\body.nif"),
				new ModInstallationSimpleFileMapping(@"textures\body.dds", @"textures\actors\body.dds")
			});
			ModInstallationRecipeInput input = CreateInput(recipe, new ModInstallContext(p_mimMethod, p_mirRoot));

			ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(input, recipe);

			Assert.That(translated, Is.Not.SameAs(input));
			Assert.That(translated.OperationIdentity, Is.SameAs(input.OperationIdentity));
			Assert.That(translated.Validation, Is.SameAs(input.Validation));
			Assert.That(input.HasNativePlan, Is.False);
			Assert.That(input.NativeOperations, Is.Null);
			Assert.That(translated.HasNativePlan, Is.True);
			Assert.That(translated.NativeOperations.Count, Is.EqualTo(2));
			Assert.That(translated.NativeOperations.All(operation => operation is InstallModFileOperation), Is.True);
			var first = (InstallModFileOperation)translated.NativeOperations[0];
			var second = (InstallModFileOperation)translated.NativeOperations[1];
			Assert.That(first.SourcePath, Is.EqualTo(@"meshes\body.nif"));
			Assert.That(first.DestinationPath, Is.EqualTo(@"meshes\actors\body.nif"));
			Assert.That(first.DeploymentDecision, Is.Null, "C5.4 must not pre-resolve native deployment behavior.");
			Assert.That(second.SourcePath, Is.EqualTo(@"textures\body.dds"));
			Assert.That(second.DestinationPath, Is.EqualTo(@"textures\actors\body.dds"));
			Assert.That(second.DeploymentDecision, Is.Null, "C5.4 must remain translation-only.");
		}

		/// <summary>
		/// Verifies translation rejects missing input rather than falling back to ordinary install behavior.
		/// </summary>
		[Test]
		public void Translate_RejectsNullInputOrRecipe()
		{
			var recipe = CreateSingleFileRecipe();
			ModInstallationRecipeInput input = CreateInput(recipe);
			var adapter = new ModInstallationSimpleFileRecipeAdapter();

			Assert.Throws<ArgumentNullException>(() => adapter.Translate(null, recipe));
			Assert.Throws<ArgumentNullException>(() => adapter.Translate(input, null));
		}

		/// <summary>
		/// Verifies translated operation intent is snapshotted once and cannot be replaced by a second adapter pass.
		/// </summary>
		[Test]
		public void Translate_AttachesImmutableSinglePlanSnapshot()
		{
			var recipe = CreateSingleFileRecipe();
			ModInstallationRecipeInput input = CreateInput(recipe);
			ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(input, recipe);

			IList<ScriptedInstallOperation> operations = translated.NativeOperations as IList<ScriptedInstallOperation>;
			Assert.That(operations, Is.Not.Null);
			Assert.That(operations.IsReadOnly, Is.True);
			Assert.Throws<NotSupportedException>(() => operations.Add(
				new InstallModFileOperation(@"source\two.bin", @"target\two.bin")));
			Assert.Throws<InvalidOperationException>(() => new ModInstallationSimpleFileRecipeAdapter().Translate(translated, recipe));
		}

		/// <summary>
		/// Verifies simple mappings reuse C5.3 path canonicalization before they can enter a native typed plan.
		/// </summary>
		[Test]
		public void Mapping_UsesValidatedCanonicalPaths()
		{
			var mapping = new ModInstallationSimpleFileMapping("meshes/body.nif", "meshes/actors/body.nif");

			Assert.That(mapping.SourcePath, Is.EqualTo(@"meshes\body.nif"));
			Assert.That(mapping.DestinationPath, Is.EqualTo(@"meshes\actors\body.nif"));
			Assert.Throws<InvalidDataException>(() => new ModInstallationSimpleFileMapping(@"..\evil.bin", @"data\evil.bin"));
			Assert.Throws<InvalidDataException>(() => new ModInstallationSimpleFileMapping(@"data\safe.bin", @"C:\evil.bin"));
		}

		/// <summary>
		/// Verifies the simple adapter does not silently implement replicated layouts or ambiguous destination collisions.
		/// </summary>
		[Test]
		public void Recipe_RejectsReplicationAndDestinationCollisions()
		{
			Assert.Throws<ArgumentException>(() => new ModInstallationSimpleFileRecipe(new[]
			{
				new ModInstallationSimpleFileMapping(@"source\same.bin", @"first\same.bin"),
				new ModInstallationSimpleFileMapping(@"source\same.bin", @"second\same.bin")
			}));
			Assert.Throws<ArgumentException>(() => new ModInstallationSimpleFileRecipe(new[]
			{
				new ModInstallationSimpleFileMapping(@"source\first.bin", @"target\same.bin"),
				new ModInstallationSimpleFileMapping(@"source\second.bin", @"target\same.bin")
			}));
		}

		/// <summary>
		/// Verifies recipe construction snapshots the caller sequence and requires at least one exact mapping.
		/// </summary>
		[Test]
		public void Recipe_SnapshotsMappingsAndRejectsEmptyInput()
		{
			var mappings = new List<ModInstallationSimpleFileMapping>
			{
				new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin")
			};
			var recipe = new ModInstallationSimpleFileRecipe(mappings);
			mappings.Clear();

			Assert.That(recipe.Mappings.Count, Is.EqualTo(1));
			Assert.Throws<ArgumentException>(() => new ModInstallationSimpleFileRecipe(new ModInstallationSimpleFileMapping[0]));
			Assert.Throws<ArgumentException>(() => new ModInstallationSimpleFileRecipe(new ModInstallationSimpleFileMapping[] { null }));
		}

		/// <summary>
		/// Verifies an adapter never translates a recipe validated for another adapter contract or capability version.
		/// </summary>
		[Test]
		public void Translate_RejectsUnsupportedAdapterOrCapabilities()
		{
			var recipe = CreateSingleFileRecipe();

			Assert.Throws<NotSupportedException>(() => new ModInstallationSimpleFileRecipeAdapter().Translate(
				CreateInput(recipe, "other.adapter", ModInstallationSimpleFileRecipeAdapter.AdapterVersion,
					new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, 1) }), recipe));
			Assert.Throws<NotSupportedException>(() => new ModInstallationSimpleFileRecipeAdapter().Translate(
				CreateInput(recipe, ModInstallationSimpleFileRecipeAdapter.AdapterId, 2,
					new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, 1) }), recipe));
			Assert.Throws<NotSupportedException>(() => new ModInstallationSimpleFileRecipeAdapter().Translate(
				CreateInput(recipe, ModInstallationSimpleFileRecipeAdapter.AdapterId, 1,
					new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, 2) }), recipe));
			Assert.Throws<NotSupportedException>(() => new ModInstallationSimpleFileRecipeAdapter().Translate(
				CreateInput(recipe, ModInstallationSimpleFileRecipeAdapter.AdapterId, 1,
					new[]
					{
						new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, 1),
						new ModInstallationRecipeCapability("generated-file", 1)
					}), recipe));
		}

		/// <summary>
		/// Verifies translation consumes exactly the path set already admitted by C5.3 validation and cannot add or ignore effects.
		/// </summary>
		[Test]
		public void Translate_RejectsMissingExtraOrDuplicateValidatedPaths()
		{
			var recipe = CreateSingleFileRecipe();
			ModInstallationSimpleFileMapping mapping = recipe.Mappings[0];
			var source = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, mapping.SourcePath);
			var destination = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, mapping.DestinationPath);

			Assert.Throws<InvalidDataException>(() => new ModInstallationSimpleFileRecipeAdapter().Translate(
				CreateInput(recipe, new[] { source }), recipe));
			Assert.Throws<InvalidDataException>(() => new ModInstallationSimpleFileRecipeAdapter().Translate(
				CreateInput(recipe, new[]
				{
					source,
					destination,
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"extra\unconsumed.bin")
				}), recipe));
			Assert.Throws<InvalidDataException>(() => new ModInstallationSimpleFileRecipeAdapter().Translate(
				CreateInput(recipe, new[] { source, destination, destination }), recipe));
		}

		/// <summary>
		/// Verifies the new simple-recipe value types expose immutable public state only.
		/// </summary>
		[Test]
		public void Contract_HasNoPublicPropertySetters()
		{
			Type[] types =
			{
				typeof(ModInstallationSimpleFileMapping),
				typeof(ModInstallationSimpleFileRecipe)
			};

			foreach (Type type in types)
			{
				Assert.That(type.GetProperties().Any(property => property.SetMethod != null && property.SetMethod.IsPublic),
					Is.False, type.FullName);
			}
		}

		/// <summary>
		/// Creates the smallest valid one-file exact recipe.
		/// </summary>
		private static ModInstallationSimpleFileRecipe CreateSingleFileRecipe()
		{
			return new ModInstallationSimpleFileRecipe(new[]
			{
				new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin")
			});
		}

		/// <summary>
		/// Creates a fully valid C5.4 input whose path declarations are derived exactly from the supplied recipe.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationSimpleFileRecipe recipe)
		{
			return CreateInput(recipe, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		/// <summary>
		/// Creates a fully valid C5.4 input for one explicit native install context.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationSimpleFileRecipe recipe, ModInstallContext context)
		{
			return CreateInput(recipe, ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion,
				new[]
				{
					new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId,
						ModInstallationSimpleFileRecipeAdapter.CapabilityVersion)
				}, CreateDeclaredPaths(recipe), context);
		}

		/// <summary>
		/// Creates a C5.4 input with an explicit set of validation-path declarations.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationSimpleFileRecipe recipe,
			IEnumerable<ModInstallationRecipePath> paths)
		{
			return CreateInput(recipe, ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion,
				new[]
				{
					new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId,
						ModInstallationSimpleFileRecipeAdapter.CapabilityVersion)
				}, paths, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		/// <summary>
		/// Creates a C5.4 input with explicit adapter/capability metadata for rejection tests.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationSimpleFileRecipe recipe, string adapterId,
			int adapterVersion, IEnumerable<ModInstallationRecipeCapability> capabilities)
		{
			return CreateInput(recipe, adapterId, adapterVersion, capabilities, CreateDeclaredPaths(recipe),
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		/// <summary>
		/// Creates a C5.4 input with all validation metadata supplied explicitly.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationSimpleFileRecipe recipe, string adapterId,
			int adapterVersion, IEnumerable<ModInstallationRecipeCapability> capabilities,
			IEnumerable<ModInstallationRecipePath> paths, ModInstallContext context)
		{
			var operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('a', 64), context, "recipe:c5.4-simple-v1"));
			var validation = new ModInstallationRecipeValidation(
				adapterId,
				adapterVersion,
				context,
				new ModInstallationRecipeExpectedContent(new string('b', 64), 4096),
				capabilities,
				paths);
			return new ModInstallationRecipeInput(operation, validation);
		}

		/// <summary>
		/// Produces the exact path declarations required by a simple recipe.
		/// </summary>
		private static IEnumerable<ModInstallationRecipePath> CreateDeclaredPaths(ModInstallationSimpleFileRecipe recipe)
		{
			var paths = new List<ModInstallationRecipePath>();
			foreach (ModInstallationSimpleFileMapping mapping in recipe.Mappings)
			{
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, mapping.SourcePath));
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, mapping.DestinationPath));
			}
			return paths;
		}
	}
}
