namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using System.Runtime.InteropServices;
	using System.Runtime.Serialization;
	using ChinhDo.Transactions;
	using Nexus.Client;
	using Nexus.Client.CollectionManagement;
	using Nexus.Client.CollectionManagement.Persistence;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.ModManagement.Scripting;
	using Nexus.Client.ModManagement.Scripting.Operations;
	using Nexus.Client.Mods;
	using Nexus.Client.PluginManagement;
	using Nexus.Client.Settings;
	using Nexus.Client.Util.Collections;
	using Nexus.Transactions;
	using NUnit.Framework;

	/// <summary>
	/// Exercises real Virtual ownership and legacy file removal across revision replacements and native uninstall.
	/// </summary>
	[TestFixture]
	public class VirtualModRemovalRegressionTests
	{
		/// <summary>A stale missing-archive placeholder still removes its exact installed registration.</summary>
		[TestCase(ModInstallRoot.Data, ModInstallMethod.Virtual)]
		[TestCase(ModInstallRoot.GameRoot, ModInstallMethod.Virtual)]
		[TestCase(ModInstallRoot.GameRoot, ModInstallMethod.Direct)]
		public void NativeUninstall_MissingArchivePlaceholderRemovesRegistrationByFilename(ModInstallRoot root, ModInstallMethod method)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod installed = fixture.CreateMod("MissingArchive");
				IMod sibling = fixture.CreateMod("NewerArchive");
				((InstallLog.DummyMod)sibling).ModName = installed.ModName;
				fixture.InstallLog.AddActiveMod(installed, root, method);
				fixture.InstallLog.AddActiveMod(sibling, root, method);
				IMod selected = new InstallLog.DummyMod(installed.ModName, installed.Filename);
				Assert.That(File.Exists(selected.Filename), Is.False);
				Assert.That(fixture.InstallLog.ActiveMods.Contains(selected), Is.False,
					"The selection must refer to a placeholder from before native state was reloaded.");
				Assert.That(fixture.InstallLog.GetModKey(selected), Is.EqualTo(fixture.InstallLog.GetModKey(installed)));
				Assert.That(fixture.Deployment.HasManagedFiles(selected), Is.False);

				SynchronousUninstaller result = fixture.Uninstall(selected);

				Assert.That(result.Succeeded, Is.True, result.CompletionMessage);
				Assert.That(fixture.InstallLog.GetModKey(installed), Is.Null);
				Assert.That(fixture.InstallLog.GetModKey(sibling), Is.Not.Null,
					"A different archive with the same display name must remain installed.");
			}
		}

		/// <summary>A disabled legacy Virtual mod remains removable when its archive and original placeholder are gone.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void NativeUninstall_ReloadedMissingArchiveRemovesInactiveLegacyLinks(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod installed = fixture.CreateMod("DisabledMissingArchive");
				fixture.Install(installed, "Disabled.esp", "disabled mod");
				fixture.Activator.VirtualLinks.Single().Active = false;
				File.Delete(fixture.Deployed("Disabled.esp"));
				IMod selected = new InstallLog.DummyMod(installed.ModName, installed.Filename);
				Assert.That(fixture.Deployment.HasManagedFiles(selected), Is.False);

				SynchronousUninstaller result = fixture.Uninstall(selected);

				Assert.That(result.Succeeded, Is.True, result.CompletionMessage);
				Assert.That(fixture.InstallLog.GetModKey(installed), Is.Null);
				Assert.That(fixture.Activator.VirtualLinks, Is.Empty);
				Assert.That(File.Exists(fixture.Staged(installed, "Disabled.esp")), Is.False);
			}
		}

		/// <summary>Legacy disable continues when the previous mesh is missing and retains its owner as inactive.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void LinkDisable_LegacyOverwriteRestorationIsTransactional(bool missingFallback)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod fallback = fixture.CreateMod("PreviousMesh");
				IMod winner = fixture.CreateMod("MeshFixes");
				const string relative = @"Meshes\Landscape\Rocks\rockcliffmineentrance.nif";
				fixture.Install(fallback, relative, "previous mesh");
				fixture.Install(winner, relative, "winning mesh");
				if (missingFallback) File.Delete(fixture.Staged(fallback, relative));

				var task = new SynchronousLinkDisableTask(winner, fixture);
				Assert.DoesNotThrow(() => task.Execute());

				if (missingFallback)
				{
					Assert.That(task.Status, Is.Not.EqualTo(Nexus.Client.BackgroundTasks.TaskStatus.Error));
					Assert.That(File.Exists(fixture.Deployed(relative)), Is.False);
					Assert.That(fixture.Activator.VirtualLinks.Single().Active, Is.False);
					Assert.That(fixture.Activator.VirtualLinks.Single().ModInfo.ModFileName,
						Is.EqualTo(Path.GetFileName(fallback.Filename)));
				}
				else
				{
					Assert.That(task.Status, Is.Not.EqualTo(Nexus.Client.BackgroundTasks.TaskStatus.Error));
					Assert.That(File.ReadAllText(fixture.Deployed(relative)), Is.EqualTo("previous mesh"));
					Assert.That(fixture.Activator.VirtualLinks.Count, Is.EqualTo(1));
				}
				Assert.That(fixture.InstallLog.GetModKey(winner), Is.Not.Null,
					"Disabling must retain the installed mod's native registration.");
			}
		}

		/// <summary>Replacement selection walks the remaining Virtual owners in priority order without discarding missing ones.</summary>
		[TestCase(0)]
		[TestCase(1)]
		[TestCase(2)]
		public void NativeUninstall_RestoresHighestAvailableVirtualReplacement(int missingCount)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod lowest = fixture.CreateMod("LowestReplacement");
				IMod middle = fixture.CreateMod("MiddleReplacement");
				IMod highest = fixture.CreateMod("HighestReplacement");
				IMod removed = fixture.CreateMod("RemovedWinner");
				fixture.Install(lowest, "Shared.nif", "lowest");
				fixture.Install(middle, "Shared.nif", "middle");
				fixture.Install(highest, "Shared.nif", "highest");
				fixture.Install(removed, "Shared.nif", "removed");
				if (missingCount > 0) File.Delete(fixture.Staged(highest, "Shared.nif"));
				if (missingCount > 1) File.Delete(fixture.Staged(middle, "Shared.nif"));

				SynchronousUninstaller result = fixture.Uninstall(removed);

				Assert.That(result.Succeeded, Is.True, result.CompletionMessage);
				Assert.That(File.ReadAllText(fixture.Deployed("Shared.nif")),
					Is.EqualTo(missingCount == 0 ? "highest" : missingCount == 1 ? "middle" : "lowest"));
				Assert.That(fixture.Activator.VirtualLinks.Count, Is.EqualTo(3));
				IMod expected = missingCount == 0 ? highest : missingCount == 1 ? middle : lowest;
				Assert.That(fixture.Activator.VirtualLinks.Single(link => link.Active).ModInfo.ModFileName,
					Is.EqualTo(Path.GetFileName(expected.Filename)));
				Assert.That(fixture.InstallLog.GetModKey(removed), Is.Null);
				foreach (IMod retained in new[] { lowest, middle, highest })
					Assert.That(fixture.InstallLog.GetModKey(retained), Is.Not.Null);
			}
		}

		/// <summary>Missing replacement content does not block native unregistering or removal of the mod's other files.</summary>
		[Test]
		public void NativeUninstall_MissingReplacementContinuesThroughRemainingFiles()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod fallback = fixture.CreateMod("MissingReplacement");
				IMod winner = fixture.CreateMod("RemovedMeshFixes");
				fixture.Install(fallback, "Shared.nif", "previous mesh");
				fixture.Install(winner, "Shared.nif", "winning mesh");
				fixture.Install(winner, "Other.nif", "other mesh");
				File.Delete(fixture.Staged(fallback, "Shared.nif"));

				SynchronousUninstaller result = fixture.Uninstall(winner);

				Assert.That(result.Succeeded, Is.True, result.CompletionMessage);
				Assert.That(fixture.InstallLog.GetModKey(winner), Is.Null);
				Assert.That(fixture.InstallLog.GetModKey(fallback), Is.Not.Null);
				Assert.That(File.Exists(fixture.Deployed("Shared.nif")), Is.False);
				Assert.That(File.Exists(fixture.Deployed("Other.nif")), Is.False);
				Assert.That(fixture.Activator.VirtualLinks.Single().Active, Is.False);
			}
		}

		/// <summary>Present but unreadable replacement content still reports failure and restores the removed winner.</summary>
		[Test]
		public void LinkDisable_LockedReplacementStillRollsBack()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod fallback = fixture.CreateMod("LockedReplacement");
				IMod winner = fixture.CreateMod("KeptMeshFixes");
				fixture.Install(fallback, "Locked.nif", "previous mesh");
				fixture.Install(winner, "Locked.nif", "winning mesh");
				using (var locked = new FileStream(fixture.Staged(fallback, "Locked.nif"), FileMode.Open, FileAccess.Read, FileShare.None))
				{
					var task = new SynchronousLinkDisableTask(winner, fixture);
					Assert.DoesNotThrow(() => task.Execute());
					Assert.That(task.Status, Is.EqualTo(Nexus.Client.BackgroundTasks.TaskStatus.Error));
					Assert.That(File.ReadAllText(fixture.Deployed("Locked.nif")), Is.EqualTo("winning mesh"));
					Assert.That(fixture.Activator.VirtualLinks.Count, Is.EqualTo(2));
				}
			}
		}

		/// <summary>Adjacent removals reuse their shifted position; an invalid hint still searches safely.</summary>
		[Test]
		public void LinkListRemoval_VerifiesAndReusesPositionWithoutRescanningUnrelatedLinks()
		{
			var links = new ThreadSafeObservableList<IVirtualModLink>();
			for (int index = 0; index < 128; index++)
				links.Add(new VirtualModLink("Other" + index, "Other" + index, 0, true, null, ModInstallRoot.Data));
			var first = new VirtualModLink("First", "First", 0, true, null, ModInstallRoot.Data);
			var second = new VirtualModLink("Second", "Second", 0, true, null, ModInstallRoot.Data);
			links.Add(first);
			links.Add(second);
			int comparisons = 0;
			var comparer = InterfaceStub<IEqualityComparer<IVirtualModLink>>.Create((method, args) =>
			{
				comparisons++;
				return ReferenceEquals(args[0], args[1]);
			});
			int hint = Int32.MaxValue;
			Assert.That(links.Remove(first, comparer, ref hint), Is.True);
			Assert.That(hint, Is.EqualTo(128));
			comparisons = 0;
			Assert.That(links.Remove(second, comparer, ref hint), Is.True);
			Assert.That(comparisons, Is.EqualTo(1));
			Assert.That(links.Count, Is.EqualTo(128));
			Assert.That(links.Remove(second, comparer, ref hint), Is.False);
			Assert.That(hint, Is.EqualTo(-1));
		}

		/// <summary>Reload reconciliation queries the authoritative root once per registration rather than per file.</summary>
		[Test]
		public void TopologyReconciliation_ReadsInstallRootOncePerOwner()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("RootBatch");
				fixture.Install(mod, "A.txt", "a");
				fixture.Install(mod, "B.txt", "b");
				int rootReads = 0;
				IInstallLog observed = InterfaceStub<IInstallLog>.Create((method, args) =>
				{
					if (method.Name == "GetModInstallRoot") rootReads++;
					return method.Invoke(fixture.InstallLog, args);
				});
				typeof(VirtualModActivator).GetProperty("ModInstallLog", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(fixture.Activator, observed, null);
				typeof(VirtualModActivator).GetMethod("ReconcileLoadedLinkInstallRoots", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(fixture.Activator, new object[] { fixture.Activator.VirtualLinks.ToList() });
				Assert.That(rootReads, Is.EqualTo(1));
			}
		}

		/// <summary>Committed fast reads must still expose pending registrations only within their transaction.</summary>
		[Test]
		public void RegistryReads_PreservePendingAndRolledBackInstallContext()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod committed = fixture.CreateMod("Committed");
				fixture.InstallLog.AddActiveMod(committed, ModInstallRoot.Data, ModInstallMethod.Virtual);
				string key = fixture.InstallLog.GetModKey(committed);
				Assert.That(key, Is.Not.Null);
				IMod pending = fixture.CreateMod("Pending");
				using (var transaction = new TransactionScope())
				{
					fixture.InstallLog.AddActiveMod(pending, ModInstallRoot.GameRoot, ModInstallMethod.Direct);
					Assert.That(fixture.InstallLog.GetModKey(committed), Is.EqualTo(key));
					Assert.That(fixture.InstallLog.GetModKey(pending), Is.Not.Null);
					Assert.That(fixture.InstallLog.GetModInstallRoot(pending), Is.EqualTo(ModInstallRoot.GameRoot));
					Assert.That(fixture.InstallLog.GetModInstallMethod(pending), Is.EqualTo(ModInstallMethod.Direct));
					Assert.That(fixture.InstallLog.GetModInstallMethod(fixture.InstallLog.GetModKey(pending)), Is.EqualTo(ModInstallMethod.Direct));
				}
				Assert.That(fixture.InstallLog.GetModKey(pending), Is.Null);
				Assert.That(fixture.InstallLog.GetModKey(committed), Is.EqualTo(key));
				Assert.That(fixture.InstallLog.GetModInstallRoot(committed), Is.EqualTo(ModInstallRoot.Data));
				Assert.That(fixture.InstallLog.GetModInstallMethod(key), Is.EqualTo(ModInstallMethod.Virtual));
			}
		}

		/// <summary>Equal path strings in different roots must not remove a sibling record or invoke its value comparison.</summary>
		[Test]
		public void LinkRemoval_UsesRecordIdentityAndRetainsMutationNotifications()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("EqualPaths");
				fixture.InstallLog.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Virtual);
				var info = new VirtualModInfo("10", "100", "EqualPaths", Path.GetFileName(mod.Filename), "1.0");
				var data = new VirtualModLink("SameSource", "Same.txt", 0, true, info, ModInstallRoot.Data);
				var game = new VirtualModLink("SameSource", "Same.txt", 0, true, info, ModInstallRoot.GameRoot);
				int comparisons = 0;
				IVirtualModLink observedData = InterfaceStub<IVirtualModLink>.Create((method, args) =>
				{
					if (method.Name == "Equals") comparisons++;
					return method.Invoke(data, args);
				});
				fixture.Activator.VirtualLinks.Add(observedData);
				fixture.Activator.VirtualLinks.Add(game);
				object removed = null;
				fixture.Activator.VirtualLinks.MutationOccurred += (sender, args) =>
				{
					if (args.OldItems != null) removed = args.OldItems[0];
				};
				typeof(VirtualModActivator).GetMethod("RemoveVirtualLink", BindingFlags.Instance | BindingFlags.NonPublic,
					null, new[] { typeof(IVirtualModLink), typeof(IMod) }, null).Invoke(fixture.Activator, new object[] { game, mod });
				Assert.That(removed, Is.SameAs(game));
				Assert.That(fixture.Activator.VirtualLinks.Single(), Is.SameAs(observedData));
				Assert.That(comparisons, Is.Zero, "Unrelated links must not compare their path strings during removal.");
			}
		}

		/// <summary>Legacy cleanup indexes active Nexus identities once instead of scanning all active mods for every stale registration.</summary>
		[TestCase(1)]
		[TestCase(32)]
		public void RemovalOwnerLookup_GroupsUnrelatedActiveCandidatesOnce(int registrationCount)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod original = fixture.CreateMod("UnrelatedCandidate");
				((InstallLog.DummyMod)original).Id = "10";
				int identityReads = 0;
				IMod observed = InterfaceStub<IMod>.Create((method, args) =>
				{
					if (method.Name == "get_Id") identityReads++;
					return method.Invoke(original, args);
				});
				fixture.InstallLog.AddActiveMod(observed, ModInstallRoot.Data, ModInstallMethod.Virtual);
				identityReads = 0;
				MethodInfo resolve = typeof(VirtualModActivator).GetMethod("FindManagedMod", BindingFlags.Instance | BindingFlags.NonPublic);
				using (var transaction = new TransactionScope())
				using (fixture.Activator.BeginVirtualRemovalOwnerLookup())
					for (int index = 0; index < registrationCount; index++)
						Assert.That(resolve.Invoke(fixture.Activator, new object[] {
							new VirtualModInfo("42", "", "Missing", "Missing" + index + ".zip", "1.0") }), Is.Null);
				Assert.That(identityReads, Is.LessThanOrEqualTo(2));
			}
		}

		/// <summary>Checks unrelated ownership once per registration regardless of how many active files it owns.</summary>
		[TestCase(1)]
		[TestCase(128)]
		public void ActiveLinksCheck_ResolvesUnrelatedMetadataOncePerCall(int linkCount)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod requested = fixture.CreateMod("Requested");
				IMod unrelated = fixture.CreateMod("Unrelated");
				fixture.InstallLog.AddActiveMod(requested, ModInstallRoot.Data, ModInstallMethod.Virtual);
				fixture.InstallLog.AddActiveMod(unrelated, ModInstallRoot.Data, ModInstallMethod.Virtual);
				var originalInfo = new VirtualModInfo("10", "100", "Unrelated", Path.GetFileName(unrelated.Filename), "1.0");
				int fileNameReads = 0;
				IVirtualModInfo observedInfo = InterfaceStub<IVirtualModInfo>.Create((method, args) =>
				{
					if (method.Name == "get_ModFileName") fileNameReads++;
					return method.Invoke(originalInfo, args);
				});
				for (int index = 0; index < linkCount; index++)
					fixture.Activator.VirtualLinks.Add(new VirtualModLink("Unrelated\\" + index, "File" + index, 0, true, observedInfo, ModInstallRoot.Data));

				Assert.That(fixture.Activator.CheckHasActiveLinks(requested), Is.False);
				Assert.That(fileNameReads, Is.LessThanOrEqualTo(8), "Owner resolution must not repeat for every unrelated file.");

				var requestedInfo = new VirtualModInfo("20", "200", "Requested", Path.GetFileName(requested.Filename), "1.0");
				var requestedLink = new VirtualModLink("Requested\\file", "Requested.txt", 0, false, requestedInfo, ModInstallRoot.Data);
				fixture.Activator.VirtualLinks.Add(requestedLink);
				Assert.That(fixture.Activator.CheckHasActiveLinks(requested), Is.False);
				requestedLink.Active = true;
				Assert.That(fixture.Activator.CheckHasActiveLinks(requested), Is.True);
				requestedLink.Active = false;
				Assert.That(fixture.Activator.CheckHasActiveLinks(requested), Is.False);
			}
		}

		/// <summary>Retains footprint-based legacy upgrade recovery when archive and download identities have changed.</summary>
		[Test]
		public void ActiveLinksCheck_PreservesLegacyUpgradeMatching()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("UpgradedArchive");
				((InstallLog.DummyMod)mod).Id = "42";
				((InstallLog.DummyMod)mod).DownloadId = "200";
				fixture.Install(mod, "Legacy.txt", "legacy");
				IVirtualModLink link = fixture.Activator.VirtualLinks.Single();
				link.ModInfo = new VirtualModInfo("42", "100", "PreviousArchive", "PreviousArchive.7z", "1.0");

				Assert.That(fixture.Activator.CheckHasActiveLinks(mod), Is.True);
				link.Active = false;
				Assert.That(fixture.Activator.CheckHasActiveLinks(mod), Is.False);
			}
		}

		/// <summary>Legacy upgrade ownership stays valid after removing the first file changes its observed footprint.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void NativeUninstall_LegacyUpgradeRemovesEveryFileAndPreservesSiblingMod(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod removed = fixture.CreateMod("UpgradedFiles");
				((InstallLog.DummyMod)removed).Id = "42";
				((InstallLog.DummyMod)removed).DownloadId = "200";
				foreach (string file in new[] { "First.txt", "Second.txt", "Third.txt" })
					fixture.Install(removed, file, file);
				IMod sibling = fixture.CreateMod("SiblingPatch");
				((InstallLog.DummyMod)sibling).Id = "42";
				fixture.Install(sibling, "Retained.txt", "retained");
				fixture.UseLegacyVirtualIdentity(removed);

				SynchronousUninstaller result = fixture.Uninstall(removed);

				Assert.That(result.Succeeded, Is.True, result.CompletionMessage);
				Assert.That(fixture.InstallLog.GetModKey(removed), Is.Null);
				foreach (string file in new[] { "First.txt", "Second.txt", "Third.txt" })
					Assert.That(File.Exists(fixture.Deployed(file)), Is.False, file);
				Assert.That(File.ReadAllText(fixture.Deployed("Retained.txt")), Is.EqualTo("retained"));
				Assert.That(fixture.Activator.GetVirtualTargetsForMod(sibling).Count, Is.EqualTo(1));
				Assert.That(fixture.Activator.VirtualLinks.Count, Is.EqualTo(1));
			}
		}

		/// <summary>Recovery comparisons scale with metadata registrations rather than target count and are discarded afterwards.</summary>
		[TestCase(2)]
		[TestCase(12)]
		public void DeploymentRemoval_ResolvesLegacyFootprintOncePerOperation(int fileCount)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod removed = fixture.CreateMod("LegacyBatch");
				((InstallLog.DummyMod)removed).Id = "42";
				((InstallLog.DummyMod)removed).DownloadId = "200";
				for (int index = 0; index < fileCount; index++)
					fixture.Install(removed, "File" + index + ".txt", "payload");
				fixture.UseLegacyVirtualIdentity(removed);
				IVirtualModInfo legacy = fixture.Activator.VirtualLinks.First().ModInfo;
				int footprintReads = 0;
				IInstallLog observedLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				{
					if (method.Name == "GetInstalledModFiles" && ReferenceEquals(args[0], removed)) footprintReads++;
					return method.Invoke(fixture.InstallLog, args);
				});
				typeof(VirtualModActivator).GetProperty("ModInstallLog", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(fixture.Activator, observedLog, null);
				using (var transaction = new TransactionScope())
				{
					IReadOnlyCollection<string> absent = fixture.Deployment.UninstallMixedMod(removed, new TxFileManager());
					Assert.That(absent.Count, Is.EqualTo(fileCount));
					Assert.That(footprintReads, Is.EqualTo(1), "Every target must reuse the recovered owner.");
					Assert.That(fixture.Activator.CheckHasActiveLinks(removed), Is.False);
					transaction.Complete();
				}

				// Later operations must use the current native registrations.
				fixture.InstallLog.RemoveMod(removed);
				IMod replacement = fixture.CreateMod("DifferentOwner");
				((InstallLog.DummyMod)replacement).Id = "42";
				fixture.Install(replacement, "Replacement.txt", "replacement");
				fixture.Activator.VirtualLinks.Single().ModInfo = legacy;
				Assert.That(fixture.Activator.CheckHasActiveLinks(replacement), Is.True);
				Assert.That(fixture.Activator.CheckHasActiveLinks(removed), Is.False);
			}
		}

		/// <summary>Transactional removal publishes one link-list reset while preserving unrelated native ownership.</summary>
		[Test]
		public void NativeUninstall_BatchesLinkNotificationsAndPreservesOtherMod()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod removed = fixture.CreateMod("RemovedBatch");
				fixture.Install(removed, "first.txt", "first");
				fixture.Install(removed, "second.txt", "second");
				IMod retained = fixture.CreateMod("RetainedBatch");
				fixture.Install(retained, "retained.txt", "retained");
				int notifications = 0;
				fixture.Activator.VirtualLinks.CollectionChanged += (sender, args) => notifications++;

				Assert.That(fixture.Uninstall(removed).Succeeded, Is.True);

				Assert.That(notifications, Is.EqualTo(1));
				Assert.That(fixture.Activator.GetVirtualTargetsForMod(removed), Is.Empty);
				Assert.That(fixture.Activator.GetVirtualTargetsForMod(retained).Count, Is.EqualTo(1));
			}
		}

		/// <summary>Checks incremental Collection verification against a full native capture after a real overwrite.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void CollectionStateRefresh_OverwriteMatchesFullCaptureAndReusesUnrelatedFiles(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod first = fixture.CreateMod("First");
				fixture.Install(first, "shared.txt", "old");
				IMod unrelated = fixture.CreateMod("Unrelated");
				fixture.Install(unrelated, "untouched.txt", "unchanged");
				CollectionNativeStateReader reader = CreateCollectionReader(fixture);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("incremental-overwrite");
				CollectionNativeStateIndex baseline = reader.Capture(target);
				Assert.That(baseline.Issues, Is.Empty);
				IMod incoming = fixture.CreateMod("Incoming");
				fixture.Install(incoming, "shared.txt", "new");
				fixture.Install(incoming, "added.txt", "added");
				fixture.InstallLog.AddIniEdit(incoming, "game.ini", "Display", "Mode", "new");
				fixture.InstallLog.AddGameSpecificValueEdit(incoming, "setting", new byte[] { 1, 2 });
				CollectionMemberEffectPreview preview = CreateCollectionPreview(fixture, incoming, root, "shared.txt", "added.txt");
				CollectionPerformanceSnapshot before = CollectionPerformanceMetrics.Capture();

				CollectionNativeStateIndex refreshed = reader.TryCaptureCommittedVirtualChild(target, baseline, baseline.Fingerprint, preview, null);

				Assert.That(refreshed, Is.Not.Null);
				CollectionPerformanceSnapshot after = CollectionPerformanceMetrics.Capture();
				Assert.That(after.NativeStateIndexBuildCount, Is.EqualTo(before.NativeStateIndexBuildCount));
				Assert.That(after.NativeStateIncrementalRefreshCount, Is.EqualTo(before.NativeStateIncrementalRefreshCount + 1));
				CollectionNativeStateIndex full = reader.Capture(target);
				Assert.That(refreshed.Fingerprint, Is.EqualTo(full.Fingerprint));
				ModDeploymentTarget untouched = ModDeploymentTargetResolver.Resolve(fixture.GameMode, unrelated, "untouched.txt", root);
				Assert.That(refreshed.Files[untouched], Is.SameAs(baseline.Files[untouched]));
				string unrelatedKey = fixture.InstallLog.GetModKey(unrelated);
				Assert.That(refreshed.FilesByOwnerKey[unrelatedKey], Is.SameAs(baseline.FilesByOwnerKey[unrelatedKey]));
				ModDeploymentTarget shared = preview.Files.First().Target;
				Assert.That(refreshed.Files[shared].EffectiveOwnerKey, Is.EqualTo(fixture.InstallLog.GetModKey(incoming)));
				Assert.That(refreshed.Files[shared].VirtualOwners.Count, Is.EqualTo(2));
				Assert.That(refreshed.IniEdits.Count, Is.EqualTo(1));
				Assert.That(refreshed.GameValues.Count, Is.EqualTo(1));
			}
		}

		/// <summary>Includes old-only and inactive ownership targets when a native upgrade removes a mod's previous files.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void CollectionStateRefresh_ReinstallRemovesOldTargetsAndMatchesFullCapture(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod previous = fixture.CreateMod("Previous");
				fixture.Install(previous, "removed.txt", "removed");
				fixture.Install(previous, "shared.txt", "old");
				IMod other = fixture.CreateMod("Other");
				fixture.Install(other, "shared.txt", "other");
				fixture.Install(other, "untouched.txt", "untouched");
				CollectionNativeStateReader reader = CreateCollectionReader(fixture);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("incremental-reinstall");
				CollectionNativeStateIndex baseline = reader.Capture(target);
				Assert.That(baseline.Issues, Is.Empty);
				CollectionNativeModState previousState = baseline.ModsByNativeKey[fixture.InstallLog.GetModKey(previous)];
				IMod incoming = fixture.CreateMod("Incoming");
				fixture.Upgrade(previous, incoming, "added.txt", "new");
				CollectionMemberEffectPreview preview = CreateCollectionPreview(fixture, incoming, root, "added.txt");

				CollectionNativeStateIndex refreshed = reader.TryCaptureCommittedVirtualChild(target, baseline, baseline.Fingerprint, preview, previousState);

				Assert.That(refreshed, Is.Not.Null);
				Assert.That(refreshed.Fingerprint, Is.EqualTo(reader.Capture(target).Fingerprint));
				Assert.That(refreshed.Files.ContainsKey(ModDeploymentTargetResolver.Resolve(fixture.GameMode, previous, "removed.txt", root)), Is.False);
				ModDeploymentTarget shared = ModDeploymentTargetResolver.Resolve(fixture.GameMode, other, "shared.txt", root);
				Assert.That(refreshed.Files[shared].VirtualOwners.Count, Is.EqualTo(1));
				Assert.That(refreshed.Files[shared].EffectiveOwnerKey, Is.EqualTo(fixture.InstallLog.GetModKey(other)));
			}
		}

		/// <summary>Retains full capture for newly promoted targets instead of treating mixed ownership as purely Virtual.</summary>
		[Test]
		public void CollectionStateRefresh_NewPromotionRequiresFullCapture()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				CollectionNativeStateReader reader = CreateCollectionReader(fixture);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("incremental-promotion");
				CollectionNativeStateIndex baseline = reader.Capture(target);
				IMod direct = fixture.CreateMod("Direct");
				ModDeploymentTarget fileTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "shared.txt");
				using (var scope = new TransactionScope())
				{
					fixture.InstallLog.AddActiveMod(direct, ModInstallRoot.Data, ModInstallMethod.Direct);
					fixture.Deployment.InstallDirectFile(direct, fileTarget, new byte[] { 1 }, new TxFileManager());
					scope.Complete();
				}
				CollectionMemberEffectPreview preview = CreateCollectionPreview(fixture, direct, ModInstallRoot.Data, "shared.txt");
				Assert.That(reader.TryCaptureCommittedVirtualChild(target, baseline, baseline.Fingerprint, preview, null), Is.Null);
			}
		}

		/// <summary>Uses complete capture when an indexed path also represents ownership under another deployment root.</summary>
		[Test]
		public void CollectionStateRefresh_PhysicalAliasRequiresFullCapture()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod original = fixture.CreateMod("Original");
				fixture.Install(original, "shared.txt", "original");
				CollectionNativeStateReader reader = CreateCollectionReader(fixture);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("incremental-alias");
				CollectionNativeStateIndex baseline = reader.Capture(target);
				IMod incoming = fixture.CreateMod("Incoming");
				fixture.InstallLog.AddActiveMod(incoming, ModInstallRoot.GameRoot, ModInstallMethod.Virtual);
				string source = fixture.Staged(incoming, @"Data\shared.txt");
				Directory.CreateDirectory(Path.GetDirectoryName(source));
				File.WriteAllText(source, "new");
				fixture.InstallLog.AddDataFile(incoming, source);
				fixture.Activator.GetModLinkInstaller().AddFileLink(incoming, @"Data\shared.txt", source, true, false, ModInstallRoot.GameRoot);
				fixture.Activator.SaveList(false);
				CollectionMemberEffectPreview preview = CreateCollectionPreview(fixture, incoming, ModInstallRoot.GameRoot, @"Data\shared.txt");

				Assert.That(reader.TryCaptureCommittedVirtualChild(target, baseline, baseline.Fingerprint, preview, null), Is.Null);
			}
		}

		/// <summary>Uses the real committed ownership services with complete, empty Collection and plugin stores.</summary>
		private static CollectionNativeStateReader CreateCollectionReader(RemovalFixture fixture)
		{
			var store = new CollectionsStore(Path.GetDirectoryName(fixture.ModPath));
			store.CreateNew();
			IPluginManager plugins = InterfaceStub<IPluginManager>.Create((method, args) => method.Name == "get_CurrentSnapshot"
				? new PluginSnapshot(new PluginSnapshotEntry[0], new PluginValidationDiagnostic[0]) : null);
			return new CollectionNativeStateReader(fixture.InstallLog, fixture.Activator, plugins, fixture.GameMode,
				new CollectionsAssociationStore(store));
		}

		/// <summary>Describes the exact deployment targets written by the native fixture.</summary>
		private static CollectionMemberEffectPreview CreateCollectionPreview(RemovalFixture fixture, IMod mod, ModInstallRoot root, params string[] files)
		{
			return new CollectionMemberEffectPreview(CollectionMemberKey.FromValidatedMatch("refresh-member"),
				CollectionRecipeIdentity.FromFingerprint("refresh-recipe"), ModInstallMethod.Virtual, root,
				files.Select(file => new CollectionPlannedFileEffect(ModDeploymentTargetResolver.Resolve(fixture.GameMode, mod, file, root))),
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0],
				new CollectionEffectPreviewIssue[0]);
		}

		/// <summary>Exercises the real Virtual switch using Collection-native keys and the persisted staged source.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void ReviewedWinner_UsesInstallLogKeysAndRetainsBothOwners(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod settings = fixture.CreateMod("BundledSettings");
				IMod package = fixture.CreateMod("PreloaderPackage");
				fixture.Install(settings, "xSE PluginPreloader.xml", "settings");
				fixture.Install(package, "xSE PluginPreloader.xml", "defaults");
				string settingsKey = fixture.InstallLog.GetModKey(settings);
				string packageKey = fixture.InstallLog.GetModKey(package);
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, settings, "xSE PluginPreloader.xml", root);
				IVirtualModLink settingsLink = fixture.Activator.VirtualLinks.Single(x => x.ModInfo.ModFileName == Path.GetFileName(settings.Filename));
				Assert.AreNotEqual(FileManagerQueryService.CreateOwnerKey(settingsLink.ModInfo), settingsKey);
				var service = new VirtualDeploymentService(fixture.Activator, fixture.Deployment);

				VirtualFileOwnerSwitchResult result = service.SwitchFileOwner(target, settingsKey);

				Assert.IsTrue(result.Success, result.FailureMessage);
				Assert.AreEqual(settingsKey, result.SelectedOwnerKey);
				Assert.AreEqual("settings", File.ReadAllText(fixture.Deployed("xSE PluginPreloader.xml")));
				CollectionAssert.AreEqual(new[] { packageKey, settingsKey }, fixture.Activator.GetVirtualOwnerKeys(target));
				Assert.IsTrue(service.SwitchFileOwner(target, settingsKey).Success);
				Assert.IsTrue(fixture.Uninstall(settings).Succeeded);
				Assert.AreEqual("defaults", File.ReadAllText(fixture.Deployed("xSE PluginPreloader.xml")));
				Assert.IsTrue(fixture.Uninstall(package).Succeeded);
				Assert.IsFalse(File.Exists(fixture.Deployed("xSE PluginPreloader.xml")));
			}
		}

		/// <summary>Repairs legacy/raw-path records that mark two sources active for the same physical destination.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void ReviewedWinner_RepairsMultipleActiveOwnersEvenWhenSelectedOwnerIsFirst(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod selected = fixture.CreateMod("Selected");
				IMod previous = fixture.CreateMod("Previous");
				fixture.Install(selected, "Shared.xml", "selected");
				fixture.Install(previous, "Shared.xml", "previous");
				foreach (IVirtualModLink link in fixture.Activator.VirtualLinks) { link.Active = true; link.Priority = 0; }
				IVirtualModLink selectedLink = fixture.Activator.VirtualLinks.Single(x => x.ModInfo.ModFileName == Path.GetFileName(selected.Filename));
				// Match the reported case: the selected source is the first active candidate, but its bytes are not deployed.
				fixture.Activator.VirtualLinks.Remove(selectedLink);
				fixture.Activator.VirtualLinks.Insert(0, selectedLink);
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, selected, "Shared.xml", root);

				VirtualFileOwnerSwitchResult result = fixture.Activator.SwitchFileOwner(target, fixture.InstallLog.GetModKey(selected));

				Assert.IsTrue(result.Success, result.FailureMessage);
				Assert.AreEqual("selected", File.ReadAllText(fixture.Deployed("Shared.xml")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count(x => x.Active));
				CollectionAssert.AreEqual(new[] { fixture.InstallLog.GetModKey(previous), fixture.InstallLog.GetModKey(selected) },
					fixture.Activator.GetVirtualOwnerKeys(target));
				Assert.IsTrue(fixture.Uninstall(selected).Succeeded);
				Assert.AreEqual("previous", File.ReadAllText(fixture.Deployed("Shared.xml")));
			}
		}

		/// <summary>Does not change fallback order when several owners have equal or inconsistent legacy priorities.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void ReviewedWinner_PreservesObservedFallbackOrder(bool multipleActive)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod[] mods = { fixture.CreateMod("Zulu"), fixture.CreateMod("Alpha"), fixture.CreateMod("Middle"), fixture.CreateMod("Last") };
				foreach (IMod mod in mods) fixture.Install(mod, "Shared.xml", mod.ModName);
				foreach (IVirtualModLink link in fixture.Activator.VirtualLinks)
				{
					link.Priority = 0;
					link.Active = multipleActive || link.ModInfo.ModFileName == Path.GetFileName(mods[3].Filename);
				}
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, mods[0], "Shared.xml", ModInstallRoot.Data);
				string selectedKey = fixture.InstallLog.GetModKey(mods[0]);
				string[] expected = fixture.Activator.GetVirtualOwnerKeys(target).Where(key => key != selectedKey).Concat(new[] { selectedKey }).ToArray();

				VirtualFileOwnerSwitchResult result = fixture.Activator.SwitchFileOwner(target, selectedKey);

				Assert.IsTrue(result.Success, result.FailureMessage);
				Assert.AreEqual(mods[0].ModName, File.ReadAllText(fixture.Deployed("Shared.xml")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count(x => x.Active));
				CollectionAssert.AreEqual(expected, fixture.Activator.GetVirtualOwnerKeys(target));
			}
		}

		/// <summary>Retains File Manager archive/download keys at the legacy Data-only entry point.</summary>
		[Test]
		public void FileManagerWinner_StillAcceptsAndReturnsLegacyOwnerKey()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod first = fixture.CreateMod("FirstOwner");
				IMod second = fixture.CreateMod("SecondOwner");
				fixture.Install(first, "Shared.xml", "first");
				fixture.Install(second, "Shared.xml", "second");
				IVirtualModLink selected = fixture.Activator.VirtualLinks.Single(x => x.ModInfo.ModFileName == Path.GetFileName(first.Filename));
				string legacyKey = FileManagerQueryService.CreateOwnerKey(selected.ModInfo);

				VirtualFileOwnerSwitchResult result = fixture.Activator.SwitchFileOwner("Shared.xml", legacyKey);

				Assert.IsTrue(result.Success, result.FailureMessage);
				Assert.AreEqual(legacyKey, result.SelectedOwnerKey);
				Assert.AreEqual("first", File.ReadAllText(fixture.Deployed("Shared.xml")));
			}
		}

		/// <summary>Uninstalls an existing Direct mod through the native task without requiring a Virtual link target.</summary>
		[TestCase(ModInstallRoot.Data, false)]
		[TestCase(ModInstallRoot.Data, true)]
		[TestCase(ModInstallRoot.GameRoot, false)]
		[TestCase(ModInstallRoot.GameRoot, true)]
		public void ExistingDirectMod_NativeUninstallRemovesFilesAndRestoresOriginal(ModInstallRoot root, bool original)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod mod = fixture.CreateMod("ExistingDirect");
				fixture.InstallLog.AddActiveMod(mod, root, ModInstallMethod.Direct);
				string key = fixture.InstallLog.GetModKey(mod);
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, mod, "Existing.esp", root);
				string destination = fixture.Deployed("Existing.esp");
				if (original) File.WriteAllText(destination, "original");
				using (var scope = new TransactionScope())
				{
					fixture.Deployment.InstallDirectFile(mod, target, System.Text.Encoding.UTF8.GetBytes("direct-mod"), new TxFileManager());
					scope.Complete();
				}
				Assert.IsEmpty(fixture.Activator.VirtualLinks);
				Assert.AreEqual(ModInstallMethod.Direct, fixture.InstallLog.GetModInstallMethod(mod));

				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);

				Assert.IsNull(fixture.InstallLog.GetModKey(mod));
				Assert.IsEmpty(fixture.InstallLog.GetDeploymentTargetsForMod(key));
				if (original) Assert.AreEqual("original", File.ReadAllText(destination));
				else Assert.IsFalse(File.Exists(destination));
			}
		}

		/// <summary>Preserves the previous Virtual source when an overwrite is rolled back after winner deactivation.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void VirtualOverwrite_RollbackRestoresPreviousSourceAfterOwnerIsDeactivated(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root, false))
			{
				IMod previous = fixture.CreateMod("PreviousOwner");
				IMod incoming = fixture.CreateMod("IncomingOwner");
				fixture.Install(previous, "Shared.esp", "previous");
				fixture.InstallLog.AddActiveMod(incoming, root, ModInstallMethod.Virtual);
				string source = fixture.Staged(incoming, "Shared.esp");
				Directory.CreateDirectory(Path.GetDirectoryName(source));
				File.WriteAllText(source, "incoming");
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, previous, "Shared.esp", root);
				using (var scope = new TransactionScope())
				{
					fixture.Activator.DetachVirtualLinkWithoutFallback(target, fixture.InstallLog.GetModKey(previous), new TxFileManager());
					fixture.Activator.DeploySpecificVirtualLink(target, fixture.InstallLog.GetModKey(previous), new TxFileManager());
					fixture.Activator.UpdateLinkListPriority(fixture.Activator.VirtualLinks.ToList());
					fixture.Activator.AddFileLink(incoming, "Shared.esp", source, false, false, false, 0, root);
					Assert.AreEqual("incoming", File.ReadAllText(fixture.Deployed("Shared.esp")));
				}

				Assert.AreEqual("previous", File.ReadAllText(fixture.Deployed("Shared.esp")));
				Assert.IsTrue(new TxFileManager { TxEnabled = false }.IsSameFile(
					fixture.Deployed("Shared.esp"), fixture.Staged(previous, "Shared.esp")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Uninstall(previous).Succeeded);
				Assert.IsFalse(File.Exists(fixture.Deployed("Shared.esp")));
			}
		}

		[TestCase(ModInstallRoot.Data, false)]
		[TestCase(ModInstallRoot.Data, true)]
		[TestCase(ModInstallRoot.GameRoot, false)]
		[TestCase(ModInstallRoot.GameRoot, true)]
		public void RevisionRoundTrip_ThenNativeUninstall_RemovesCollectionFilesAndRestoresOnlyOriginal(ModInstallRoot root, bool original)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod first = fixture.CreateMod("Revision5");
				IMod second = fixture.CreateMod("Revision6");
				string destination = fixture.Deployed("Collection.esp");
				if (original) File.WriteAllText(destination, "pre-existing");
				fixture.Install(first, "Collection.esp", "rev5");
				fixture.Upgrade(first, second, "Collection.esp", "rev6");
				fixture.Upgrade(second, first, "Collection.esp", "rev5-again");

				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count, "The mod must not retain its own predecessors as fallback owners.");
				Assert.AreEqual("rev5-again", File.ReadAllText(destination));
				Assert.IsTrue(fixture.Uninstall(first).Succeeded);
				Assert.IsEmpty(fixture.Activator.VirtualLinks);
				Assert.IsNull(fixture.InstallLog.GetModKey(first));
				if (original) Assert.AreEqual("pre-existing", File.ReadAllText(destination));
				else Assert.IsFalse(File.Exists(destination));
			}
		}

		/// <summary>Removes the obsolete Data deployment and restores originals when uninstalling from the new root.</summary>
		[TestCase(false, false)]
		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public void SameArchiveFolderCorrection_CleansDataAndUninstallsFromRecordedGameRoot(bool dataOriginal, bool gameOriginal)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("Preloader");
				string data = fixture.Deployed("WinHTTP.dll");
				string game = Path.Combine(fixture.GameMode.InstallationPath, "WinHTTP.dll");
				if (dataOriginal) File.WriteAllText(data, "data-original");
				if (gameOriginal) File.WriteAllText(game, "game-original");
				fixture.Install(mod, "WinHTTP.dll", "preloader");
				string key = fixture.InstallLog.GetModKey(mod);
				fixture.Upgrade(mod, mod, "WinHTTP.dll", "preloader", replacementRoot: ModInstallRoot.GameRoot);
				Assert.AreEqual(key, fixture.InstallLog.GetModKey(mod));
				Assert.AreEqual(ModInstallRoot.GameRoot, fixture.InstallLog.GetModInstallRoot(mod));
				Assert.AreEqual(ModInstallMethod.Virtual, fixture.InstallLog.GetModInstallMethod(mod));
				Assert.AreEqual("preloader", File.ReadAllText(game));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Activator.VirtualLinks.All(x => x.InstallRoot == ModInstallRoot.GameRoot));
				if (dataOriginal) Assert.AreEqual("data-original", File.ReadAllText(data));
				else Assert.IsFalse(File.Exists(data));
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				if (gameOriginal) Assert.AreEqual("game-original", File.ReadAllText(game));
				else Assert.IsFalse(File.Exists(game));
				if (dataOriginal) Assert.AreEqual("data-original", File.ReadAllText(data));
				else Assert.IsFalse(File.Exists(data));
			}
		}

		/// <summary>Restores legacy inactive payloads using the installed ID after archive metadata changes.</summary>
		[TestCase(ModInstallRoot.Data, true)]
		[TestCase(ModInstallRoot.GameRoot, true)]
		[TestCase(ModInstallRoot.Data, false)]
		[TestCase(ModInstallRoot.GameRoot, false)]
		public void NativeUninstall_LegacyInactiveSourceUsesInstalledDownloadId(ModInstallRoot root, bool sourcePresent)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod previous = fixture.CreateMod("PreviousArchive");
				((InstallLog.DummyMod)previous).DownloadId = "140620";
				fixture.Install(previous, "Shared.txt", "previous");
				IMod winner = fixture.CreateMod("Winner");
				fixture.Install(winner, "Shared.txt", "winner");
				string legacySource = Path.Combine(fixture.Activator.VirtualPath, "140620", "Shared.txt");
				Directory.CreateDirectory(Path.GetDirectoryName(legacySource));
				File.Move(fixture.Staged(previous, "Shared.txt"), legacySource);
				((InstallLog.DummyMod)previous).DownloadId = "146240";
				if (!sourcePresent)
					File.Delete(legacySource);
				// Another download's payload must never substitute for this owner's missing file.
				string unrelatedSource = Path.Combine(fixture.Activator.VirtualPath, "999999", "Shared.txt");
				Directory.CreateDirectory(Path.GetDirectoryName(unrelatedSource));
				File.WriteAllText(unrelatedSource, "unrelated");

				SynchronousUninstaller result = fixture.Uninstall(winner);

				Assert.That(result.Succeeded, Is.EqualTo(sourcePresent), result.CompletionMessage);
				Assert.That(File.ReadAllText(fixture.Deployed("Shared.txt")), Is.EqualTo(sourcePresent ? "previous" : "winner"));
				Assert.That(fixture.InstallLog.ActiveMods.Contains(previous), Is.True);
				Assert.That(fixture.InstallLog.ActiveMods.Contains(winner), Is.EqualTo(!sourcePresent));
				if (sourcePresent)
				{
					ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, previous, "Shared.txt", root);
					Assert.That(fixture.Activator.GetVirtualSourceForOwner(target, fixture.InstallLog.GetModKey(previous)), Is.EqualTo(legacySource));
					Assert.That(fixture.Uninstall(previous).Succeeded, Is.True);
				}
			}
		}

		/// <summary>Retains an exact recorded source when archive metadata changes and an older ID directory also exists.</summary>
		[Test]
		public void NativeUninstall_ExactRecordedSourceTakesPriorityOverLegacyDownloadId()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod previous = fixture.CreateMod("RecordedArchive");
				((InstallLog.DummyMod)previous).DownloadId = "140620";
				fixture.Install(previous, "Shared.txt", "recorded");
				IMod winner = fixture.CreateMod("Winner");
				fixture.Install(winner, "Shared.txt", "winner");
				((InstallLog.DummyMod)previous).DownloadId = "146240";
				string legacySource = Path.Combine(fixture.Activator.VirtualPath, "140620", "Shared.txt");
				Directory.CreateDirectory(Path.GetDirectoryName(legacySource));
				File.WriteAllText(legacySource, "older");

				Assert.That(fixture.Uninstall(winner).Succeeded, Is.True);
				Assert.That(File.ReadAllText(fixture.Deployed("Shared.txt")), Is.EqualTo("recorded"));
			}
		}

		/// <summary>Existing game-folder installs retain access to originals saved in the older shared backup directory.</summary>
		[Test]
		public void NativeUninstall_RestoresLegacyGameRootBackup()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.GameRoot))
			{
				IMod mod = fixture.CreateMod("LegacyRoot");
				File.WriteAllText(fixture.Deployed("Original.dll"), "original");
				fixture.Install(mod, "Original.dll", "mod");
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, mod, "Original.dll", ModInstallRoot.GameRoot);
				string backup = fixture.Activator.GetVirtualOverwritePath(target, fixture.InstallLog.GetModKey(mod));
				Assert.That(File.Exists(backup), Is.True);
				string legacy = Path.Combine(fixture.Activator.VirtualPath, "_overwrites", "LegacyRoot", "Original.dll");
				Directory.CreateDirectory(Path.GetDirectoryName(legacy));
				File.Move(backup, legacy);
				Assert.That(fixture.Uninstall(mod).Succeeded, Is.True);
				Assert.That(File.ReadAllText(fixture.Deployed("Original.dll")), Is.EqualTo("original"));
			}
		}

		/// <summary>Restores the old native root, file bytes and ownership when the folder correction rolls back.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void SameArchiveFolderCorrection_RollbackRestoresDataRootFilesAndOwnership(bool gameOriginal)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("PreloaderRollback");
				fixture.Install(mod, "WinHTTP.dll", "old-preloader");
				string key = fixture.InstallLog.GetModKey(mod);
				string game = Path.Combine(fixture.GameMode.InstallationPath, "WinHTTP.dll");
				if (gameOriginal) File.WriteAllText(game, "game-original");
				fixture.Upgrade(mod, mod, "WinHTTP.dll", "new-preloader", false, replacementRoot: ModInstallRoot.GameRoot);
				Assert.AreEqual(key, fixture.InstallLog.GetModKey(mod));
				Assert.AreEqual(ModInstallRoot.Data, fixture.InstallLog.GetModInstallRoot(mod));
				Assert.AreEqual("old-preloader", File.ReadAllText(fixture.Deployed("WinHTTP.dll")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Activator.VirtualLinks.All(x => x.InstallRoot == ModInstallRoot.Data));
				if (gameOriginal) Assert.AreEqual("game-original", File.ReadAllText(game));
				else Assert.IsFalse(File.Exists(game));
			}
		}

		[Test]
		public void SameArchiveReinstall_KeepsReinstalledStreamAndRemovesObsoletePlugin()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("SameArchive");
				fixture.Install(mod, "Keep.esp", "old");
				fixture.Install(mod, "Removed.esp", "obsolete");
				fixture.Upgrade(mod, mod, "Keep.esp", "new");

				Assert.IsTrue(File.Exists(fixture.Staged(mod, "Keep.esp")));
				Assert.AreEqual("new", File.ReadAllText(fixture.Deployed("Keep.esp")));
				Assert.IsFalse(File.Exists(fixture.Deployed("Removed.esp")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				CollectionAssert.Contains(fixture.RemovedPlugins, fixture.Deployed("Removed.esp"));
			}
		}

		[TestCase(false)]
		[TestCase(true)]
		public void PreparedRecipe_ReusedStagingSurvivesWhileObsoleteFilesAreRemoved(bool generated)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("ReusedRecipe");
				fixture.Install(mod, "Reused.esp", "cached");
				fixture.Install(mod, "Obsolete.esp", "obsolete");
				fixture.Upgrade(mod, mod, "Reused.esp", "cached", true, generated);
				Assert.AreEqual("cached", File.ReadAllText(fixture.Staged(mod, "Reused.esp")));
				Assert.AreEqual("cached", File.ReadAllText(fixture.Deployed("Reused.esp")));
				Assert.IsFalse(File.Exists(fixture.Deployed("Obsolete.esp")));
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsFalse(File.Exists(fixture.Deployed("Reused.esp")));
			}
		}

		[Test]
		public void ChangedArchiveUpgrade_RemovesObsoleteDeploymentBeforeRebindingOwner()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod oldMod = fixture.CreateMod("OldArchive");
				IMod newMod = fixture.CreateMod("NewArchive");
				fixture.Install(oldMod, "Keep.esp", "old");
				fixture.Install(oldMod, "Removed.esp", "obsolete");
				fixture.Upgrade(oldMod, newMod, "Keep.esp", "new");

				Assert.IsFalse(File.Exists(fixture.Deployed("Removed.esp")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Uninstall(newMod).Succeeded);
				Assert.IsFalse(File.Exists(fixture.Deployed("Keep.esp")));
			}
		}

		[Test]
		public void ExistingDuplicateOwnerRecords_NativeUninstallRemovesAllRecordsAndPhysicalFile()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("LegacyDuplicates");
				fixture.Install(mod, "Duplicate.esp", "mod");
				fixture.Activator.VirtualLinks[0].Active = false;
				fixture.Activator.AddFileLink(mod, "Duplicate.esp", fixture.Staged(mod, "Duplicate.esp"), false, false, false, 0, ModInstallRoot.Data);
				Assert.AreEqual(2, fixture.Activator.VirtualLinks.Count);

				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsEmpty(fixture.Activator.VirtualLinks);
				Assert.IsFalse(File.Exists(fixture.Deployed("Duplicate.esp")));
			}
		}

		[Test]
		public void DanglingVirtualLink_NativeUninstallRemovesTheFilesystemEntry()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("Dangling");
				fixture.Install(mod, "Dangling.esp", "mod");
				string destination = fixture.Deployed("Dangling.esp");
				string source = fixture.Staged(mod, "Dangling.esp");
				File.Delete(destination);
				var files = new TxFileManager { TxEnabled = false };
				try
				{
					if (!files.CreateSymbolicLink(destination, source))
						Assert.Ignore("Symbolic links are not available in this test environment.");
				}
				catch (IOException exception) when (exception.InnerException is System.ComponentModel.Win32Exception &&
					((System.ComponentModel.Win32Exception)exception.InnerException).NativeErrorCode == 1314)
				{
					Assert.Ignore("Creating symbolic links requires an unavailable privilege.");
				}
				File.Delete(source);
				Assert.IsFalse(File.Exists(destination));
				Assert.AreEqual(FileEntryKind.SymbolicLink, files.GetFileEntryKind(destination, source));
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.AreEqual(FileEntryKind.Absent, files.GetFileEntryKind(destination, source));
			}
		}

		[Test]
		public void AbortedReplacement_RestoresPreviousOwnerContentAndOriginalBackup()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod oldMod = fixture.CreateMod("BeforeAbort");
				IMod newMod = fixture.CreateMod("AfterAbort");
				File.WriteAllText(fixture.Deployed("Rollback.esp"), "original");
				fixture.Install(oldMod, "Rollback.esp", "old");
				fixture.Upgrade(oldMod, newMod, "Rollback.esp", "new", false);

				Assert.AreEqual("old", File.ReadAllText(fixture.Deployed("Rollback.esp")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.AreEqual(Path.GetFileName(oldMod.Filename), fixture.Activator.VirtualLinks[0].ModInfo.ModFileName);
				Assert.IsTrue(fixture.Uninstall(oldMod).Succeeded);
				Assert.AreEqual("original", File.ReadAllText(fixture.Deployed("Rollback.esp")));
			}
		}

		[Test]
		public void AbortedSameArchiveHardlinkReplacement_RestoresOriginalBytesAndTopology()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("HardlinkRollback");
				fixture.Install(mod, "Rollback.esp", "old");
				string destination = fixture.Deployed("Rollback.esp");
				string source = fixture.Staged(mod, "Rollback.esp");
				File.Delete(destination);
				var files = new TxFileManager { TxEnabled = false };
				if (!files.CreateHardLink(destination, source))
					Assert.Ignore("Hardlinks are not available in this test environment.");
				fixture.Upgrade(mod, mod, "Rollback.esp", "new", false);
				Assert.AreEqual("old", File.ReadAllText(source));
				Assert.AreEqual("old", File.ReadAllText(destination));
				Assert.AreEqual(FileEntryKind.HardLink, files.GetFileEntryKind(destination, source));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsFalse(File.Exists(destination));
			}
		}

		[TestCase(false)]
		[TestCase(true)]
		public void LegacyFileRemoval_ProtectedFileFailsWithoutDroppingOwnership(bool readOnly)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("Protected");
				fixture.InstallLog.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Virtual);
				fixture.InstallLog.AddDataFile(mod, "Protected.esp");
				string path = fixture.Deployed("Protected.esp");
				File.WriteAllText(path, "keep tracking");
				try
				{
					if (readOnly) File.SetAttributes(path, FileAttributes.ReadOnly);
					using (FileStream locked = readOnly ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
					using (var scope = new TransactionScope())
						Assert.Throws<IOException>(() => fixture.LegacyInstaller(mod).UninstallDataFile("Protected.esp"));
					CollectionAssert.Contains(fixture.InstallLog.GetInstalledModFiles(mod), "Protected.esp");
					Assert.IsTrue(File.Exists(path));
				}
				finally { File.SetAttributes(path, FileAttributes.Normal); }
			}
		}

		[Test]
		public void LegacyBackupRestoreFailure_RollsBackRemovedFileAndPreservesBackupAndOwnership()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("RestoreFailure");
				fixture.InstallLog.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Virtual);
				using (var setup = new TransactionScope())
				{
					fixture.InstallLog.LogOriginalDataFile("Restore.esp");
					fixture.InstallLog.AddDataFile(mod, "Restore.esp");
					setup.Complete();
				}
				string path = fixture.Deployed("Restore.esp");
				string backup = Path.Combine(fixture.OverwritePath, fixture.InstallLog.OriginalValuesKey + "_Restore.esp");
				File.WriteAllText(path, "mod");
				File.WriteAllText(backup, "original");
				using (var locked = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.None))
				using (var scope = new TransactionScope())
					Assert.Catch<Exception>(() => fixture.LegacyInstaller(mod).UninstallDataFile("Restore.esp"));

				Assert.AreEqual("mod", File.ReadAllText(path));
				Assert.AreEqual("original", File.ReadAllText(backup));
				CollectionAssert.Contains(fixture.InstallLog.GetInstalledModFiles(mod), "Restore.esp");
			}
		}

		[Test]
		public void NativeUninstall_ReportsLockedFileAndKeepsRegistrationForRetry()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("NativeLocked");
				fixture.InstallLog.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Virtual);
				fixture.InstallLog.AddDataFile(mod, "Locked.esp");
				string path = fixture.Deployed("Locked.esp");
				File.WriteAllText(path, "locked");
				using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					SynchronousUninstaller result = fixture.Uninstall(mod);
					Assert.IsFalse(result.Succeeded);
					StringAssert.Contains("Locked.esp", result.CompletionMessage);
					Assert.IsNotNull(fixture.InstallLog.GetModKey(mod));
					CollectionAssert.Contains(fixture.InstallLog.GetInstalledModFiles(mod), "Locked.esp");
				}
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsFalse(File.Exists(path));
			}
		}

		/// <summary>Native removal prunes the empty file ancestors but preserves the deployment root and unrelated empty folders.</summary>
		[Test]
		public void NativeUninstall_RemovesEmptyDeploymentAncestorsOnly()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("NestedDirectories");
				fixture.Install(mod, @"Meshes\Example\part.nif", "mesh");
				string unrelated = Path.Combine(fixture.DataPath, "Unrelated");
				Directory.CreateDirectory(unrelated);
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsFalse(Directory.Exists(Path.Combine(fixture.DataPath, "Meshes")));
				Assert.IsTrue(Directory.Exists(fixture.DataPath));
				Assert.IsTrue(Directory.Exists(unrelated));
			}
		}

		/// <summary>Shared folders keep remaining content after the mod's nested file is removed.</summary>
		[Test]
		public void NativeUninstall_PreservesNonemptyDeploymentAncestors()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("SharedDirectories");
				fixture.Install(mod, @"Meshes\Example\part.nif", "mesh");
				string otherFile = Path.Combine(fixture.DataPath, "Meshes", "Example", "keep.nif");
				File.WriteAllText(otherFile, "unmanaged");
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.AreEqual("unmanaged", File.ReadAllText(otherFile));
			}
		}

		/// <summary>Cleanup ignores removed paths outside the configured root, including siblings with a matching name prefix.</summary>
		[Test]
		public void EmptyDirectoryCleanup_OutsideRootAndUnrelatedDirectoriesArePreserved()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				string sibling = fixture.DataPath + "Other";
				Directory.CreateDirectory(sibling);
				string unrelated = Path.Combine(fixture.DataPath, "Unrelated");
				Directory.CreateDirectory(unrelated);
				UninstalledModDirectoryCleanup.RemoveEmptyDirectories(fixture.DataPath,
					new[] { Path.Combine(sibling, "removed.nif"), Path.Combine(fixture.DataPath, "..", "outside.nif") });
				Assert.IsTrue(Directory.Exists(sibling));
				Assert.IsTrue(Directory.Exists(unrelated));
				Assert.IsTrue(Directory.Exists(fixture.DataPath));
			}
		}

		/// <summary>Cleanup never follows a linked ancestor to remove an empty directory in another tree.</summary>
		[Test]
		public void EmptyDirectoryCleanup_LinkedAncestorPreservesExternalDirectory()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				string external = fixture.DataPath + "External";
				Directory.CreateDirectory(Path.Combine(external, "Nested"));
				string link = Path.Combine(fixture.DataPath, "Linked");
				if (!CreateDirectoryLink(link, external, 3) && !CreateDirectoryLink(link, external, 1))
					Assert.Ignore("Directory symbolic links are not available in this test environment.");
				try
				{
					UninstalledModDirectoryCleanup.RemoveEmptyDirectories(fixture.DataPath,
						new[] { Path.Combine(link, "Nested", "removed.nif") });
					Assert.IsTrue(Directory.Exists(Path.Combine(external, "Nested")));
					Assert.IsTrue(Directory.Exists(link));
				}
				finally { Directory.Delete(link, false); }
			}
		}

		/// <summary>Creates a directory symbolic link solely within the isolated regression fixture.</summary>
		[DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool CreateDirectoryLink(string link, string target, int flags);

		/// <summary>
		/// Provides isolated filesystem roots and real native deployment services without a running main form.
		/// </summary>
		private sealed class RemovalFixture : IDisposable
		{
			private readonly string m_root = Path.Combine(Path.GetTempPath(), "NMM-removal-regression-" + Guid.NewGuid().ToString("N"));
			private readonly ThreadSafeObservableList<IMod> m_mods;
			private readonly ModInstallRoot m_installRoot;
			private readonly IGameModeEnvironmentInfo m_gameInfo;
			/// <summary>
			/// Creates an isolated native deployment environment for the requested install root.
			/// </summary>
			public RemovalFixture(ModInstallRoot root, bool realFiles = true)
			{
				m_installRoot = root;
				DataPath = Path.Combine(m_root, "Game", "Data");
				string gamePath = Path.Combine(m_root, "Game");
				string installInfo = Path.Combine(m_root, "InstallInfo");
				OverwritePath = Path.Combine(installInfo, "overwrites");
				ModPath = Path.Combine(m_root, "Mods");
				foreach (string path in new[] { DataPath, installInfo, OverwritePath, ModPath }) Directory.CreateDirectory(path);
				m_gameInfo = InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_InstallationPath": return DataPath;
						case "get_InstallInfoDirectory": return installInfo;
						case "get_OverwriteDirectory": return OverwritePath;
						default: return null;
					}
				});
				GameMode = InterfaceStub<IGameMode>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_ModeId": return "fallout4";
						case "get_GameModeEnvironmentInfo": return m_gameInfo;
						case "get_InstallationPath": return gamePath;
						case "get_PluginDirectory": return DataPath;
						case "get_UsesPlugins": return true;
						case "RealFileRequired": return realFiles;
						case "GetModFormatAdjustedPath": return args[1];
						default: return null;
					}
				});
				var folders = new PerGameModeSettings<string>(); folders["fallout4"] = Path.Combine(m_root, "Virtual");
				var linkFolders = new PerGameModeSettings<string>(); linkFolders["fallout4"] = String.Empty;
				var multiHd = new PerGameModeSettings<bool>(); multiHd["fallout4"] = false;
				ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_VirtualFolder": return folders;
						case "get_HDLinkFolder": return linkFolders;
						case "get_MultiHDInstall": return multiHd;
						default: return null;
					}
				});
				Environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) => method.Name == "get_Settings" ? settings : null);
				Plugins = InterfaceStub<IPluginManager>.Create((method, args) =>
				{
					if (method.Name == "IsActivatiblePluginFile") return Path.GetExtension((string)args[0]) == ".esp";
					if (method.Name == "RemovePlugins") RemovedPlugins.AddRange((IEnumerable<string>)args[0]);
					return null;
				});
				var registry = new ModRegistry(null, GameMode);
				m_mods = (ThreadSafeObservableList<IMod>)typeof(ModRegistry).GetField("m_oclRegisteredMods", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(registry);
				ConstructorInfo constructor = typeof(InstallLog).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
					new[] { typeof(ModRegistry), typeof(IGameMode), typeof(string), typeof(string) }, null);
				InstallLog = (InstallLog)constructor.Invoke(new object[] { registry, null, ModPath, Path.Combine(installInfo, "InstallLog.xml") });
				var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
				SetField(manager, "<GameMode>k__BackingField", GameMode);
				SetField(manager, "<InstallationLog>k__BackingField", InstallLog);
				SetField(manager, "<ManagedModRegistry>k__BackingField", registry);
				SetField(manager, "<EnvironmentInfo>k__BackingField", Environment);
				Activator = new VirtualModActivator(manager, Plugins, GameMode, InstallLog, Environment, ModPath);
				SetField(manager, "m_vmaVirtualModActivator", Activator);
				Deployment = new ModDeploymentManager(InstallLog, Activator, GameMode);
				SetField(manager, "m_mdmDeploymentManager", Deployment);
				Activator.Initialize();
			}

			public string DataPath { get; private set; }
			public string ModPath { get; private set; }
			public string OverwritePath { get; private set; }
			public IGameMode GameMode { get; private set; }
			public IEnvironmentInfo Environment { get; private set; }
			public IPluginManager Plugins { get; private set; }
			public InstallLog InstallLog { get; private set; }
			public VirtualModActivator Activator { get; private set; }
			public ModDeploymentManager Deployment { get; private set; }
			public List<string> RemovedPlugins { get; private set; } = new List<string>();
			/// <summary>
			/// Registers a library archive that can participate in native replacement.
			/// </summary>
			public IMod CreateMod(string name)
			{
				var mod = new InstallLog.DummyMod(name, Path.Combine(ModPath, name + ".7z"));
				m_mods.Add(mod);
				return mod;
			}
			/// <summary>
			/// Resolves the archive staging path inside this fixture.
			/// </summary>
			public string Staged(IMod mod, string file) { return Path.Combine(Activator.VirtualPath, Path.GetFileNameWithoutExtension(mod.Filename), file); }
			/// <summary>
			/// Resolves the game destination for the captured install root.
			/// </summary>
			public string Deployed(string file) { return Path.Combine(m_installRoot == ModInstallRoot.GameRoot ? GameMode.InstallationPath : DataPath, file); }
			/// <summary>
			/// Creates the legacy file remover with the fixture ownership log.
			/// </summary>
			public ModFileInstaller LegacyInstaller(IMod mod)
			{
				return new ModFileInstaller(m_gameInfo, mod, InstallLog, Plugins, InterfaceStub<IDataFileUtil>.Create((method, args) => null),
					new TxFileManager(), null, true, Environment, DataPath);
			}
			/// <summary>
			/// Stages and deploys one recorded Virtual file through the production link installer.
			/// </summary>
			public void Install(IMod mod, string file, string content)
			{
				InstallLog.AddActiveMod(mod, m_installRoot, ModInstallMethod.Virtual);
				string source = Staged(mod, file); Directory.CreateDirectory(Path.GetDirectoryName(source)); File.WriteAllText(source, content);
				InstallLog.AddDataFile(mod, source);
				new ModLinkInstaller(Activator, true).AddFileLinkWithResolvedDecision(mod, file, source,
					true, false, m_installRoot, new ModLinkInstallDecision(true));
				Activator.SaveList(false);
			}
			/// <summary>
			/// Recreates persisted upgrade metadata whose archive and download identities predate its native owner.
			/// </summary>
			public void UseLegacyVirtualIdentity(IMod mod)
			{
				IVirtualModLink[] links = Activator.VirtualLinks.ToArray();
				IVirtualModInfo previous = links.First(link => link.ModInfo.ModFileName == Path.GetFileName(mod.Filename)).ModInfo;
				var legacy = new VirtualModInfo(mod.Id, "100", mod.ModName, Path.Combine(ModPath, "PreviousArchive.7z"), "1.0");
				foreach (IVirtualModLink link in links.Where(link => ReferenceEquals(link.ModInfo, previous))) link.ModInfo = legacy;
				Activator.VirtualMods.Remove(previous);
				Activator.VirtualMods.Add(legacy);
				Activator.SetCurrentList(links);
				Activator.SaveList(false);
			}
			/// <summary>
			/// Runs stream staging, file finalization, and deployment finalization in one native transaction.
			/// </summary>
			public void Upgrade(IMod oldMod, IMod newMod, string file, string content, bool commit = true, bool? reuseGenerated = null, ModInstallRoot? replacementRoot = null)
			{
				ModInstallRoot destinationRoot = replacementRoot ?? m_installRoot;
				var finalizer = new UpgradeFinalizer(oldMod, newMod, this, destinationRoot);
				using (var scope = new TransactionScope())
				{
					InstallLog.ReplaceActiveMod(oldMod, newMod, destinationRoot, ModInstallMethod.Virtual);
					var installer = new StreamUpgradeInstaller(newMod, this);
					string source = Staged(newMod, file); Directory.CreateDirectory(Path.GetDirectoryName(source));
					if (reuseGenerated.HasValue)
					{
						ScriptedInstallOperation operation = reuseGenerated.Value
							? (ScriptedInstallOperation)new GenerateDataFileOperation(file, System.Text.Encoding.UTF8.GetBytes(content), source, false, new ModLinkInstallDecision(true))
							: new InstallModFileOperation(file, file, source, false, new ModLinkInstallDecision(true));
						installer.RetainPreparedStagingFiles(new[] { operation });
					}
					else
						installer.WriteStream(source, content);
					Activator.GetModLinkInstaller().AddFileLink(newMod, file, source, true, false, destinationRoot);
					installer.FinalizeInstall();
					finalizer.Finish(new TxFileManager());
					if (commit) scope.Complete();
				}
			}
			/// <summary>
			/// Runs the production native uninstall without a background worker.
			/// </summary>
			public SynchronousUninstaller Uninstall(IMod mod)
			{
				var uninstaller = new SynchronousUninstaller(mod, this); uninstaller.Execute(); return uninstaller;
			}
			/// <summary>
			/// Removes the fixture files after the test.
			/// </summary>
			public void Dispose() { if (Directory.Exists(m_root)) Directory.Delete(m_root, true); }
			/// <summary>
			/// Binds the uninitialized manager shell to the native fixture services.
			/// </summary>
			private static void SetField(object target, string name, object value) { typeof(ModManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }

			/// <summary>Exposes streamed staging writes through the production upgrade installer.</summary>
			private sealed class StreamUpgradeInstaller : ModFileUpgradeInstaller
			{
				/// <summary>
				/// Creates the upgrade file installer for the fixture native owner.
				/// </summary>
				public StreamUpgradeInstaller(IMod mod, RemovalFixture fixture)
					: base(fixture.m_gameInfo, mod, fixture.InstallLog, fixture.Plugins,
						InterfaceStub<IDataFileUtil>.Create((method, args) => null), new TxFileManager(), null, true, fixture.Environment)
				{ UpgradeDeploymentBackend = fixture.Activator; }
				/// <summary>
				/// Writes an archive stream through the production upgrade file path.
				/// </summary>
				public void WriteStream(string path, string content)
				{
					string payload = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".payload");
					File.WriteAllText(payload, content);
					try { using (FileStream stream = File.OpenRead(payload)) Assert.IsTrue(GenerateDataFileWithResolvedOverwrite(path, stream)); }
					finally { File.Delete(payload); }
				}
			}
			/// <summary>Exposes native upgrade deployment finalization without starting its worker.</summary>
			private sealed class UpgradeFinalizer : ModUpgrader
			{
				/// <summary>
				/// Creates the production upgrade deployment finalizer.
				/// </summary>
				public UpgradeFinalizer(IMod oldMod, IMod newMod, RemovalFixture fixture, ModInstallRoot root)
					: base(oldMod, newMod, fixture.GameMode, fixture.Environment, null, null, fixture.InstallLog, fixture.Plugins,
						fixture.Activator, fixture.Deployment, null, null, new ModInstallContext(ModInstallMethod.Virtual, root)) { }
				/// <summary>
				/// Runs the production obsolete deployment cleanup and owner rebind.
				/// </summary>
				public void Finish(TxFileManager files) { FinalizeDeploymentAfterInstall(files); }
			}
		}

		/// <summary>
		/// Executes the native uninstall body on the test thread without starting a background worker.
		/// </summary>
		private sealed class SynchronousUninstaller : ModUninstaller
		{
			/// <summary>
			/// Creates the native uninstall task using the fixture services.
			/// </summary>
			public SynchronousUninstaller(IMod mod, RemovalFixture fixture)
				: base(mod, fixture.GameMode, fixture.Environment, fixture.Activator, fixture.Deployment,
					fixture.InstallLog, fixture.Plugins, fixture.InstallLog.ActiveMods) { }
			/// <summary>
			/// Runs the protected native uninstall body on the current test thread.
			/// </summary>
			public void Execute() { RunTasks(); }
		}

		/// <summary>Exercises the production disable task without starting a worker thread.</summary>
		private sealed class SynchronousLinkDisableTask : LinkActivationTask
		{
			/// <summary>Binds the task to the fixture's real legacy Virtual deployment services.</summary>
			public SynchronousLinkDisableTask(IMod mod, RemovalFixture fixture)
				: base(fixture.Plugins, fixture.Activator, null, mod, true, null, ModInstallRoot.Data, fixture.Deployment) { }
			/// <summary>Runs the production task body on the calling thread.</summary>
			public void Execute() { DoWork(new object[0]); }
		}
	}
}
