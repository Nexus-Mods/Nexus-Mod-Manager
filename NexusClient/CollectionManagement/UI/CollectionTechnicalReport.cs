using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Nexus.Client.OnlineServices.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Read-only support snapshot exported from the Collections surface.</summary>
	internal sealed class CollectionTechnicalReportSnapshot
	{
		internal const string CurrentSchemaVersion = "nmm-ce.collections.technical-report/1";

		internal CollectionTechnicalReportSnapshot(string nmmVersion)
		{
			SchemaVersion = CurrentSchemaVersion;
			CreatedUtc = DateTimeOffset.UtcNow;
			NmmVersion = nmmVersion ?? String.Empty;
			Members = new List<CollectionTechnicalReportMember>();
			Acquisitions = new List<CollectionTechnicalReportAcquisition>();
			ReviewItems = new List<CollectionTechnicalReportReviewItem>();
			Recovery = new List<CollectionTechnicalReportRecoveryItem>();
			Exceptions = new List<CollectionTechnicalReportException>();
			UnavailableData = new List<string>();
		}

		public string SchemaVersion { get; private set; }
		public DateTimeOffset CreatedUtc { get; private set; }
		public string NmmVersion { get; private set; }
		public CollectionTechnicalReportTarget Target { get; internal set; }
		public CollectionTechnicalReportContext Context { get; internal set; }
		public CollectionTechnicalReportCollection Collection { get; internal set; }
		public CollectionTechnicalReportOperation Operation { get; internal set; }
		public List<CollectionTechnicalReportMember> Members { get; private set; }
		public List<CollectionTechnicalReportAcquisition> Acquisitions { get; private set; }
		public List<CollectionTechnicalReportReviewItem> ReviewItems { get; private set; }
		public List<CollectionTechnicalReportRecoveryItem> Recovery { get; private set; }
		public CollectionTechnicalReportProgress Progress { get; internal set; }
		public CollectionPerformanceSnapshot Performance { get; internal set; }
		public List<CollectionTechnicalReportException> Exceptions { get; private set; }
		public List<string> UnavailableData { get; private set; }
	}

	internal sealed class CollectionTechnicalReportTarget
	{
		public string GameId { get; internal set; }
		public string GameName { get; internal set; }
		public string TargetFingerprint { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportContext
	{
		public string Kind { get; internal set; }
		public int Generation { get; internal set; }
		public string RevisionIdentity { get; internal set; }
		public string OperationIdentity { get; internal set; }
		public string AssociationIdentity { get; internal set; }
		public string LocalCaptureIdentity { get; internal set; }
		public string CompatibilityStatus { get; internal set; }
		public string ContentStatus { get; internal set; }
		public string AppliedStatus { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportCollection
	{
		public string Identity { get; internal set; }
		public string DisplayName { get; internal set; }
		public string Curator { get; internal set; }
		public string RevisionIdentity { get; internal set; }
		public string RevisionLabel { get; internal set; }
		public string Locator { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportOperation
	{
		internal CollectionTechnicalReportOperation()
		{
			NativeChildren = new List<CollectionTechnicalReportNativeChild>();
		}

		public string Identity { get; internal set; }
		public string Kind { get; internal set; }
		public string Phase { get; internal set; }
		public string ResultState { get; internal set; }
		public string PlanIdentity { get; internal set; }
		public long CheckpointSequence { get; internal set; }
		public bool RequiresRecovery { get; internal set; }
		public bool HasCrossedNativeBoundary { get; internal set; }
		public List<CollectionTechnicalReportNativeChild> NativeChildren { get; private set; }
	}

	internal sealed class CollectionTechnicalReportNativeChild
	{
		public int Sequence { get; internal set; }
		public string Member { get; internal set; }
		public string Action { get; internal set; }
		public string OperationId { get; internal set; }
		public string AttemptId { get; internal set; }
		public string Origin { get; internal set; }
		public string Checkpoint { get; internal set; }
		public string ReportedStatus { get; internal set; }
		public string Durability { get; internal set; }
		public string Message { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportMember
	{
		public string MemberKey { get; internal set; }
		public int SourceOrdinal { get; internal set; }
		public string DisplayName { get; internal set; }
		public string Requirement { get; internal set; }
		public bool Selected { get; internal set; }
		public string Compatibility { get; internal set; }
		public string Artifact { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportAcquisition
	{
		public string MemberKey { get; internal set; }
		public string Disposition { get; internal set; }
		public string MatchDisposition { get; internal set; }
		public string MatchReason { get; internal set; }
		public string RequestId { get; internal set; }
		public string QueueOperationId { get; internal set; }
		public string ArchiveSource { get; internal set; }
		public string VerificationBasis { get; internal set; }
		public string RetainedArtifactId { get; internal set; }
		public string ContentHash { get; internal set; }
		public long? ByteLength { get; internal set; }
		public string PendingActions { get; internal set; }
		public string BrowserUri { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportReviewItem
	{
		public string Severity { get; internal set; }
		public string Kind { get; internal set; }
		public string Code { get; internal set; }
		public string Subject { get; internal set; }
		public string Explanation { get; internal set; }
		public string NextAction { get; internal set; }
		public string TechnicalDetail { get; internal set; }
		public string MemberKey { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportRecoveryItem
	{
		public string Scope { get; internal set; }
		public string Status { get; internal set; }
		public string OperationIdentity { get; internal set; }
		public string Phase { get; internal set; }
		public string ResultState { get; internal set; }
		public string CaptureIdentity { get; internal set; }
		public string CaptureDisplayName { get; internal set; }
		public string CaptureRevisionLabel { get; internal set; }
		public string SourceDiagnostic { get; internal set; }
		public string Message { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportProgress
	{
		internal CollectionTechnicalReportProgress()
		{
			AcquisitionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
		}

		public string WorkflowStatus { get; internal set; }
		public string OperationPhase { get; internal set; }
		public string ActivityState { get; internal set; }
		public string ActivityPhase { get; internal set; }
		public bool CommandsLocked { get; internal set; }
		public bool WorkActive { get; internal set; }
		public bool BackgroundContinuation { get; internal set; }
		public long? ProgressCurrent { get; internal set; }
		public long? ProgressTotal { get; internal set; }
		public string ProgressBasis { get; internal set; }
		public string ArchiveOverwritePolicy { get; internal set; }
		public Dictionary<string, int> AcquisitionCounts { get; private set; }
		public bool EtaAvailable { get; internal set; }
		public bool EtaEstimating { get; internal set; }
		public long? EtaSeconds { get; internal set; }
		public long? EtaRemainingBytes { get; internal set; }
		public double? EtaBytesPerSecond { get; internal set; }
		public int EtaSampleCount { get; internal set; }
		public string EtaBasis { get; internal set; }
	}

	internal sealed class CollectionTechnicalReportException
	{
		public string Source { get; internal set; }
		public string Type { get; internal set; }
		public string Message { get; internal set; }
		public string StackTrace { get; internal set; }
	}

	/// <summary>Sanitizes arbitrary report text before it leaves the application.</summary>
	internal static class CollectionTechnicalReportSanitizer
	{
		private const string RedactedValue = "<redacted>";
		private const string LocalPathValue = "<local-path>";
		private static readonly string[] SensitiveQueryNames =
		{
			"key", "expires", "apikey", "api_key", "access_token", "refresh_token", "token", "authorization"
		};
		private static readonly Regex AbsoluteUriPattern = new Regex("\\b(?:https?|nxm|file)://[^\\s<>\"']+",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex WindowsPathPattern = new Regex("(?<![A-Za-z0-9])(?:[A-Za-z]:\\\\|\\\\\\\\)[^\\r\\n\\t\"'<>|]+",
			RegexOptions.CultureInvariant);
		private static readonly Regex UnixUserPathPattern = new Regex("(?<![A-Za-z0-9])/(?:Users|home)/[^\\s\"']+",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex SensitiveHeaderPattern = new Regex(@"\b(authorization|proxy-authorization|cookie|set-cookie|apikey|api_key)\s*:\s*[^\r\n]*",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex BearerPattern = new Regex(@"\bBearer\s+[A-Za-z0-9._~+\-/]+=*",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex NamedSecretPattern = new Regex(@"\b(apikey|api_key|access_token|refresh_token|authorization|proxy-authorization|cookie|set-cookie)\b\s*[:=]\s*[^\s,;]+",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		internal static string SanitizeText(string value)
		{
			if (String.IsNullOrEmpty(value))
				return value ?? String.Empty;

			string sanitized = AbsoluteUriPattern.Replace(value, SanitizeUriMatch);
			sanitized = WindowsPathPattern.Replace(sanitized, LocalPathValue);
			sanitized = UnixUserPathPattern.Replace(sanitized, LocalPathValue);
			sanitized = SensitiveHeaderPattern.Replace(sanitized, match => match.Groups[1].Value + ": " + RedactedValue);
			sanitized = BearerPattern.Replace(sanitized, "Bearer " + RedactedValue);
			sanitized = NamedSecretPattern.Replace(sanitized, match => match.Groups[1].Value + "=" + RedactedValue);
			return sanitized;
		}

		private static string SanitizeUriMatch(Match match)
		{
			Uri uri;
			if (!Uri.TryCreate(match.Value, UriKind.Absolute, out uri))
				return RedactedValue;
			if (StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, Uri.UriSchemeFile))
				return "file://" + LocalPathValue;
			try
			{
				return ApiDiagnosticSanitizer.SanitizeUri(uri, SensitiveQueryNames, redactAllQueryValues: true);
			}
			catch
			{
				return uri.GetLeftPart(UriPartial.Path);
			}
		}
	}

	/// <summary>Stable JSON writer for Collections technical support reports.</summary>
	internal static class CollectionTechnicalReportSerializer
	{
		private static readonly JsonSerializerSettings Settings = CreateSettings();

		internal static string Serialize(CollectionTechnicalReportSnapshot report)
		{
			if (report == null)
				throw new ArgumentNullException(nameof(report));
			return JsonConvert.SerializeObject(report, Formatting.Indented, Settings);
		}

		internal static void Save(string path, CollectionTechnicalReportSnapshot report)
		{
			if (String.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A report destination is required.", nameof(path));
			File.WriteAllText(path, Serialize(report), new UTF8Encoding(false));
		}

		private static JsonSerializerSettings CreateSettings()
		{
			var settings = new JsonSerializerSettings
			{
				ContractResolver = new CamelCasePropertyNamesContractResolver(),
				NullValueHandling = NullValueHandling.Include,
				DateFormatHandling = DateFormatHandling.IsoDateFormat
			};
			settings.Converters.Add(new StringEnumConverter());
			return settings;
		}
	}
}
