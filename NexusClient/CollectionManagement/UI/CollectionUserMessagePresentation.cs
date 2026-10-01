using System;
using System.Text.RegularExpressions;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Localized-message descriptor which keeps user-facing wording separate from retained technical detail.</summary>
	internal sealed class CollectionUserMessagePresentation
	{
		internal CollectionUserMessagePresentation(string messageKey, string messageFallback,
			string nextActionKey, string nextActionFallback, string technicalDetail)
		{
			MessageKey = messageKey ?? String.Empty;
			MessageFallback = messageFallback ?? String.Empty;
			NextActionKey = nextActionKey ?? String.Empty;
			NextActionFallback = nextActionFallback ?? String.Empty;
			TechnicalDetail = technicalDetail ?? String.Empty;
		}

		internal string MessageKey { get; }
		internal string MessageFallback { get; }
		internal string NextActionKey { get; }
		internal string NextActionFallback { get; }
		internal string TechnicalDetail { get; }
	}

	/// <summary>Maps structured Collection outcomes to beginner-readable UI wording while retaining raw diagnostics for support export.</summary>
	internal static class CollectionUserMessagePresenter
	{
		private static readonly Regex InternalStagePattern = new Regex(
			@"\bC\d+(?:\.\d+)*(?:[a-z]\d*)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex GateAPattern = new Regex(@"\bGate[- ]A\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex GateLPattern = new Regex(@"\bGate[- ]L\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		internal static CollectionUserMessagePresentation ForPreparation(CollectionAdditiveWorkflowPreparationStatus status,
			string technicalDetail, bool hasManualAction, bool hasBlockedAcquisition)
		{
			switch (status)
			{
				case CollectionAdditiveWorkflowPreparationStatus.AwaitingInput:
					if (hasBlockedAcquisition)
						return Message("Collections.Messages.Preparation.AcquisitionBlocked",
							"One or more selected mods cannot be acquired safely with the available source information.",
							"Collections.Messages.Next.ResolveBlockedAcquisition",
							"Review the blocked mod below and resolve its source or compatibility issue before continuing.", technicalDetail);
					return hasManualAction
						? Message("Collections.Messages.Preparation.AwaitingManual",
							"One or more selected mods need your input before preparation can continue.",
							"Collections.Messages.Next.DownloadAndContinue",
							"Download the missing mod shown below, then choose Check downloads and continue.", technicalDetail)
						: Message("Collections.Messages.Preparation.AwaitingDownloads",
							"Selected mod archives are still downloading or waiting to be imported by NMM.",
							"Collections.Messages.Next.WaitOrCheckDownloads",
							"Wait for the downloads to finish; NMM will recheck automatically, or choose Check downloads and continue.", technicalDetail);
				case CollectionAdditiveWorkflowPreparationStatus.PreparationRequired:
					return Message("Collections.Messages.Preparation.RefreshRequired",
						"Preparation must be refreshed because the reviewed inputs are no longer current.",
						"Collections.Messages.Next.PrepareAgain", "Choose Download / Prepare to build a fresh review.", technicalDetail);
				case CollectionAdditiveWorkflowPreparationStatus.ActionRequired:
					return Message("Collections.Messages.Preparation.ActionRequired",
						"Preparation found a decision that NMM cannot apply automatically.",
						"Collections.Messages.Next.ReviewRequiredActions", "Review the errors and required actions before continuing.", technicalDetail);
				case CollectionAdditiveWorkflowPreparationStatus.Blocked:
					return Message("Collections.Messages.Preparation.Blocked",
						"Preparation stopped because one or more selected Collection requirements cannot be handled safely.",
						"Collections.Messages.Next.ResolveBlockedPreparation", "Review the errors below and resolve the blocked requirement before preparing again.", technicalDetail);
				case CollectionAdditiveWorkflowPreparationStatus.ReadyForReview:
					return Message("Collections.Messages.Preparation.Ready",
						"All required content is verified and the installation changes are ready for review.",
						"Collections.Messages.Next.ReviewAndInstall", "Review the planned changes, then choose Review and install...", technicalDetail);
				default:
					return FromRaw(technicalDetail, String.Empty);
			}
		}

		internal static CollectionUserMessagePresentation ForApply(CollectionAdditiveWorkflowApplyStatus status, string technicalDetail)
		{
			switch (status)
			{
				case CollectionAdditiveWorkflowApplyStatus.Committed:
					return Message("Collections.Messages.Apply.Committed",
						"The Collection was installed and the resulting managed state was verified.", String.Empty, String.Empty, technicalDetail);
				case CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary:
					return Message("Collections.Messages.Apply.Paused",
						"Installation paused after verified progress at a safe boundary.",
						"Collections.Messages.Next.ReviewAndContinue", "Review the recovered plan, then choose Review and continue...", technicalDetail);
				case CollectionAdditiveWorkflowApplyStatus.RecoveryRequired:
					return Message("Collections.Messages.Apply.RecoveryRequired",
						"Installation stopped because NMM cannot safely confirm the final managed state yet.",
						"Collections.Messages.Next.CheckRecoveryContinue", "Choose Check recovery and continue... to reconcile the interrupted installation and review the remaining changes.", technicalDetail);
				case CollectionAdditiveWorkflowApplyStatus.RepreparationRequired:
					return Message("Collections.Messages.Apply.Reprepare",
						"The approved review is no longer valid for the current setup.",
						"Collections.Messages.Next.PrepareAgain", "Choose Download / Prepare to build a fresh review.", technicalDetail);
				case CollectionAdditiveWorkflowApplyStatus.StoppedPartial:
					return Message("Collections.Messages.Apply.StoppedPartial",
						"Installation stopped after verified partial progress.",
						"Collections.Messages.Next.CheckRecovery", "Check the recovery status before making further managed changes.", technicalDetail);
				default:
					return FromRaw(technicalDetail, String.Empty);
			}
		}

		internal static CollectionUserMessagePresentation ForRecovery(CollectionAdditiveWorkflowRecoveryStatus status, string technicalDetail)
		{
			switch (status)
			{
				case CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired:
					return Message("Collections.Messages.Recovery.ReviewRequired",
						"An interrupted Collection operation was reconciled. The recovered review needs your approval before continuing.",
						"Collections.Messages.Next.ReviewAndInstall", "Review the planned changes, then choose Review and install...", technicalDetail);
				case CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume:
					return Message("Collections.Messages.Recovery.ReadyToResume",
						"An interrupted Collection operation was reconciled to a safe resume point.",
						"Collections.Messages.Next.ReviewAndContinue", "Review the recovered plan, then choose Review and continue...", technicalDetail);
				case CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired:
					return Message("Collections.Messages.Recovery.Reprepare",
						"The interrupted operation can no longer use its previous review because the current setup changed.",
						"Collections.Messages.Next.PrepareAgain", "Choose Download / Prepare to build a fresh review.", technicalDetail);
				case CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired:
					return Message("Collections.Messages.Recovery.Required",
						"NMM could not safely reconcile the interrupted Collection operation automatically.",
						"Collections.Messages.Next.CheckRecoveryContinue", "Choose Check recovery and continue... to reconcile the interrupted installation and review the remaining changes.", technicalDetail);
				case CollectionAdditiveWorkflowRecoveryStatus.StoppedPartial:
					return Message("Collections.Messages.Recovery.StoppedPartial",
						"The interrupted operation was reconciled as partial verified progress and cannot continue automatically.",
						"Collections.Messages.Next.CheckRecovery", "Check the recovery status before making further managed changes.", technicalDetail);
				default:
					return FromRaw(technicalDetail, String.Empty);
			}
		}

		internal static CollectionUserMessagePresentation ForLocalRestore(CollectionLocalRestoreWorkflowStatus status, string technicalDetail)
		{
			switch (status)
			{
				case CollectionLocalRestoreWorkflowStatus.Completed:
					return Message("Collections.Messages.LocalRestore.Completed",
						"The saved Local Collection was restored and the final managed state was verified.", String.Empty, String.Empty, technicalDetail);
				case CollectionLocalRestoreWorkflowStatus.RecoveryRequired:
					return Message("Collections.Messages.LocalRestore.RecoveryRequired",
						"Local Collection restore stopped at a point that requires recovery before it can continue.",
						"Collections.Messages.Next.CheckRecovery", "Check the recovery status before making further managed changes.", technicalDetail);
				case CollectionLocalRestoreWorkflowStatus.CurrentStateChanged:
					return Message("Collections.Messages.LocalRestore.CurrentStateChanged",
						"The current managed setup changed after this Local Collection restore was reviewed.",
						"Collections.Messages.Next.ReviewLocalRestoreAgain", "Build and review the Local Collection restore again from the current setup.", technicalDetail);
				case CollectionLocalRestoreWorkflowStatus.RetainedInputInvalid:
					return Message("Collections.Messages.LocalRestore.RetainedInputInvalid",
						"The retained files or metadata required for this Local Collection restore could not be validated.",
						"Collections.Messages.Next.InspectSavedCapture", "Do not continue this restore until the saved capture is verified or replaced.", technicalDetail);
				default:
					return FromRaw(technicalDetail, String.Empty);
			}
		}

		internal static CollectionUserMessagePresentation ForAcquisition(CollectionMemberAcquisitionDisposition disposition, string technicalDetail,
			bool producerEndedWithoutArchive = false)
		{
			if (disposition == CollectionMemberAcquisitionDisposition.RestartActionRequired && producerEndedWithoutArchive)
				return Message("Collections.Messages.Acquisition.EndedUnverified", "The download or import ended without a verified archive for this Collection member.",
					"Collections.Messages.Next.RetryAcquisition", "Choose Download / Prepare to retry acquisition, then check downloads again.", technicalDetail);

			switch (disposition)
			{
				case CollectionMemberAcquisitionDisposition.ReadyInstalled:
					return Message("Collections.Messages.Acquisition.ReadyInstalled", "A compatible installed mod already satisfies this Collection member.", String.Empty, String.Empty, technicalDetail);
				case CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive:
					return Message("Collections.Messages.Acquisition.ReadyArchive", "A verified local archive is ready for this Collection member.", String.Empty, String.Empty, technicalDetail);
				case CollectionMemberAcquisitionDisposition.PremiumQueued:
					return Message("Collections.Messages.Acquisition.PremiumQueued", "This mod is queued for automatic Nexus download.", String.Empty, String.Empty, technicalDetail);
				case CollectionMemberAcquisitionDisposition.BundledQueued:
					return Message("Collections.Messages.Acquisition.BundledQueued", "This bundled mod archive is queued for import.", String.Empty, String.Empty, technicalDetail);
				case CollectionMemberAcquisitionDisposition.ManualInputRequired:
					return Message("Collections.Messages.Acquisition.ManualRequired", "This mod needs a user-authorized download before preparation can continue.",
						"Collections.Messages.Next.DownloadAndContinue", "Download the missing mod shown below, then choose Check downloads and continue.", technicalDetail);
				case CollectionMemberAcquisitionDisposition.RestartActionRequired:
					return Message("Collections.Messages.Acquisition.RestartRequired", "The previous download state needs to be confirmed before preparation can continue.",
						"Collections.Messages.Next.DownloadAndContinue", "Complete the requested download step, then choose Check downloads and continue.", technicalDetail);
				case CollectionMemberAcquisitionDisposition.Blocked:
					return Message("Collections.Messages.Acquisition.Blocked", "This mod cannot be acquired safely with the currently available source information.",
						"Collections.Messages.Next.ResolveBlockedAcquisition", "Review this mod's source and compatibility details; preparation cannot continue while it is blocked.", technicalDetail);
				default:
					return FromRaw(technicalDetail, String.Empty);
			}
		}

		internal static CollectionUserMessagePresentation ForCapability(CollectionCompatibilityStatus status, string technicalDetail)
		{
			string userText = SanitizeInternalTerminology(technicalDetail);
			if (status == CollectionCompatibilityStatus.Unsupported)
				return new CollectionUserMessagePresentation(String.Empty, userText,
					"Collections.Messages.Next.UnsupportedCapability", "This behavior is not currently supported automatically; change the Collection selection or use a supported revision before continuing.", technicalDetail);
			if (status == CollectionCompatibilityStatus.ActionRequired)
				return new CollectionUserMessagePresentation(String.Empty, userText,
					"Collections.Messages.Next.ResolveCapability", "Review and resolve this required decision before preparing the Collection again.", technicalDetail);
			return new CollectionUserMessagePresentation(String.Empty, userText, String.Empty, String.Empty, technicalDetail);
		}

		internal static CollectionUserMessagePresentation ForDependency(string technicalDetail)
		{
			return new CollectionUserMessagePresentation(String.Empty, SanitizeInternalTerminology(technicalDetail),
				"Collections.Messages.Next.ResolveDependency", "Resolve this member requirement before the Collection can be installed.", technicalDetail);
		}

		internal static CollectionUserMessagePresentation ForImpact(CollectionConflictImpactStatus status, string technicalDetail)
		{
			return status == CollectionConflictImpactStatus.Ready
				? new CollectionUserMessagePresentation(String.Empty, SanitizeInternalTerminology(technicalDetail), String.Empty, String.Empty, technicalDetail)
				: new CollectionUserMessagePresentation(String.Empty, SanitizeInternalTerminology(technicalDetail),
					"Collections.Messages.Next.ResolveImpact", "Review this conflict or limitation. NMM will not install the Collection until it has a supported resolution.", technicalDetail);
		}

		internal static CollectionUserMessagePresentation ForProviderMessage(string technicalDetail, bool blocking)
		{
			return blocking
				? Message("Collections.Messages.Provider.RequiredData",
					"Nexus could not provide the Collection data required for this step.",
					"Collections.Messages.Next.ResolveProvider", "Check the Collection source or Nexus connection, then prepare it again.", technicalDetail)
				: Message("Collections.Messages.Provider.OptionalData",
					"Some optional Collection information could not be loaded. This does not by itself block installation.",
					String.Empty, String.Empty, technicalDetail);
		}

		internal static CollectionUserMessagePresentation ForRetainedSourceIssue(string technicalDetail)
		{
			return Message("Collections.Messages.Association.RetainedSource",
				"NMM could not verify all retained source details for this installed Collection.",
				"Collections.Messages.Next.InspectInstalledCollection", "Inspect the installed Collection before attempting another management action.", technicalDetail);
		}

		internal static CollectionUserMessagePresentation ForFailure(string code, string technicalDetail)
		{
			switch (code ?? String.Empty)
			{
				case "preview.unexpected-failure":
					return Message("Collections.Messages.Failure.Preview", "NMM could not load this Collection preview.",
						"Collections.Messages.Next.OpenCollectionAgain", "Check the Collection link and open it again.", technicalDetail);
				case "association.presentation-failed":
					return Message("Collections.Messages.Failure.AssociationPresentation", "NMM could not load the retained details for this installed Collection. The Collection remains tracked.",
						"Collections.Messages.Next.InspectInstalledCollection", "Inspect the installed Collection state before attempting another management action.", technicalDetail);
				case "association.detach-failed":
					return Message("Collections.Messages.Failure.Detach", "NMM could not stop tracking this Collection. Installed content was not intentionally changed.",
						"Collections.Messages.Next.InspectInstalledCollection", "Inspect the installed Collection state before attempting another management action.", technicalDetail);
				case "association.remove-failed":
					return Message(String.Empty, SanitizeInternalTerminology(technicalDetail),
						"Collections.Messages.Next.ResolveRemovalFailure", "Resolve the reported problem before reviewing removal again.", technicalDetail);
				case "local-restore.failed":
					return Message("Collections.Messages.Failure.LocalRestore", "Local Collection restore stopped before NMM could verify a complete result.",
						"Collections.Messages.Next.CheckRecovery", "Check the recovery status before making further managed changes.", technicalDetail);
				case "capture.failed":
					return Message("Collections.Messages.Failure.Capture", "The Local Collection could not be saved. No capture was published.",
						"Collections.Messages.Next.ResolveCaptureFailure", "Review the current setup and resolve the reported problem before saving again.", technicalDetail);
				case "bundle.import-failed":
					return Message("Collections.Messages.Failure.BundleImport", "The Collection bundle could not be imported and retained.",
						"Collections.Messages.Next.ImportMatchingBundle", "Choose a valid bundle or collection.json for the currently displayed revision.", technicalDetail);
				case "workflow.prepare-failed":
				case "workflow.resume-failed":
					return Message("Collections.Messages.Failure.Preparation", "Collection preparation stopped unexpectedly.",
						"Collections.Messages.Next.PrepareAgain", "Review the errors below, then choose Download / Prepare to build a fresh review when it is safe to do so.", technicalDetail);
				case "workflow.review-invalid":
					return Message("Collections.Messages.Failure.ReviewInvalid", "The saved Collection review is no longer valid for the current setup.",
						"Collections.Messages.Next.PrepareAgain", "Choose Download / Prepare to build a fresh review.", technicalDetail);
				case "workflow.apply-failed":
					return Message("Collections.Messages.Failure.Apply", "Installation stopped unexpectedly before NMM could present a verified final result.",
						"Collections.Messages.Next.CheckRecovery", "Check the recovery status before making further managed changes.", technicalDetail);
				case "workflow.recovery-failed":
					return Message("Collections.Messages.Failure.Recovery", "NMM could not reconcile an incomplete Collection operation automatically.",
						"Collections.Messages.Next.CheckRecovery", "Do not start another managed Collection change until the recovery state is understood.", technicalDetail);
				case "acquisition.overwrite-policy-conflict":
					return Message("Collections.Messages.Failure.OverwritePolicyConflict", "The same mod archive is already being acquired by another Collection preparation with a different overwrite choice.",
						"Collections.Messages.Next.WaitForSharedAcquisition", "Wait for the current shared download/import to finish, then choose Download / Prepare again.", technicalDetail);
				case "acquisition.open-page-failed":
					return Message("Collections.Messages.Failure.OpenDownloadPage", "NMM could not open the selected mod's Nexus download page.",
						"Collections.Messages.Next.OpenDownloadManually", "Open the Nexus page manually for this mod, then return here and continue preparation.", technicalDetail);
				default:
					return FromRaw(technicalDetail, String.Empty);
			}
		}

		internal static CollectionUserMessagePresentation FromRaw(string technicalDetail, string nextActionFallback)
		{
			return new CollectionUserMessagePresentation(String.Empty, SanitizeInternalTerminology(technicalDetail),
				String.Empty, nextActionFallback ?? String.Empty, technicalDetail);
		}

		internal static string SanitizeInternalTerminology(string text)
		{
			if (String.IsNullOrWhiteSpace(text))
				return String.Empty;

			string sanitized = text.Replace("C6.2-C6.4", "member matching, dependency planning, and impact review")
				.Replace("C7.2-C7.6", "Local Collection capture snapshots");
			sanitized = InternalStagePattern.Replace(sanitized, match => DescribeInternalStage(match.Value));
			sanitized = GateAPattern.Replace(sanitized, "the supported automatic Collection capability");
			sanitized = GateLPattern.Replace(sanitized, "the supported Local Collection restore capability");
			return sanitized;
		}

		private static CollectionUserMessagePresentation Message(string messageKey, string messageFallback,
			string nextActionKey, string nextActionFallback, string technicalDetail)
		{
			return new CollectionUserMessagePresentation(messageKey, messageFallback, nextActionKey, nextActionFallback, technicalDetail);
		}

		private static string DescribeInternalStage(string stage)
		{
			string value = (stage ?? String.Empty).ToUpperInvariant();
			if (value.StartsWith("C6.15", StringComparison.Ordinal)) return "Collection workflow preparation";
			if (value == "C6.1") return "installed-state inspection";
			if (value == "C6.2") return "member compatibility matching";
			if (value == "C6.3") return "dependency planning";
			if (value == "C6.4") return "change-impact review";
			if (value == "C6.5") return "Collection workflow";
			if (value == "C6.6") return "installation preparation";
			if (value == "C6.7") return "installation";
			if (value == "C6.8") return "installed-state verification";
			if (value == "C6.9") return "restart recovery";
			if (value == "C6.10") return "Collection state update";
			if (value == "C6.11") return "drift tracking";
			if (value == "C6.12") return "pin and override handling";
			if (value == "C6.13") return "stop-tracking";
			if (value == "C6.14") return "Collection effect removal";
			if (value == "C7.9") return "Local Collection restore review";
			if (value == "C7.10A") return "Local Collection member restore";
			if (value == "C7.10B1") return "file ownership restore";
			if (value == "C7.10B2") return "scripted replay restore";
			if (value == "C7.10B3") return "plugin-state restore";
			if (value == "C7.10B4") return "configuration restore";
			if (value == "C7.10B5") return "game-value restore";
			if (value == "C7.10B6") return "user-metadata restore";
			if (value == "C7.11") return "profile and Collection tracking restore";
			if (value == "C1") return "Collection definition processing";
			if (value == "C2") return "Collection source loading";
			if (value == "C3") return "installation operation handling";
			if (value == "C4" || value.StartsWith("C4.", StringComparison.Ordinal)) return "Collection content handling";
			if (value == "C5" || value.StartsWith("C5.", StringComparison.Ordinal)) return "installation recipe preparation";
			if (value == "C6") return "Collection installation workflow";
			if (value == "C7" || value.StartsWith("C7.", StringComparison.Ordinal)) return "Local Collection processing";
			if (value == "C8" || value.StartsWith("C8.", StringComparison.Ordinal)) return "Collection replacement workflow";
			return "Collection processing";
		}
	}
}
