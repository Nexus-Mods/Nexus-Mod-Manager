using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C5.8 byte-preserving replicated-layout translation without adding a separate deployment engine.
	/// </summary>
	[TestFixture]
	public class ModInstallationReplicatedLayoutRecipeAdapterTests
	{
		/// <summary>
		/// Verifies verified archive content can be renamed, replicated to several destinations and selectively excluded.
		/// </summary>
		[TestCase(ModInstallMethod.Virtual, ModInstallRoot.Data)]
		[TestCase(ModInstallMethod.Direct, ModInstallRoot.GameRoot)]
		public void Translate_ResolvesRenameReplicationAndExclusion(ModInstallMethod p_mimMethod, ModInstallRoot p_mirRoot)
		{
			byte[] alpha = Encoding.UTF8.GetBytes("alpha-content");
			byte[] beta = Encoding.UTF8.GetBytes("beta-content");
			byte[] excluded = Encoding.UTF8.GetBytes("excluded-content");

			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				IMod mod = CreateArchiveMod(tmp, new[]
				{
					new ArchiveEntry(@"source\alpha.bin", alpha),
					new ArchiveEntry(@"source\beta.bin", beta),
					new ArchiveEntry(@"source\excluded.bin", excluded)
				});
				var recipe = new ModInstallationReplicatedLayoutRecipe(new[]
				{
					new ModInstallationReplicatedFile(@"renamed\beta.dat", ComputeMd5(beta)),
					new ModInstallationReplicatedFile(@"copies\alpha-one.dat", ComputeMd5(alpha)),
					new ModInstallationReplicatedFile(@"copies\alpha-two.dat", ComputeMd5(alpha))
				});
				ModInstallationRecipeInput input = CreateInput(recipe, new ModInstallContext(p_mimMethod, p_mirRoot));

				ModInstallationRecipeInput translated = new ModInstallationReplicatedLayoutRecipeAdapter().Translate(input, mod, recipe);

				Assert.That(translated, Is.Not.SameAs(input));
				Assert.That(translated.OperationIdentity, Is.SameAs(input.OperationIdentity));
				Assert.That(translated.Validation, Is.SameAs(input.Validation));
				Assert.That(input.HasNativePlan, Is.False);
				Assert.That(translated.NativeOperations.Count, Is.EqualTo(3));
				Assert.That(translated.NativeOperations.All(operation => operation is InstallModFileOperation), Is.True);

				var first = (InstallModFileOperation)translated.NativeOperations[0];
				var second = (InstallModFileOperation)translated.NativeOperations[1];
				var third = (InstallModFileOperation)translated.NativeOperations[2];
				Assert.That(first.SourcePath, Is.EqualTo(@"source\beta.bin"));
				Assert.That(first.DestinationPath, Is.EqualTo(@"renamed\beta.dat"));
				Assert.That(second.SourcePath, Is.EqualTo(@"source\alpha.bin"));
				Assert.That(second.DestinationPath, Is.EqualTo(@"copies\alpha-one.dat"));
				Assert.That(third.SourcePath, Is.EqualTo(@"source\alpha.bin"));
				Assert.That(third.DestinationPath, Is.EqualTo(@"copies\alpha-two.dat"));
				Assert.That(translated.NativeOperations.Cast<InstallModFileOperation>().All(operation => operation.DeploymentDecision == null), Is.True,
					"C5.8 must leave Direct/Virtual/mixed deployment decisions to the existing native executor.");
				Assert.That(translated.NativeOperations.Cast<InstallModFileOperation>().Any(operation =>
					StringComparer.OrdinalIgnoreCase.Equals(operation.SourcePath, @"source\excluded.bin")), Is.False,
					"Archive entries absent from the desired replicated output tree must remain excluded.");
			}
		}

		/// <summary>
		/// Verifies a requested digest which does not exist in the verified archive fails closed.
		/// </summary>
		[Test]
		public void Translate_RejectsMissingContent()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				IMod mod = CreateArchiveMod(tmp, new[]
				{
					new ArchiveEntry(@"source\one.bin", Encoding.UTF8.GetBytes("one"))
				});
				var recipe = new ModInstallationReplicatedLayoutRecipe(new[]
				{
					new ModInstallationReplicatedFile(@"target\missing.bin", new string('a', 32))
				});

				Assert.Throws<InvalidDataException>(() => new ModInstallationReplicatedLayoutRecipeAdapter().Translate(
					CreateInput(recipe), mod, recipe));
			}
		}

		/// <summary>
		/// Verifies identical archive content at several paths is rejected unless the recipe explicitly disambiguates the source.
		/// </summary>
		[Test]
		public void Translate_RejectsAmbiguousContentWithoutHint()
		{
			byte[] shared = Encoding.UTF8.GetBytes("same-content");
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				IMod mod = CreateArchiveMod(tmp, new[]
				{
					new ArchiveEntry(@"source\first.bin", shared),
					new ArchiveEntry(@"source\second.bin", shared)
				});
				var recipe = new ModInstallationReplicatedLayoutRecipe(new[]
				{
					new ModInstallationReplicatedFile(@"target\same.bin", ComputeMd5(shared))
				});

				Assert.Throws<InvalidDataException>(() => new ModInstallationReplicatedLayoutRecipeAdapter().Translate(
					CreateInput(recipe), mod, recipe));
			}
		}

		/// <summary>
		/// Verifies an exact validated source hint resolves an otherwise ambiguous same-content archive match.
		/// </summary>
		[Test]
		public void Translate_SourceHintResolvesAmbiguousContent()
		{
			byte[] shared = Encoding.UTF8.GetBytes("same-content");
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				IMod mod = CreateArchiveMod(tmp, new[]
				{
					new ArchiveEntry(@"source\first.bin", shared),
					new ArchiveEntry(@"source\second.bin", shared)
				});
				var recipe = new ModInstallationReplicatedLayoutRecipe(new[]
				{
					new ModInstallationReplicatedFile(@"target\same.bin", ComputeMd5(shared), @"source/second.bin")
				});

				ModInstallationRecipeInput translated = new ModInstallationReplicatedLayoutRecipeAdapter().Translate(
					CreateInput(recipe), mod, recipe);

				var operation = (InstallModFileOperation)translated.NativeOperations.Single();
				Assert.That(operation.SourcePath, Is.EqualTo(@"source\second.bin"));
				Assert.That(operation.DestinationPath, Is.EqualTo(@"target\same.bin"));
			}
		}

		/// <summary>
		/// Verifies an explicit source hint must exist and must contain the exact requested bytes.
		/// </summary>
		[Test]
		public void Translate_RejectsMissingOrMismatchedSourceHint()
		{
			byte[] one = Encoding.UTF8.GetBytes("one");
			byte[] two = Encoding.UTF8.GetBytes("two");
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				IMod mod = CreateArchiveMod(tmp, new[]
				{
					new ArchiveEntry(@"source\one.bin", one),
					new ArchiveEntry(@"source\two.bin", two)
				});
				var mismatched = new ModInstallationReplicatedLayoutRecipe(new[]
				{
					new ModInstallationReplicatedFile(@"target\one.bin", ComputeMd5(one), @"source\two.bin")
				});
				var missing = new ModInstallationReplicatedLayoutRecipe(new[]
				{
					new ModInstallationReplicatedFile(@"target\one.bin", ComputeMd5(one), @"source\missing.bin")
				});

				Assert.Throws<InvalidDataException>(() => new ModInstallationReplicatedLayoutRecipeAdapter().Translate(
					CreateInput(mismatched), mod, mismatched));
				Assert.Throws<InvalidDataException>(() => new ModInstallationReplicatedLayoutRecipeAdapter().Translate(
					CreateInput(missing), mod, missing));
			}
		}

		/// <summary>
		/// Verifies replicated-file values reuse path validation and require canonical lowercase matching digests.
		/// </summary>
		[Test]
		public void File_ValidatesPathsAndCanonicalDigest()
		{
			string digest = new string('a', 32);
			var file = new ModInstallationReplicatedFile("target/output.bin", digest, "source/input.bin");

			Assert.That(file.DestinationPath, Is.EqualTo(@"target\output.bin"));
			Assert.That(file.ContentMd5, Is.EqualTo(digest));
			Assert.That(file.SourcePathHint, Is.EqualTo(@"source\input.bin"));
			Assert.Throws<InvalidDataException>(() => new ModInstallationReplicatedFile(@"..\evil.bin", digest));
			Assert.Throws<InvalidDataException>(() => new ModInstallationReplicatedFile(@"target\safe.bin", digest, @"C:\evil.bin"));
			Assert.Throws<ArgumentException>(() => new ModInstallationReplicatedFile(@"target\safe.bin", new string('A', 32)));
			Assert.Throws<ArgumentException>(() => new ModInstallationReplicatedFile(@"target\safe.bin", new string('a', 31)));
			Assert.Throws<ArgumentException>(() => new ModInstallationReplicatedFile(@"target\safe.bin", new string('z', 32)));
		}

		/// <summary>
		/// Verifies recipe construction snapshots the caller sequence, permits source replication and rejects destination collisions.
		/// </summary>
		[Test]
		public void Recipe_SnapshotsOutputsAllowsReplicationAndRejectsDestinationCollisions()
		{
			string digest = new string('a', 32);
			var files = new List<ModInstallationReplicatedFile>
			{
				new ModInstallationReplicatedFile(@"target\one.bin", digest),
				new ModInstallationReplicatedFile(@"target\two.bin", digest)
			};
			var recipe = new ModInstallationReplicatedLayoutRecipe(files);
			files.Clear();

			Assert.That(recipe.Files.Count, Is.EqualTo(2));
			Assert.Throws<ArgumentException>(() => new ModInstallationReplicatedLayoutRecipe(new ModInstallationReplicatedFile[0]));
			Assert.Throws<ArgumentException>(() => new ModInstallationReplicatedLayoutRecipe(new ModInstallationReplicatedFile[] { null }));
			Assert.Throws<ArgumentException>(() => new ModInstallationReplicatedLayoutRecipe(new[]
			{
				new ModInstallationReplicatedFile(@"target\same.bin", digest),
				new ModInstallationReplicatedFile(@"TARGET\SAME.BIN", new string('b', 32))
			}));
		}

		/// <summary>
		/// Verifies the adapter refuses recipe inputs produced for a different or broader capability contract.
		/// </summary>
		[Test]
		public void Translate_RejectsUnsupportedAdapterOrCapabilities()
		{
			byte[] bytes = Encoding.UTF8.GetBytes("content");
			var recipe = new ModInstallationReplicatedLayoutRecipe(new[]
			{
				new ModInstallationReplicatedFile(@"target\one.bin", ComputeMd5(bytes))
			});

			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				IMod mod = CreateArchiveMod(tmp, new[] { new ArchiveEntry(@"source\one.bin", bytes) });
				var adapter = new ModInstallationReplicatedLayoutRecipeAdapter();

				Assert.Throws<NotSupportedException>(() => adapter.Translate(
					CreateInput(recipe, "other.adapter", 1, new[]
					{
						new ModInstallationRecipeCapability(ModInstallationReplicatedLayoutRecipeAdapter.CapabilityId, 1)
					}), mod, recipe));
				Assert.Throws<NotSupportedException>(() => adapter.Translate(
					CreateInput(recipe, ModInstallationReplicatedLayoutRecipeAdapter.AdapterId, 2, new[]
					{
						new ModInstallationRecipeCapability(ModInstallationReplicatedLayoutRecipeAdapter.CapabilityId, 1)
					}), mod, recipe));
				Assert.Throws<NotSupportedException>(() => adapter.Translate(
					CreateInput(recipe, ModInstallationReplicatedLayoutRecipeAdapter.AdapterId, 1, new[]
					{
						new ModInstallationRecipeCapability(ModInstallationReplicatedLayoutRecipeAdapter.CapabilityId, 2)
					}), mod, recipe));
				Assert.Throws<NotSupportedException>(() => adapter.Translate(
					CreateInput(recipe, ModInstallationReplicatedLayoutRecipeAdapter.AdapterId, 1, new[]
					{
						new ModInstallationRecipeCapability(ModInstallationReplicatedLayoutRecipeAdapter.CapabilityId, 1),
						new ModInstallationRecipeCapability("binary-patch", 1)
					}), mod, recipe));
			}
		}

		/// <summary>
		/// Verifies translation consumes exactly the destinations and explicit source hints admitted by C5.3 validation.
		/// </summary>
		[Test]
		public void Translate_RejectsMissingExtraOrDuplicateValidatedPaths()
		{
			byte[] bytes = Encoding.UTF8.GetBytes("content");
			var recipe = new ModInstallationReplicatedLayoutRecipe(new[]
			{
				new ModInstallationReplicatedFile(@"target\one.bin", ComputeMd5(bytes), @"source\one.bin")
			});
			var destination = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"target\one.bin");
			var source = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, @"source\one.bin");

			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				IMod mod = CreateArchiveMod(tmp, new[] { new ArchiveEntry(@"source\one.bin", bytes) });
				var adapter = new ModInstallationReplicatedLayoutRecipeAdapter();

				Assert.Throws<InvalidDataException>(() => adapter.Translate(CreateInput(recipe, new[] { destination }), mod, recipe));
				Assert.Throws<InvalidDataException>(() => adapter.Translate(CreateInput(recipe, new[]
				{
					destination,
					source,
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"target\extra.bin")
				}), mod, recipe));
				Assert.Throws<InvalidDataException>(() => adapter.Translate(CreateInput(recipe, new[]
				{
					destination,
					source,
					source
				}), mod, recipe));
			}
		}

		/// <summary>
		/// Verifies case-insensitive duplicate archive paths cannot make content resolution depend on archive enumeration behavior.
		/// </summary>
		[Test]
		public void Translate_RejectsAmbiguousDuplicateArchivePaths()
		{
			byte[] bytes = Encoding.UTF8.GetBytes("content");
			var recipe = new ModInstallationReplicatedLayoutRecipe(new[]
			{
				new ModInstallationReplicatedFile(@"target\one.bin", ComputeMd5(bytes))
			});

			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				IMod mod = CreateArchiveMod(tmp, new[]
				{
					new ArchiveEntry(@"source\one.bin", bytes),
					new ArchiveEntry(@"SOURCE\ONE.BIN", bytes)
				});

				Assert.Throws<InvalidDataException>(() => new ModInstallationReplicatedLayoutRecipeAdapter().Translate(
					CreateInput(recipe), mod, recipe));
			}
		}

		/// <summary>
		/// Verifies the new replicated-layout value types expose immutable public state only.
		/// </summary>
		[Test]
		public void Contract_HasNoPublicPropertySetters()
		{
			Type[] types =
			{
				typeof(ModInstallationReplicatedFile),
				typeof(ModInstallationReplicatedLayoutRecipe)
			};

			foreach (Type type in types)
			{
				Assert.That(type.GetProperties().Any(property => property.SetMethod != null && property.SetMethod.IsPublic),
					Is.False, type.FullName);
			}
		}

		/// <summary>
		/// Creates a fully valid C5.8 input using the default Virtual/Data native context.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationReplicatedLayoutRecipe recipe)
		{
			return CreateInput(recipe, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		/// <summary>
		/// Creates a fully valid C5.8 input for one explicit native install context.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationReplicatedLayoutRecipe recipe, ModInstallContext context)
		{
			return CreateInput(recipe, ModInstallationReplicatedLayoutRecipeAdapter.AdapterId,
				ModInstallationReplicatedLayoutRecipeAdapter.AdapterVersion,
				new[]
				{
					new ModInstallationRecipeCapability(ModInstallationReplicatedLayoutRecipeAdapter.CapabilityId,
						ModInstallationReplicatedLayoutRecipeAdapter.CapabilityVersion)
				}, CreateDeclaredPaths(recipe), context);
		}

		/// <summary>
		/// Creates a C5.8 input with explicit validation-path declarations.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationReplicatedLayoutRecipe recipe,
			IEnumerable<ModInstallationRecipePath> paths)
		{
			return CreateInput(recipe, ModInstallationReplicatedLayoutRecipeAdapter.AdapterId,
				ModInstallationReplicatedLayoutRecipeAdapter.AdapterVersion,
				new[]
				{
					new ModInstallationRecipeCapability(ModInstallationReplicatedLayoutRecipeAdapter.CapabilityId,
						ModInstallationReplicatedLayoutRecipeAdapter.CapabilityVersion)
				}, paths, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		/// <summary>
		/// Creates a C5.8 input with explicit adapter/capability metadata for rejection tests.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationReplicatedLayoutRecipe recipe, string adapterId,
			int adapterVersion, IEnumerable<ModInstallationRecipeCapability> capabilities)
		{
			return CreateInput(recipe, adapterId, adapterVersion, capabilities, CreateDeclaredPaths(recipe),
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		/// <summary>
		/// Creates a C5.8 input with all validation metadata supplied explicitly.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(ModInstallationReplicatedLayoutRecipe recipe, string adapterId,
			int adapterVersion, IEnumerable<ModInstallationRecipeCapability> capabilities,
			IEnumerable<ModInstallationRecipePath> paths, ModInstallContext context)
		{
			var operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('a', 64), context, "recipe:c5.8-replicated-v1"));
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
		/// Produces exactly the caller-supplied paths C5.8 consumes before resolving content inside the verified archive.
		/// </summary>
		private static IEnumerable<ModInstallationRecipePath> CreateDeclaredPaths(ModInstallationReplicatedLayoutRecipe recipe)
		{
			var paths = new List<ModInstallationRecipePath>();
			var addedHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationReplicatedFile file in recipe.Files)
			{
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, file.DestinationPath));
				if (file.SourcePathHint != null && addedHints.Add(file.SourcePathHint))
					paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, file.SourcePathHint));
			}
			return paths;
		}

		/// <summary>
		/// Creates an <see cref="IMod"/> test double whose archive paths stream bytes from isolated temporary files.
		/// </summary>
		private static IMod CreateArchiveMod(TemporaryDirectory temporaryDirectory, IEnumerable<ArchiveEntry> entries)
		{
			var fileList = new List<string>();
			var physicalPaths = new Dictionary<string, string>(StringComparer.Ordinal);
			int index = 0;
			foreach (ArchiveEntry entry in entries)
			{
				string physicalPath = Path.Combine(temporaryDirectory.Path, "archive-" + index.ToString() + ".bin");
				File.WriteAllBytes(physicalPath, entry.Bytes);
				fileList.Add(entry.Path);
				physicalPaths.Add(entry.Path, physicalPath);
				index++;
			}

			return InterfaceStub<IMod>.Create((method, args) =>
			{
				if (method.Name == "GetFileList" && args.Length == 0)
					return new List<string>(fileList);
				if (method.Name == "GetFile")
					return File.ReadAllBytes(physicalPaths[(string)args[0]]);
				return null;
			});
		}

		/// <summary>
		/// Computes the canonical MD5 digest used by the characterized replicated-layout contract.
		/// </summary>
		private static string ComputeMd5(byte[] bytes)
		{
			using (MD5 md5 = MD5.Create())
			{
				byte[] digest = md5.ComputeHash(bytes);
				var builder = new StringBuilder(digest.Length * 2);
				foreach (byte value in digest)
					builder.Append(value.ToString("x2"));
				return builder.ToString();
			}
		}

		/// <summary>
		/// Describes one synthetic archive path and its file bytes for adapter tests.
		/// </summary>
		private sealed class ArchiveEntry
		{
			/// <summary>
			/// Initializes a synthetic archive entry.
			/// </summary>
			public ArchiveEntry(string path, byte[] bytes)
			{
				Path = path;
				Bytes = bytes;
			}

			/// <summary>
			/// Gets the archive-relative path exposed by the test double.
			/// </summary>
			public string Path { get; }

			/// <summary>
			/// Gets the bytes streamed for this archive entry.
			/// </summary>
			public byte[] Bytes { get; }
		}
	}
}
