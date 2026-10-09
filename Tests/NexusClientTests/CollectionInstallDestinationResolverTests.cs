using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies Collection destination precedence and exact basic mappings without installed game state.</summary>
	[TestFixture]
	public class CollectionInstallDestinationResolverTests
	{
		/// <summary>Resolves known root packages and preserves both native installation methods.</summary>
		[TestCase(ModInstallMethod.Virtual, "")]
		[TestCase(ModInstallMethod.Direct, "")]
		[TestCase(ModInstallMethod.Virtual, "Package")]
		[TestCase(ModInstallMethod.Direct, "Package")]
		public void Resolve_KnownPackage_MapsReviewedSourcesToGameRoot(ModInstallMethod method, string wrapper)
		{
			string prefix = wrapper.Length == 0 ? String.Empty : wrapper + "\\";
			IMod mod = CreateMod(prefix + "WinHTTP.dll", prefix + "xSE PluginPreloader.xml", prefix + @"Data\Scripts\test.pex");
			IGameMode game = CreateGameMode();
			ResolvedCollectionMemberPlan member = CreateMember();
			CollectionInstallDestination destination = CollectionInstallDestinationResolver.Resolve(game, member, mod,
				new ModInstallContext(method, ModInstallRoot.Data), CancellationToken.None);
			Assert.That(destination.InstallContext.Method, Is.EqualTo(method));
			Assert.That(destination.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.GameRoot));
			Assert.That(destination.GameRootArchiveBaseDirectory, Is.EqualTo(wrapper));
			Assert.That(member.RequiresGameRootInstall, Is.False, "Native inference must not change the provider recipe.");

			BasicInstallPlanResult planned = new BasicInstallPlanBuilder().Build(mod, game, destination.InstallContext,
				false, null, null, true, destination.GameRootArchiveBaseDirectory);
			Assert.That(planned.IsSupported, Is.True);
			Assert.That(planned.Plan.Files.Select(x => x.SourcePath), Is.EquivalentTo(new[]
				{ prefix + "WinHTTP.dll", prefix + "xSE PluginPreloader.xml", prefix + @"Data\Scripts\test.pex" }));
			Assert.That(planned.Plan.Files.Select(x => x.DestinationPath), Is.EquivalentTo(new[]
				{ "WinHTTP.dll", "xSE PluginPreloader.xml", @"Data\Scripts\test.pex" }));
		}

		/// <summary>Explicit Collection root metadata takes precedence without requiring package recognition.</summary>
		[Test]
		public void Resolve_ExplicitRoot_DoesNotReadRulesOrArchive()
		{
			IGameMode game = InterfaceStub<ITestGameMode>.Create((method, args) =>
			{
				if (method.Name == "get_SupportsGameRootModInstall") return true;
				if (method.Name == "get_GameRootPackageRules") throw new InvalidOperationException("Explicit metadata has priority.");
				return null;
			});
			IMod mod = InterfaceStub<IMod>.Create((method, args) => throw new InvalidOperationException("No archive read is needed."));
			CollectionInstallDestination result = CollectionInstallDestinationResolver.Resolve(game,
				CreateMember(CollectionMemberInstallRootBehavior.VortexDInputGameRoot), mod,
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), CancellationToken.None);
			Assert.That(result.InstallContext.Method, Is.EqualTo(ModInstallMethod.Direct));
			Assert.That(result.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.GameRoot));
			Assert.That(result.GameRootArchiveBaseDirectory, Is.Null);
		}

		/// <summary>A DLL or EXE in ordinary mod content folders does not change the recorded native destination.</summary>
		[TestCase("F4SE\\Plugins\\plugin.dll")]
		[TestCase("Tools\\tool.exe")]
		[TestCase("WinHTTP.dll")]
		public void Resolve_OrdinaryMod_PreservesFallback(string path)
		{
			var fallback = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			CollectionInstallDestination result = CollectionInstallDestinationResolver.Resolve(CreateGameMode(),
				CreateMember(), CreateMod(path), fallback, CancellationToken.None);
			Assert.That(result.InstallContext, Is.SameAs(fallback));
			Assert.That(result.GameRootArchiveBaseDirectory, Is.Null);
		}

		/// <summary>Rules remain optional and legacy games preserve their existing root and method.</summary>
		[Test]
		public void Resolve_GameWithoutRules_PreservesExistingGameRoot()
		{
			IGameMode game = InterfaceStub<IGameMode>.Create((method, args) =>
				method.Name == "get_SupportsGameRootModInstall" ? (object)true : null);
			var fallback = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot);
			CollectionInstallDestination result = CollectionInstallDestinationResolver.Resolve(game, CreateMember(),
				CreateMod("normal.dll"), fallback, CancellationToken.None);
			Assert.That(result.InstallContext, Is.SameAs(fallback));
		}

		/// <summary>Unsupported explicit root metadata remains blocked rather than silently installing under Data.</summary>
		[Test]
		public void Resolve_UnsupportedExplicitRoot_RejectsDestination()
		{
			IGameMode game = InterfaceStub<IGameMode>.Create((method, args) => null);
			Assert.Throws<NotSupportedException>(() => CollectionInstallDestinationResolver.Resolve(game,
				CreateMember(CollectionMemberInstallRootBehavior.VortexEnbGameRoot), CreateMod("file.dll"),
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), CancellationToken.None));
		}

		/// <summary>Exact mapping cannot silently include a file outside the recognised enclosing folder.</summary>
		[Test]
		public void Build_FrozenWrapperRejectsFilesOutsideReviewedBase()
		{
			Assert.Throws<InvalidDataException>(() => new BasicInstallPlanBuilder().Build(
				CreateMod(@"Package\WinHTTP.dll", "other.dll"), CreateGameMode(),
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.GameRoot), false, null, null, true, "Package"));
		}

		/// <summary>Already approved mappings remain reproducible even if current rules are unavailable.</summary>
		[Test]
		public void Build_FrozenWrapperDoesNotReadCurrentRules()
		{
			IGameMode game = InterfaceStub<ITestGameMode>.Create((method, args) =>
			{
				if (method.Name == "get_GameRootPackageRules") throw new InvalidOperationException("Approved mappings must not reevaluate rules.");
				if (method.Name == "get_PluginDirectory") return @"C:\Game\Data";
				return null;
			});
			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(CreateMod(@"Package\WinHTTP.dll"), game,
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot), false, null, null, true, "Package");
			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.Plan.Files.Single().DestinationPath, Is.EqualTo("WinHTTP.dll"));
		}

		/// <summary>Creates a default provider recipe so destination inference remains independent of source identity.</summary>
		private static ResolvedCollectionMemberPlan CreateMember(CollectionMemberInstallRootBehavior behavior = CollectionMemberInstallRootBehavior.Default)
		{
			var member = new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("preloader")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "fallout4/33946/323314", null),
				CollectionRecipeIdentity.FromFingerprint("unchanged-provider-recipe"), "Preloader", 0, behavior);
			return new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
		}

		/// <summary>Creates a game which opts into only the characterised preloader signature.</summary>
		private static IGameMode CreateGameMode()
		{
			return InterfaceStub<ITestGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_SupportsGameRootModInstall": return true;
					case "get_GameRootPackageRules": return new[] { new GameRootPackageRule("preloader",
						new[] { "WinHTTP.dll", "xSE PluginPreloader.xml" },
						new[] { new GameRootPackageXmlCheck("xSE PluginPreloader.xml", new[] { "xSE", "PluginPreloader" }) }, true) };
					case "get_StopFolders": return new[] { "F4SE", "Tools", "Data" };
					case "get_PluginDirectory": return @"C:\Game\Data";
					case "GetModFormatAdjustedPath": return args[1];
					default: return null;
				}
			});
		}

		/// <summary>Creates immutable in-memory package entries without accessing game files or settings.</summary>
		private static IMod CreateMod(params string[] files)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Filename": return @"C:\Mods\preloader.zip";
					case "get_FileName": return "preloader.zip";
					case "GetFileList": return files.ToList();
					case "GetFileStream": return new MemoryStream(Encoding.UTF8.GetBytes("<xSE><PluginPreloader/></xSE>"), false);
					default: return null;
				}
			});
		}

		/// <summary>Exposes optional rules to a detached test GameMode.</summary>
		private interface ITestGameMode : IGameMode, IGameRootPackageRuleProvider { }
	}
}
