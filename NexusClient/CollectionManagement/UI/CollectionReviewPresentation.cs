using System;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Severity used by the Collections Review / issues presentation independently from localized status text.</summary>
	public enum CollectionReviewSeverity
	{
		Error = 1,
		Warning = 2,
		Info = 3
	}

	/// <summary>Semantic role of one row in the Collections review surface.</summary>
	public enum CollectionReviewItemKind
	{
		Diagnostic = 1,
		ManualAction = 2,
		PlannedEffect = 3,
		Progress = 4
	}

	/// <summary>
	/// Immutable typed presentation row backing the Collections Review / issues surface.
	/// </summary>
	/// <remarks>
	/// Severity and kind are deliberately independent from <see cref="StatusText"/> so filtering and report export never
	/// need to infer semantics by comparing localized display strings. Planned effects remain first-class rows but are
	/// distinguishable from diagnostics before UX filtering is introduced.
	/// </remarks>
	public sealed class CollectionReviewItem
	{
		public CollectionReviewItem(CollectionReviewSeverity severity, CollectionReviewItemKind kind,
			string statusText, string code, string subject, string explanation,
			CollectionMemberKey memberKey = null, string nextAction = null, string technicalDetail = null)
		{
			if (!Enum.IsDefined(typeof(CollectionReviewSeverity), severity))
				throw new ArgumentOutOfRangeException(nameof(severity));
			if (!Enum.IsDefined(typeof(CollectionReviewItemKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (String.IsNullOrWhiteSpace(code))
				throw new ArgumentException("A stable review item code is required.", nameof(code));

			Severity = severity;
			Kind = kind;
			StatusText = statusText ?? String.Empty;
			Code = code;
			Subject = subject ?? String.Empty;
			Explanation = explanation ?? String.Empty;
			MemberKey = memberKey;
			NextAction = nextAction ?? String.Empty;
			TechnicalDetail = technicalDetail ?? String.Empty;
		}

		public CollectionReviewSeverity Severity { get; }
		public CollectionReviewItemKind Kind { get; }
		public string StatusText { get; }
		public string Code { get; }
		public string Subject { get; }
		public string Explanation { get; }
		public CollectionMemberKey MemberKey { get; }
		public string NextAction { get; }
		public string TechnicalDetail { get; }
	}

	/// <summary>Pure visibility policy for the Review / issues filters.</summary>
	public static class CollectionReviewPresentationFilter
	{
		public static bool IsSeverityFiltered(CollectionReviewItem item)
		{
			if (item == null)
				throw new ArgumentNullException(nameof(item));

			return item.Kind == CollectionReviewItemKind.Diagnostic || item.Kind == CollectionReviewItemKind.Progress;
		}

		public static bool IsPersistentReviewAction(CollectionReviewItem item)
		{
			if (item == null)
				throw new ArgumentNullException(nameof(item));

			return item.Kind == CollectionReviewItemKind.ManualAction || item.Kind == CollectionReviewItemKind.PlannedEffect;
		}

		public static bool MatchesSeverity(CollectionReviewItem item, bool showErrors, bool showWarnings, bool showInfo)
		{
			if (item == null)
				throw new ArgumentNullException(nameof(item));

			switch (item.Severity)
			{
				case CollectionReviewSeverity.Error:
					return showErrors;
				case CollectionReviewSeverity.Warning:
					return showWarnings;
				case CollectionReviewSeverity.Info:
					return showInfo;
				default:
					return false;
			}
		}
	}

	/// <summary>Pure semantic classification helpers used by the WinForms adapter and focused tests.</summary>
	public static class CollectionReviewPresentationClassifier
	{
		public static CollectionReviewSeverity ForCapabilityIssue(CollectionCompatibilityStatus status, bool affectsSelectedOperation)
		{
			if (!affectsSelectedOperation)
				return CollectionReviewSeverity.Warning;
			return status == CollectionCompatibilityStatus.ActionRequired || status == CollectionCompatibilityStatus.Unsupported
				? CollectionReviewSeverity.Error
				: CollectionReviewSeverity.Info;
		}

		public static CollectionReviewSeverity ForPreparation(CollectionAdditiveWorkflowPreparationStatus status,
			bool hasManualAction, bool hasBlockedAcquisition)
		{
			if (status == CollectionAdditiveWorkflowPreparationStatus.ReadyForReview)
				return CollectionReviewSeverity.Info;
			if (status == CollectionAdditiveWorkflowPreparationStatus.AwaitingInput && !hasManualAction && !hasBlockedAcquisition)
				return CollectionReviewSeverity.Info;
			return CollectionReviewSeverity.Error;
		}

		public static CollectionReviewSeverity ForAcquisition(CollectionMemberAcquisitionDisposition disposition)
		{
			switch (disposition)
			{
				case CollectionMemberAcquisitionDisposition.ReadyInstalled:
				case CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive:
				case CollectionMemberAcquisitionDisposition.PremiumQueued:
				case CollectionMemberAcquisitionDisposition.BundledQueued:
				case CollectionMemberAcquisitionDisposition.DirectQueued:
					return CollectionReviewSeverity.Info;
				default:
					return CollectionReviewSeverity.Error;
			}
		}

		public static CollectionReviewItemKind KindForAcquisition(CollectionMemberAcquisitionDisposition disposition)
		{
			if (disposition == CollectionMemberAcquisitionDisposition.ManualInputRequired ||
				disposition == CollectionMemberAcquisitionDisposition.RestartActionRequired)
				return CollectionReviewItemKind.ManualAction;
			return disposition == CollectionMemberAcquisitionDisposition.Blocked
				? CollectionReviewItemKind.Diagnostic
				: CollectionReviewItemKind.Progress;
		}

		public static CollectionReviewSeverity ForImpact(CollectionConflictImpactStatus status)
		{
			return status == CollectionConflictImpactStatus.Ready
				? CollectionReviewSeverity.Info
				: CollectionReviewSeverity.Error;
		}

		public static CollectionReviewSeverity ForRecovery(CollectionAdditiveWorkflowRecoveryStatus status)
		{
			return status == CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired ||
				status == CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume
				? CollectionReviewSeverity.Warning
				: CollectionReviewSeverity.Error;
		}

		public static CollectionReviewSeverity ForApply(CollectionAdditiveWorkflowApplyStatus status)
		{
			return status == CollectionAdditiveWorkflowApplyStatus.Committed
				? CollectionReviewSeverity.Info
				: CollectionReviewSeverity.Error;
		}

		public static CollectionReviewSeverity ForAssociation(CollectionAssociationState state)
		{
			switch (state)
			{
				case CollectionAssociationState.Applied:
					return CollectionReviewSeverity.Info;
				case CollectionAssociationState.Modified:
					return CollectionReviewSeverity.Warning;
				default:
					return CollectionReviewSeverity.Error;
			}
		}
	}
}
