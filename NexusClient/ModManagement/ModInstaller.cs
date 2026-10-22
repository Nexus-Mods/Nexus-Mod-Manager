using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ChinhDo.Transactions;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.Util.Threading;
using Nexus.Transactions;
using Nexus.Client.Util.Collections;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// This installs mods.
	/// </summary>
	public class ModInstaller : ModInstallerBase
	{
		private ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmationDelegate = null;
		private BasicInstallPlan m_preparedPackageFilePlan;
		private string m_preparedPackageDataRoot;
		private ModDeploymentOverwriteResolver m_dorOverwriteResolver;
		private bool m_booUsedPromotedDeployment;
		private bool m_booUsePromotedDeploymentPath;
		private bool m_booNativeMutationStarted;
		private bool m_booNativeTransactionCommitted;
		private bool m_booVirtualStorePersistenceRequired;
		private bool m_booVirtualStorePersisted;
		private Action _collectionArchivePreparation;
		private PreparedBasicInstallMapping m_preparedBasicMapping;
		private Witcher3StandaloneMenuInstallPlan _standaloneWitcherMenuPlan;

		#region Properties

		/// <summary>
		/// Gets or sets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected IMod Mod { get; set; }

		/// <summary>
		/// Gets or sets the mod name.
		/// </summary>
		/// <value>The mod name.</value>
		public string ModName 
		{ 
			get
			{
				if(Mod != null)
					return Mod.ModName;
				else
					return null;
			}
		}

		/// <summary>
		/// Gets or sets the mod file name.
		/// </summary>
		/// <value>The mod file name.</value>
		public string ModFileName
		{
			get
			{
				if (Mod != null)
					return Mod.Filename;
				else
					return null;
			}
		}

		/// <summary>
		/// Gets whether the most recent install task set completed successfully.
		/// </summary>
		public bool Succeeded { get; private set; }

		/// <summary>
		/// Gets the completion message from the most recent install task set.
		/// </summary>
		public string CompletionMessage { get; private set; }

		/// <summary>
		/// Gets or sets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; set; }

		/// <summary>
		/// Gets the mod activator to use to manage file installation.
		/// </summary>
		/// <value>The mod activator to use to manage file installation.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets the method-neutral deployment coordinator.
		/// </summary>
		protected IModDeploymentManager DeploymentManager { get; private set; }

		/// <summary>
		/// Gets the profile manager.
		/// </summary>
		/// <value>The profile manager.</value>
		protected IProfileManager ProfileManager { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The the current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the manager to use to manage plugins.
		/// </summary>
		/// <value>The manager to use to manage plugins.</value>
		protected IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the install log that tracks mod install info
		/// for the current game mode.
		/// </summary>
		/// <value>The install log that tracks mod install info
		/// for the current game mode.</value>
		protected IInstallLog ModInstallLog { get; private set; }

		/// <summary>Final promoted winners captured in an explicit folder-correction review.</summary>
		internal IReadOnlyDictionary<ModDeploymentTarget, string> ReviewedFinalFileOwners { get; set; }

		/// <summary>
		/// Gets or sets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		protected FileUtil FileUtility { get; set; }

		/// <summary>
		/// Gets the <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.
		/// </summary>
		/// <value>The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</value>
		protected SynchronizationContext UIContext { get; private set; }

		protected ReadOnlyObservableList<IMod> ActiveMods { get; set; }

		protected ModInstallContext InstallContext { get; private set; }

		/// <summary>
		/// Gets the optional explicit installation recipe input carried by this native installer.
		/// </summary>
		protected ModInstallationRecipeInput InstallationRecipeInput { get; private set; }

		/// <summary>
		/// Gets the resolved immutable installation context used by this native operation.
		/// </summary>
		internal ModInstallContext OperationInstallContext => InstallContext;

		protected ModInstallRoot InstallRoot => InstallContext.InstallRoot;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgModInstallLog">The install log that tracks mod install info
		/// for the current game mode</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		public ModInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, IProfileManager p_prmProfileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods)
			: this(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_futFileUtility, p_scxUIContext, p_ilgModInstallLog, p_pmgPluginManager, p_ivaVirtualModActivator, p_prmProfileManager, p_dlgOverwriteConfirmationDelegate, p_rolActiveMods, ModInstallRoot.Default)
		{
		}

		public ModInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, IProfileManager p_prmProfileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods, ModInstallRoot p_mirInstallRoot)
			: this(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_futFileUtility, p_scxUIContext, p_ilgModInstallLog, p_pmgPluginManager, p_ivaVirtualModActivator, null, p_prmProfileManager, p_dlgOverwriteConfirmationDelegate, p_rolActiveMods, new ModInstallContext(ModInstallMethod.Virtual, p_mirInstallRoot))
		{
		}

		/// <summary>
		/// Initializes an installer with the immutable deployment context captured for this operation.
		/// </summary>
		public ModInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, IModDeploymentManager p_mdmDeploymentManager, IProfileManager p_prmProfileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods, ModInstallContext p_micInstallContext)
			: this(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_futFileUtility, p_scxUIContext, p_ilgModInstallLog, p_pmgPluginManager, p_ivaVirtualModActivator, p_mdmDeploymentManager, p_prmProfileManager, p_dlgOverwriteConfirmationDelegate, p_rolActiveMods, p_micInstallContext, null)
		{
		}

		/// <summary>
		/// Initializes an installer with the immutable deployment context and optional explicit recipe input captured for this operation.
		/// </summary>
		public ModInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, IModDeploymentManager p_mdmDeploymentManager, IProfileManager p_prmProfileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods, ModInstallContext p_micInstallContext, ModInstallationRecipeInput p_mriRecipeInput)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			FileUtility = p_futFileUtility;
			UIContext = p_scxUIContext;
			ModInstallLog = p_ilgModInstallLog;
			PluginManager = p_pmgPluginManager;
			VirtualModActivator = p_ivaVirtualModActivator;
			DeploymentManager = p_mdmDeploymentManager;
			ProfileManager = p_prmProfileManager;
			m_dlgOverwriteConfirmationDelegate = p_dlgOverwriteConfirmationDelegate;
			ActiveMods = p_rolActiveMods;
			InstallContext = p_micInstallContext ?? throw new ArgumentNullException(nameof(p_micInstallContext));
			if (p_mriRecipeInput != null &&
				(p_mriRecipeInput.InstallContext.Method != InstallContext.Method ||
				p_mriRecipeInput.InstallContext.InstallRoot != InstallContext.InstallRoot))
			{
				throw new ArgumentException("Recipe input method/root must match the native installer context.", nameof(p_mriRecipeInput));
			}
			InstallationRecipeInput = p_mriRecipeInput;
			if (InstallContext.Method == ModInstallMethod.Direct && DeploymentManager == null)
				throw new ArgumentNullException(nameof(p_mdmDeploymentManager));
		}

		#endregion

		/// <summary>Attaches Collection evidence capture after native extraction and validation, before any native mutation.</summary>
		internal void SetCollectionArchivePreparation(Action preparation)
		{
			if (preparation == null) throw new ArgumentNullException(nameof(preparation));
			if (OperationIdentity == null || OperationIdentity.Origin != ModOperationOrigin.Collection ||
				m_booNativeMutationStarted || _collectionArchivePreparation != null)
				throw new InvalidOperationException("Archive preparation must be assigned once before a Collection installer starts mutation.");
			_collectionArchivePreparation = preparation;
		}

		/// <inheritdoc />
		protected internal override void RebindNativeMutationServices(ModManager p_mmgModManager)
		{
			if (p_mmgModManager == null)
				throw new ArgumentNullException(nameof(p_mmgModManager));

			ModInstallLog = p_mmgModManager.InstallationLog;
			VirtualModActivator = p_mmgModManager.VirtualModActivator;
			DeploymentManager = p_mmgModManager.DeploymentManager;
			if (ActiveMods != null)
				ActiveMods = ModInstallLog.ActiveMods;
		}

		/// <summary>
		/// Installs the mod.
		/// </summary>
		public void Install()
		{
			TrackedThread thdWorker = new TrackedThread(RunTasks);
			thdWorker.Thread.IsBackground = false;
			thdWorker.Start();
		}

		/// <summary>
		/// Runs the install tasks.
		/// </summary>
		protected void RunTasks()
		{
			//the install process modifies INI and config files.
			// if multiple sources (i.e., installs) try to modify
			// these files simultaneously the outcome is not well known
			// (e.g., one install changes SETTING1 in a config file to valueA
			// while simultaneously another install changes SETTING1 in the
			// file to value2 - after each install commits its changes it is
			// not clear what the value of SETTING1 will be).
			// as a result, we only allow one mod to be installed at a time,
			// hence the lock.
			bool booSuccess = false;
			ModOperationReportedStatus reportedStatus = ModOperationReportedStatus.Failed;
			string strMessage = "The mod was not activated.";
			long collectionInstallationStarted = OperationIdentity != null && OperationIdentity.Origin == ModOperationOrigin.Collection
				? Nexus.Client.CollectionManagement.CollectionPerformanceMetrics.StartTiming() : 0;
			
			try
			{
				using (var mutationLease = AcquireMutationLease())
				lock (objInstallLock)
				{
					// Startup discovery is not write authorization: counterpart ownership can
					// change after the mode was opened. Check again under the native lease
					// BEFORE replay deletion, registration or a native transaction.
					if (Witcher3RootOwnershipGuard.IsWitcherMode(GameMode.ModeId))
						Witcher3RootOwnershipGuard.RequireNativeMutation(EnvironmentInfo, GameMode.ModeId,
							GameMode.GameModeEnvironmentInfo.InstallationPath,
							GameMode.GameModeEnvironmentInfo.InstallInfoDirectory);
					// The native lease is held, and the authority has been reloaded, but no
					// native transaction has begun. Capture committed Witcher owners HERE:
					// the generic snapshot reader rejects any ambient native transaction.
					m_preparedBasicMapping = null;
					_standaloneWitcherMenuPlan = null;
					Witcher3NativeMutationReview remasteredReview = null;
					if (String.Equals(GameMode.ModeId, Witcher3RootOwnershipGuard.RemasteredModeId,
						StringComparison.OrdinalIgnoreCase))
					{
						if (OperationIdentity == null || (InstallationRecipeInput == null &&
							(OperationIdentity.Origin != ModOperationOrigin.Manual || _collectionArchivePreparation != null)))
							throw new InvalidDataException("Witcher 3 Remastered requires a reviewed basic mapping or an explicit Collection recipe.");
						remasteredReview = Witcher3NativeMutationReview.Capture(EnvironmentInfo, GameMode,
							ModInstallLog, VirtualModActivator, DeploymentManager, PluginManager, GetRootMutationAuthority(mutationLease));
						if (InstallationRecipeInput != null) remasteredReview.AuthorizeCollectionRecipe(InstallationRecipeInput);
					}
					using (TransactionScope tsTransaction = new TransactionScope())
					{
						if (!File.Exists(Mod.Filename))
							throw new Exception("The selected file was not found: " + Mod.Filename);

						TxFileManager tfmFileManager = new TxFileManager();

						if (!BeginModReadOnlyTransaction())
						{
							reportedStatus = ModOperationReportedStatus.Cancelled;
							strMessage = "The mod activation was cancelled.";
							return;
						}

						ValidateInstallationRecipeExecutionInput();
						// The archive is open read-only. Require exact source-to-target approval
						// before script caches are touched, mod registration starts or native
						// Direct/Virtual file writes become possible.
						if (remasteredReview != null && InstallationRecipeInput == null)
						{
							var upgrade = this as ModUpgrader;
							_standaloneWitcherMenuPlan = Witcher3StandaloneMenuInstallPlan.TryCreate(Mod, InstallContext);
							if (upgrade != null && _standaloneWitcherMenuPlan != null)
								throw Witcher3StandaloneMenuInstallPlan.UpgradeRequiresRemoval();
							IReadOnlyList<KeyValuePair<string, string>> reviewed = _standaloneWitcherMenuPlan != null
								? remasteredReview.AuthorizeMenuInstall(Mod, InstallContext, _standaloneWitcherMenuPlan)
								: upgrade == null
								? remasteredReview.AuthorizeBasicInstall(Mod, InstallContext)
								: remasteredReview.AuthorizeBasicUpgrade(upgrade.ReplacedMod, Mod, InstallContext);
							m_preparedBasicMapping = PreparedBasicInstallMapping.Capture(Mod, GameMode, InstallContext, reviewed);
						}
						_collectionArchivePreparation?.Invoke();
						m_booNativeMutationStarted = true;
						if (!IsRestoreRegistrationOnlyRecipe())
							DeleteLiveScriptedReplayArtifacts();
						RegisterMod();
						booSuccess = RunScript(tfmFileManager);
						if (booSuccess)
						{
							tsTransaction.Complete();
							Mod.InstallDate = DateTime.Now.ToString();
							if (InstallContext.Method == ModInstallMethod.Virtual && !m_booUsedPromotedDeployment)
							{
								m_booVirtualStorePersistenceRequired = true;
								m_booVirtualStorePersisted = VirtualModActivator.SaveList(true);
							}
							strMessage = "The mod was successfully activated.";
						}
					}

					if (booSuccess)
						m_booNativeTransactionCommitted = true;

					if (booSuccess)
					{
						VirtualModActivator.PublishPendingDeploymentChanges();
						ProfileManager?.UpdateCurrentDeploymentManifest();
						reportedStatus = ModOperationReportedStatus.Succeeded;
					}
				}
			}
			catch (TransactionException e)
			{
				booSuccess = false;
				reportedStatus = ModOperationReportedStatus.Failed;
				strMessage = "The mod activation failed: " + e.Message;
				TraceInstallException(e);
				// The Collection coordinator records Unknown durability and stops for recovery.
				// Let its terminal result reach that boundary instead of terminating NMM.
				if (OperationIdentity == null || OperationIdentity.Origin != ModOperationOrigin.Collection || InstallationRecipeInput == null)
					throw;
			}
			catch (SecurityException e)
			{
				booSuccess = false;
				reportedStatus = ModOperationReportedStatus.Failed;
				strMessage = "The mod activation failed: " + e.Message;
				TraceInstallException(e);
				throw;
			}
			catch (ObjectDisposedException e)
			{
				booSuccess = false;
				reportedStatus = ModOperationReportedStatus.Failed;
				strMessage = "The mod activation failed: " + e.Message;
				TraceInstallException(e);
				throw;
			}
			//this blobck used to be conditionally excluded from debug builds,
			// presumably so that the debugger would break on the source of the
			// exception, however that prevents the full mod install flow from
			// happening, which lead to missed bugs
			catch (Exception e)
			{
				booSuccess = false;
				reportedStatus = ModOperationReportedStatus.Failed;
				TraceInstallException(e);
				StringBuilder stbError = new StringBuilder(e.Message);
				if (e is FileNotFoundException)
					stbError.Append(" (" + ((FileNotFoundException)e).FileName + ")");
				if (e is IllegalFilePathException)
					stbError.Append(" (" + ((IllegalFilePathException)e).Path + ")");
				if (e.InnerException != null)
					stbError.AppendLine().AppendLine(e.InnerException.Message);
				if (e is RollbackException)
					foreach (RollbackException.ExceptedResourceManager erm in ((RollbackException)e).ExceptedResourceManagers)
					{
						stbError.AppendLine(erm.ResourceManager.ToString());
						stbError.AppendLine(erm.Exception.Message);
						if (erm.Exception.InnerException != null)
							stbError.AppendLine(erm.Exception.InnerException.Message);
					}
				string detail = stbError.ToString();
				if (String.Equals(GameMode.ModeId, Witcher3RootOwnershipGuard.RemasteredModeId, StringComparison.OrdinalIgnoreCase))
					detail = e is Witcher3InstallException ? e.Message : LanguageManager.Get("Witcher3.Install.UnexpectedFailure",
						"NMM could not complete this installation. Close the game and any tools using its files, then try again. If it still fails, include NMM's trace log in your bug report.");
				strMessage = LanguageManager.Format("ModInstaller.InstallFailed", "The mod could not be installed.\n\n{0}", detail);
				PopupErrorMessage = strMessage;
				PopupErrorMessageType = "Error";
			}
			finally
			{
				try
				{
					Mod.EndReadOnlyTransaction();
				}
				catch (Exception e)
				{
					booSuccess = false;
					reportedStatus = ModOperationReportedStatus.Failed;
					strMessage = "The mod activation cleanup failed: " + e.Message;
					throw;
				}
				finally
				{
					// Completion is a terminal scheduling signal, not a durability verdict. Publish it even when
					// preparation returned early or an exception is about to escape so the shared operation queue
					// cannot remain permanently occupied by an in-process task that has already stopped running.
					if (collectionInstallationStarted != 0)
						Nexus.Client.CollectionManagement.CollectionPerformanceMetrics.RecordNativeInstallation(collectionInstallationStarted);
					if (!IsCompleted)
						OnTaskSetCompleted(reportedStatus, DetermineOperationDurability(), booSuccess, strMessage, Mod);
				}
			}
		}

		/// <summary>Writes the complete native installer exception to Trace before UI handling strips it to message text.</summary>
		private void TraceInstallException(Exception exception)
		{
			Trace.TraceError(
				"Native mod install failed for '{0}' (method={1}, root={2}, nativeMutationStarted={3}, nativeTransactionCommitted={4}, virtualStoreRequired={5}, virtualStorePersisted={6}): {7}",
				Mod == null ? "<unknown>" : Mod.Filename,
				InstallContext == null ? "<unknown>" : InstallContext.Method.ToString(),
				InstallContext == null ? "<unknown>" : InstallContext.InstallRoot.ToString(),
				m_booNativeMutationStarted,
				m_booNativeTransactionCommitted,
				m_booVirtualStorePersistenceRequired,
				m_booVirtualStorePersisted,
				exception);
		}

		/// <summary>
		/// Determines what can be proven about durable native state after this installer terminates.
		/// </summary>
		/// <remarks>
		/// This deliberately verifies only the native registration/persistence boundary available at C3.7. Detailed
		/// collection recipe/effect verification is performed by later collection planning/execution layers. Failure
		/// after mutation begins remains unknown unless the native committed state can be positively verified.
		/// </remarks>
		protected virtual ModOperationDurability DetermineOperationDurability()
		{
			if (!m_booNativeMutationStarted)
				return ModOperationDurability.NotStarted;
			if (!m_booNativeTransactionCommitted || ModInstallLog == null)
				return ModOperationDurability.Unknown;

			try
			{
				if (String.IsNullOrEmpty(ModInstallLog.GetModKey(Mod)))
					return ModOperationDurability.Unknown;
				if (ModInstallLog.GetModInstallRoot(Mod) != InstallContext.InstallRoot ||
					ModInstallLog.GetModInstallMethod(Mod) != InstallContext.Method)
					return ModOperationDurability.Unknown;
				if (m_booVirtualStorePersistenceRequired && !m_booVirtualStorePersisted)
					return ModOperationDurability.Unknown;

				return ModOperationDurability.VerifiedCommitted;
			}
			catch
			{
				return ModOperationDurability.Unknown;
			}
		}

		/// <summary>
		/// Captures the final task-set result so callers that synchronously wait for an install can detect failure.
		/// </summary>
		protected override void OnTaskSetCompleted(TaskSetCompletedEventArgs e)
		{
			Succeeded = e.Success;
			CompletionMessage = e.Message;
			base.OnTaskSetCompleted(e);
		}

		/// <summary>
		/// Puts the mod into read-only mode.
		/// </summary>
		/// <returns><c>true</c> if the the read only mode started;
		/// <c>false</c> otherwise.</returns>
		private bool BeginModReadOnlyTransaction()
		{
			PrepareModTask pmtTask = new PrepareModTask(FileUtility);
			OnTaskStarted(pmtTask);
			long started = OperationIdentity != null && OperationIdentity.Origin == ModOperationOrigin.Collection
				? Nexus.Client.CollectionManagement.CollectionPerformanceMetrics.StartTiming() : 0;
			try { return pmtTask.PrepareMod(Mod); }
			finally
			{
				if (started != 0)
					Nexus.Client.CollectionManagement.CollectionPerformanceMetrics.RecordNativeArchivePreparation(started);
			}
		}

		/// <summary>
		/// Deletes the current live scripted replay only after native mod preparation has succeeded.
		/// </summary>
		/// <remarks>
		/// Construction and queueing must remain read-only so callers can persist intent and retain recovery inputs before start.
		/// Profile replay artifacts use separate paths and are not removed here.
		/// </remarks>
		private void DeleteLiveScriptedReplayArtifacts()
		{
			ScriptedFileSelectionCache liveCache = new ScriptedFileSelectionCache(Mod, GameMode);
			ScriptedFileSelectionCache.DeleteArtifacts(liveCache.FilePath);
		}

		/// <summary>
		/// Revalidates an explicit recipe immediately before native mutation begins.
		/// </summary>
		/// <remarks>
		/// Opted-in package plans are frozen before replay cleanup; other recipe-less installs retain their behavior.
		/// Recipe-bearing installs fail closed when translation
		/// has not produced a native plan or when the archive bytes no longer match the immutable C5.3 expectation.
		/// </remarks>
		private void ValidateInstallationRecipeExecutionInput()
		{
			bool packageFiles = (GameMode as IDeterministicPackageFilePlanProvider)?.HasPackageFilePlan == true;
			if (packageFiles && Mod.HasInstallScript)
				throw new NotSupportedException("Configured package generation does not support scripted package execution.");
			if (InstallationRecipeInput == null)
			{
				if (packageFiles)
				{
					ValidateLegacyReplayBasenameCollision();
					BasicInstallPlanResult planned = new BasicInstallPlanBuilder().Build(Mod, GameMode, InstallContext,
						EnvironmentInfo.Settings.SkipReadmeFiles, null, ActiveMods);
					if (!planned.IsSupported) throw new NotSupportedException(planned.Message);
					m_preparedPackageFilePlan = planned.Plan;
					m_preparedPackageDataRoot = Path.GetFullPath(GameMode.GameModeEnvironmentInfo.InstallationPath);
				}
				return;
			}

			ModOperationIdentity recipeOperation = InstallationRecipeInput.OperationIdentity;
			if (OperationIdentity == null ||
				OperationIdentity.OperationId != recipeOperation.OperationId ||
				OperationIdentity.AttemptId != recipeOperation.AttemptId ||
				OperationIdentity.Origin != recipeOperation.Origin ||
				!OperationIdentity.Fingerprint.Equals(recipeOperation.Fingerprint))
			{
				throw new InvalidDataException("Explicit installation recipe input must execute under its assigned native operation identity.");
			}

			if (!InstallationRecipeInput.HasNativePlan ||
				InstallationRecipeInput.NativeOperations == null ||
				InstallationRecipeInput.NativeOperations.Count == 0)
			{
				throw new InvalidDataException("Explicit installation recipe input has not been translated into a native operation plan.");
			}

			bool hasRestoreRegistration = InstallationRecipeInput.NativeOperations.Any(x => x is RestoreNativeRegistrationOperation);
			if (hasRestoreRegistration &&
				(InstallationRecipeInput.NativeOperations.Count != 1 ||
				(recipeOperation.Origin != ModOperationOrigin.LocalRestore && recipeOperation.Origin != ModOperationOrigin.Recovery)))
			{
				throw new InvalidDataException("Registration-only installation is restricted to one LocalRestore or Recovery native operation.");
			}

			if (!hasRestoreRegistration)
				ValidateLegacyReplayBasenameCollision();

			ModInstallationRecipeExpectedContent expectedContent = InstallationRecipeInput.Validation.ExpectedContent;
			if (recipeOperation.Origin == ModOperationOrigin.Collection)
			{
				if (InstallationRecipeInput.SkipContentChecks) return;
				// Preparation already verified the selected archive. Never rehash it during Collection installation.
				if (!Nexus.Client.CollectionManagement.CollectionArchiveContentMatcher.MatchesPreparedFile(Mod.Filename,
					expectedContent.ByteLength, Nexus.Client.CollectionManagement.CollectionContentHash.FromSha256(expectedContent.Sha256)))
					throw new InvalidDataException("The installation recipe source no longer matches its expected content.");
				return;
			}
			using (var stream = new FileStream(Mod.Filename, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				if (stream.Length != expectedContent.ByteLength)
					throw new InvalidDataException("The installation recipe source no longer matches its expected byte length.");

				string actualSha256 = ComputeSha256Hex(stream);
				if (!StringComparer.Ordinal.Equals(actualSha256, expectedContent.Sha256))
					throw new InvalidDataException("The installation recipe source no longer matches its expected SHA-256 digest.");
			}
		}

		/// <summary>Returns whether the explicit recipe is the single restore-only registration marker used by Local Collection restore/recovery.</summary>
		private bool IsRestoreRegistrationOnlyRecipe()
		{
			return InstallationRecipeInput != null && InstallationRecipeInput.NativeOperations != null &&
				InstallationRecipeInput.NativeOperations.Count == 1 &&
				InstallationRecipeInput.NativeOperations[0] is RestoreNativeRegistrationOperation;
		}

		/// <summary>
		/// Rejects a recipe when another active native archive maps to the same legacy live replay path.
		/// </summary>
		/// <remarks>
		/// Native mod identity uses the full archive filename while the legacy scripted replay cache uses only the
		/// filename stem. Distinct active archive identities must therefore not share one replay XML/payload location.
		/// </remarks>
		private void ValidateLegacyReplayBasenameCollision()
		{
			if (ModInstallLog == null || ModInstallLog.ActiveMods == null)
				return;

			string replayPath = new ScriptedFileSelectionCache(Mod, GameMode).FilePath;
			foreach (IMod activeMod in ModInstallLog.ActiveMods)
			{
				if (activeMod == null || IsActiveModExemptFromReplayBasenameCollision(activeMod))
					continue;
				if (StringComparer.OrdinalIgnoreCase.Equals(activeMod.Filename, Mod.Filename))
					continue;

				string activeReplayPath = new ScriptedFileSelectionCache(activeMod, GameMode).FilePath;
				if (StringComparer.OrdinalIgnoreCase.Equals(activeReplayPath, replayPath))
				{
					throw new InvalidDataException(String.Format(
						"The installation recipe archive '{0}' collides with active archive '{1}' at legacy replay basename '{2}'.",
						Path.GetFileName(Mod.Filename), Path.GetFileName(activeMod.Filename), Path.GetFileName(replayPath)));
				}
			}
		}

		/// <summary>
		/// Returns whether an active mod is the native identity intentionally replaced by this installer.
		/// </summary>
		/// <param name="p_modActiveMod">The active mod sharing the candidate replay basename.</param>
		/// <returns><c>true</c> when this installer may replace that identity; otherwise, <c>false</c>.</returns>
		protected virtual bool IsActiveModExemptFromReplayBasenameCollision(IMod p_modActiveMod)
		{
			return false;
		}

		/// <summary>
		/// Computes the canonical lowercase SHA-256 identity used by the C5 recipe content contract.
		/// </summary>
		private static string ComputeSha256Hex(Stream p_stmStream)
		{
			using (SHA256 sha256 = SHA256.Create())
				return BitConverter.ToString(sha256.ComputeHash(p_stmStream)).Replace("-", String.Empty).ToLowerInvariant();
		}

		#region Script Execution

		/// <summary>
		/// This executes the install script.
		/// </summary>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns><c>true</c> if the script completed successfully;
		/// <c>false</c> otherwise.</returns>
		protected bool RunScript(TxFileManager p_tfmFileManager)
		{
			// Recipe operations were reviewed under the live native lease. Ordinary manual
			// packages continue to require the dedicated basic Mods/DLC mapping.
			if (String.Equals(GameMode.ModeId, "Witcher3Remastered", StringComparison.OrdinalIgnoreCase) && InstallationRecipeInput == null &&
				(GameMode as IReviewedBasicInstallMappingProvider == null || Mod.HasInstallScript || InstallContext.InstallRoot != ModInstallRoot.Data))
				throw new InvalidDataException("Witcher 3 Remastered native writes require a reviewed ordinary basic Mods/DLC archive mapping.");

			m_booUsedPromotedDeployment = false;
			m_booUsePromotedDeploymentPath = InstallContext.Method == ModInstallMethod.Virtual &&
				!Mod.HasInstallScript &&
				DeploymentManager != null &&
				DeploymentManager.HasPromotedTargets;
			bool booNeedsDeploymentOverwriteResolver = DeploymentManager != null &&
				(InstallContext.Method == ModInstallMethod.Direct || DeploymentManager.HasPromotedTargets);
			m_dorOverwriteResolver = booNeedsDeploymentOverwriteResolver
				? new ModDeploymentOverwriteResolver(Mod, ModInstallLog, DeploymentManager, m_dlgOverwriteConfirmationDelegate)
				: null;
			IModFileInstaller mfiFileInstaller = CreateFileInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
			bool booResult = false;
			IIniInstaller iniIniInstaller = null;
			IGameSpecificValueInstaller gviGameSpecificValueInstaller = null;
			if (InstallationRecipeInput != null)
			{
				if (!InstallationRecipeInput.HasNativePlan ||
					InstallationRecipeInput.NativeOperations == null ||
					InstallationRecipeInput.NativeOperations.Count == 0)
				{
					throw new InvalidDataException("Explicit installation recipe input has not been translated into a native operation plan.");
				}
				(mfiFileInstaller as ModFileUpgradeInstaller)?.RetainPreparedStagingFiles(InstallationRecipeInput.NativeOperations);
				booResult = RunInstallationRecipe(mfiFileInstaller, InstallationRecipeInput.NativeOperations, p_tfmFileManager);
			}
			else if (_standaloneWitcherMenuPlan != null)
			{
				booResult = RunInstallationRecipe(mfiFileInstaller, _standaloneWitcherMenuPlan.Operations, p_tfmFileManager);
			}
			else if (Mod.HasInstallScript)
			{
				if (CheckScriptedModLog())
				{
					string replaySourcePath;
					IScriptedFileSelectionCache replayCache = GetScriptedFileSelectionCache(out replaySourcePath);
					IReadOnlyList<ScriptedReplayOperation> replayOperations = replayCache == null ? null : replayCache.LoadReplayOperations();
					bool requiresOperationReplay = replayOperations != null && replayOperations.Any(x => x.Kind != ScriptedReplayOperationKind.ArchiveFile);
					if (requiresOperationReplay)
					{
						booResult = RunScriptedReplay(mfiFileInstaller, replayOperations, p_tfmFileManager);
						if (booResult)
							CopyProfileReplayToLiveCache(replaySourcePath);
					}
					else
					{
						booResult = RunBasicInstallScript(mfiFileInstaller, ActiveMods, replayCache == null ? null : replayCache.LoadSelections(), p_tfmFileManager);
						if (booResult)
							CopyProfileReplayToLiveCache(replaySourcePath);
					}
				}
				else
				{
					try
					{
						IDataFileUtil dfuDataFileUtility = new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath);

						iniIniInstaller = CreateIniInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
						gviGameSpecificValueInstaller = CreateGameSpecificValueInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);

						InstallerGroup ipgInstallers = new InstallerGroup(
							dfuDataFileUtility, mfiFileInstaller, iniIniInstaller, gviGameSpecificValueInstaller, PluginManager,
							InstallContext, DeploymentManager, p_tfmFileManager, m_dorOverwriteResolver);
						IScriptExecutor sexScript = Mod.InstallScript.Type.CreateExecutor(Mod, GameMode, EnvironmentInfo, VirtualModActivator, ipgInstallers, UIContext);
						sexScript.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(ScriptExecutor_TaskStarted);
						sexScript.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(ScriptExecutor_TaskSetCompleted);
						booResult = sexScript.Execute(Mod.InstallScript);
						m_booUsedPromotedDeployment |= ipgInstallers.UsedPromotedDeployment;
					}
					catch (Exception ex)
					{
						PopupErrorMessage = ex.Message;
						PopupErrorMessageType = "Error";
					}

					iniIniInstaller.FinalizeInstall();

					if (gviGameSpecificValueInstaller != null)
						gviGameSpecificValueInstaller.FinalizeInstall();
				}
			}
			else
				booResult = RunBasicInstallScript(mfiFileInstaller, ActiveMods, null, p_tfmFileManager);
			mfiFileInstaller.FinalizeInstall();
			FinalizeDeploymentAfterInstall(p_tfmFileManager);
			if (booResult && ReviewedFinalFileOwners != null)
				foreach (KeyValuePair<ModDeploymentTarget, string> winner in ReviewedFinalFileOwners)
					DeploymentManager.SwitchPromotedOwner(winner.Key, winner.Value);
			return booResult;
		}

		/// <summary>
		/// Executes an already-validated native recipe plan through the existing scripted installer group and executor.
		/// </summary>
		/// <remarks>
		/// The caller owns the native transaction and has already registered the mod. Replayable file operations are
		/// recorded through the same native scripted-selection cache used by ordinary scripted installs, including
		/// generated-file payload sidecars. Non-file effects remain owned by their existing native subsystems.
		/// </remarks>
		private bool RunInstallationRecipe(IModFileInstaller p_mfiFileInstaller, IReadOnlyList<ScriptedInstallOperation> p_lstOperations, TxFileManager p_tfmFileManager)
		{
			if (p_lstOperations == null || p_lstOperations.Count == 0)
				throw new InvalidDataException("Explicit installation recipe input requires at least one native operation.");

			IDataFileUtil dataFileUtility = new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath);
			IIniInstaller iniInstaller = CreateIniInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
			IGameSpecificValueInstaller gameSpecificInstaller = CreateGameSpecificValueInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
			var installers = new InstallerGroup(
				dataFileUtility, p_mfiFileInstaller, iniInstaller, gameSpecificInstaller, PluginManager,
				InstallContext, DeploymentManager, p_tfmFileManager, m_dorOverwriteResolver);
			IScriptedFileSelectionCache replayCache = new ScriptedFileSelectionCache(Mod, GameMode);
			var executor = new ImmediateScriptedInstallOperationExecutor(
				Mod, GameMode, EnvironmentInfo, VirtualModActivator, VirtualModActivator.GetModLinkInstaller(), installers,
				x => OnTaskStarted(x), replayCache, true, InstallationRecipeInput != null && InstallationRecipeInput.DeferPluginReconciliation);

			bool result = true;
			using (executor.BeginExecutionBatch(p_lstOperations.Count))
			{
				foreach (ScriptedInstallOperation operation in p_lstOperations)
				{
					if (!executor.Execute(operation))
					{
						result = false;
						break;
					}
				}
			}

			if (result)
				executor.CompleteExecution();

			m_booUsedPromotedDeployment |= installers.UsedPromotedDeployment;
			iniInstaller?.FinalizeInstall();
			gameSpecificInstaller?.FinalizeInstall();
			return result;
		}

		/// <summary>
		/// Allows specialized installers to finalize method-neutral deployment state after file finalization.
		/// </summary>
		protected virtual void FinalizeDeploymentAfterInstall(TxFileManager p_tfmFileManager)
		{
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event of script executors.
		/// </summary>
		/// <remarks>
		/// This unwires our listeners from the executor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void ScriptExecutor_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			IBackgroundTaskSet btsExecutor = (IBackgroundTaskSet)sender;
			btsExecutor.TaskStarted -= ScriptExecutor_TaskStarted;
			btsExecutor.TaskSetCompleted -= ScriptExecutor_TaskSetCompleted;
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskStarted"/> event of script executors.
		/// </summary>
		/// <remarks>
		/// This bubbles the started task to any listeners.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ScriptExecutor_TaskStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			OnTaskStarted(e.Argument);
		}

		/// <summary>
		/// Runs the basic install script.
		/// </summary>
		/// <remarks>
		/// A basic install installs all of the files in the mod to the installation directory,
		/// and activates all plugin files.
		/// </remarks>
		/// <param name="p_mfiFileInstaller">The file installer to use.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <param name="p_dicInstallFiles">The list of specific files to install, if null the mod will be installed as usual.</param>
		/// <returns><c>true</c> if the installation was successful;
		/// <c>false</c> otherwise.</returns>
		protected bool RunBasicInstallScript(IModFileInstaller p_mfiFileInstaller,
			ReadOnlyObservableList<IMod> p_rolActiveMods, List<KeyValuePair<string, string>> p_dicInstallFiles,
			TxFileManager p_tfmFileManager)
		{
			IModDeploymentManager deploymentManager = m_booUsePromotedDeploymentPath ? DeploymentManager : null;
			// The native operation must use the same approved archive sources/destinations
			// that were reviewed under its cross-process lease. A second independent
			// package selection may not be substituted after authorization.
			if (String.Equals(GameMode.ModeId, Witcher3RootOwnershipGuard.RemasteredModeId,
				StringComparison.OrdinalIgnoreCase))
			{
				if (m_preparedBasicMapping == null)
					throw new InvalidOperationException("Remastered basic install has no locked native ownership authorization.");
			}
			if ((GameMode as IDeterministicPackageFilePlanProvider)?.HasPackageFilePlan == true)
			{
				// The existing native recipe executor owns both generated payload retention and Direct/Virtual deployment.
				if (m_preparedPackageFilePlan == null || p_dicInstallFiles != null ||
					!String.Equals(m_preparedPackageDataRoot, Path.GetFullPath(GameMode.GameModeEnvironmentInfo.InstallationPath), StringComparison.OrdinalIgnoreCase) ||
					m_preparedPackageFilePlan.InstallContext.Method != InstallContext.Method ||
					m_preparedPackageFilePlan.InstallContext.InstallRoot != InstallContext.InstallRoot)
					throw new InvalidOperationException("The package file recipe no longer matches its reviewed native context.");
				var operations = new List<ScriptedInstallOperation>();
				foreach (BasicInstallGameSpecificValue value in m_preparedPackageFilePlan.GameSpecificValues)
					operations.Add(new EditGameSpecificValueOperation(value.Key, value.Value));
				foreach (BasicInstallPlanFile file in m_preparedPackageFilePlan.Files)
					operations.Add(new InstallModFileOperation(file.SourcePath, file.DestinationPath));
				foreach (BasicInstallGeneratedFile file in m_preparedPackageFilePlan.GeneratedFiles)
					operations.Add(new GenerateDataFileOperation(file.DestinationPath, file.Data));
				(p_mfiFileInstaller as ModFileUpgradeInstaller)?.RetainPreparedStagingFiles(operations);
				return RunInstallationRecipe(p_mfiFileInstaller, operations, p_tfmFileManager);
			}
			StructuredXmlRecordRule records = (GameMode as IStructuredXmlRecordProvider)?.StructuredRecords;
			IGameSpecificValueInstaller structuredInstaller = null;
			if (records != null && !Mod.HasInstallScript)
			{
				// The enclosing native transaction owns both full-file snapshots and value history.
				// Plan all profiles before the first edit, then use the same installer as Collections.
				IReadOnlyList<BasicInstallGameSpecificValue> values = new StructuredXmlRecordStore(records).BuildPlan(Mod);
				structuredInstaller = CreateGameSpecificValueInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
				if (structuredInstaller == null) throw new InvalidOperationException("Configured structured effects require a native game-value installer.");
				foreach (BasicInstallGameSpecificValue value in values)
					if (!structuredInstaller.EditGameSpecificValue(value.Key, value.Value)) return false;
			}
			PreparedBasicInstallMapping prepared = m_preparedBasicMapping;
			if (prepared == null && records == null && GameMode is IReviewedBasicInstallMappingProvider)
				prepared = PreparedBasicInstallMapping.Prepare(Mod, GameMode, InstallContext, p_dicInstallFiles);
			BasicInstallTask bitTask = new BasicInstallTask(
				Mod,
				GameMode,
				p_mfiFileInstaller,
				PluginManager,
				VirtualModActivator,
				EnvironmentInfo.Settings.SkipReadmeFiles,
				p_rolActiveMods,
				p_dicInstallFiles,
				InstallContext,
				deploymentManager,
				p_tfmFileManager,
				deploymentManager == null ? null : m_dorOverwriteResolver,
				prepared);
			OnTaskStarted(bitTask);
			bool result = bitTask.Execute();
			if (result) structuredInstaller?.FinalizeInstall();
			m_booUsedPromotedDeployment |= bitTask.UsedPromotedDeployment;
			return result;
		}

		#endregion

		#region Installer Creation

		/// <summary>
		/// Creates the file installer to use to install the mod's files.
		/// </summary>
		/// <remarks>
		/// This returns the regular <see cref="ModFileInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected virtual IModFileInstaller CreateFileInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			if (InstallContext.Method == ModInstallMethod.Direct)
			{
				return new DirectModFileInstaller(Mod, GameMode, DeploymentManager, PluginManager,
					p_tfmFileManager, EnvironmentInfo, InstallContext, m_dorOverwriteResolver);
			}

			return new ModFileInstaller(GameMode.GameModeEnvironmentInfo, Mod, ModInstallLog, PluginManager, new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath), p_tfmFileManager, p_dlgOverwriteConfirmationDelegate, GameMode.UsesPlugins, EnvironmentInfo, GetInstallBasePath());
		}

		private string GetInstallBasePath()
		{
			return InstallRoot == ModInstallRoot.GameRoot ? GameMode.InstallationPath : GameMode.GameModeEnvironmentInfo.InstallationPath;
		}

		/// <summary>
		/// Creates the file installer to use to install the mod's ini edits.
		/// </summary>
		/// <remarks>
		/// This returns the regular <see cref="IniInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected virtual IIniInstaller CreateIniInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return new IniInstaller(Mod, ModInstallLog, VirtualModActivator, p_tfmFileManager, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Creates the file installer to use to install the mod's game specific value edits.
		/// </summary>
		/// <remarks>
		/// This returns a regular <see cref="IGameSpecificValueInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected virtual IGameSpecificValueInstaller CreateGameSpecificValueInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return GameMode.GetGameSpecificValueInstaller(Mod, ModInstallLog, p_tfmFileManager, new NexusFileUtil(EnvironmentInfo), p_dlgOverwriteConfirmationDelegate);
		}

		#endregion

		/// <summary>
		/// Checks whether there's a log file for the current scripted installer.
		/// </summary>
		protected bool CheckScriptedModLog()
		{
			if ((ProfileManager != null) && !String.IsNullOrWhiteSpace(ProfileManager.IsScriptedLogPresent(Mod.Filename)))
				return true;

			IScriptedFileSelectionCache sfcFileSelectionCache = new ScriptedFileSelectionCache(Mod, GameMode);
			return sfcFileSelectionCache.Exists;
		}

		/// <summary>
		/// Checks if there's an XML with the list of files to install for the current mod, if present the list of files will be returned.
		/// </summary>
		protected List<KeyValuePair<string, string>> LoadXMLModFilesToInstall()
		{
			string cachePath;
			IScriptedFileSelectionCache cache = GetScriptedFileSelectionCache(out cachePath);
			return cache == null ? null : cache.LoadSelections();
		}

		/// <summary>
		/// Resolves the replay cache selected for this install, preferring the active profile snapshot when present.
		/// </summary>
		private IScriptedFileSelectionCache GetScriptedFileSelectionCache(out string p_strCachePath)
		{
			IScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(Mod, GameMode);
			p_strCachePath = cache.FilePath;
			if (ProfileManager != null)
			{
				string profileCachePath = ProfileManager.IsScriptedLogPresent(Mod.Filename);
				if (!String.IsNullOrWhiteSpace(profileCachePath))
				{
					cache = new ScriptedFileSelectionCache(profileCachePath);
					p_strCachePath = profileCachePath;
				}
			}
			return cache;
		}

		/// <summary>
		/// Replays the ordered scripted file operations needed for exact profile restoration.
		/// </summary>
		private bool RunScriptedReplay(IModFileInstaller p_mfiFileInstaller, IReadOnlyList<ScriptedReplayOperation> p_lstOperations, TxFileManager p_tfmFileManager)
		{
			if (p_lstOperations == null || p_lstOperations.Count == 0)
				return false;

			IDataFileUtil dataFileUtility = new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath);
			IIniInstaller iniInstaller = CreateIniInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
			IGameSpecificValueInstaller gameSpecificInstaller = CreateGameSpecificValueInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
			var installers = new InstallerGroup(
				dataFileUtility, p_mfiFileInstaller, iniInstaller, gameSpecificInstaller, PluginManager,
				InstallContext, DeploymentManager, p_tfmFileManager, m_dorOverwriteResolver);
			var executor = new ImmediateScriptedInstallOperationExecutor(
				Mod, GameMode, EnvironmentInfo, VirtualModActivator, VirtualModActivator.GetModLinkInstaller(), installers,
				x => OnTaskStarted(x), null);

			bool result = true;
			using (executor.BeginExecutionBatch(p_lstOperations.Count))
			{
				foreach (ScriptedReplayOperation operation in p_lstOperations)
				{
					ScriptedInstallOperation installOperation;
					switch (operation.Kind)
					{
						case ScriptedReplayOperationKind.ArchiveFile:
							installOperation = new InstallModFileOperation(operation.SourcePath, operation.DestinationPath);
							break;
						case ScriptedReplayOperationKind.GeneratedFile:
							installOperation = new GenerateDataFileOperation(operation.DestinationPath, File.ReadAllBytes(operation.PayloadPath));
							break;
						case ScriptedReplayOperationKind.BasicInstall:
							installOperation = new PerformBasicInstallOperation();
							break;
						default:
							throw new InvalidDataException("The scripted replay contains an unsupported operation.");
					}

					if (!executor.Execute(installOperation))
					{
						result = false;
						break;
					}
				}
			}

			if (result)
				executor.CompleteExecution();

			m_booUsedPromotedDeployment |= installers.UsedPromotedDeployment;
			iniInstaller.FinalizeInstall();
			if (gameSpecificInstaller != null)
				gameSpecificInstaller.FinalizeInstall();
			return result;
		}

		/// <summary>
		/// Restores the profile replay artifacts into the live InstallInfo cache after a successful replay.
		/// </summary>
		private void CopyProfileReplayToLiveCache(string p_strReplaySourcePath)
		{
			if (String.IsNullOrWhiteSpace(p_strReplaySourcePath))
				return;

			var liveCache = new ScriptedFileSelectionCache(Mod, GameMode);
			if (p_strReplaySourcePath.Equals(liveCache.FilePath, StringComparison.OrdinalIgnoreCase))
				return;
			ScriptedFileSelectionCache.CopyArtifacts(p_strReplaySourcePath, liveCache.FilePath);
		}

		/// <summary>
		/// Registers the mod being installed with the install log.
		/// </summary>
		protected virtual void RegisterMod()
		{
			ModInstallLog.AddActiveMod(Mod, InstallRoot, InstallContext.Method);
		}
	}
}
