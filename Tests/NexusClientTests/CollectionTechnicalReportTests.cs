using System;
using NUnit.Framework;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionTechnicalReportTests
	{
		[Test]
		public void Sanitizer_RedactsSignedUrlValuesAndLocalPaths()
		{
			string source = "Download https://cdn.example.test/file.zip?key=secret&expires=123 from C:\\Users\\Fabio\\Downloads\\file.zip";
			string sanitized = CollectionTechnicalReportSanitizer.SanitizeText(source);

			Assert.That(sanitized, Does.Not.Contain("secret"));
			Assert.That(sanitized, Does.Not.Contain("123"));
			Assert.That(sanitized, Does.Not.Contain("Fabio"));
			Assert.That(sanitized, Does.Contain("redacted"));
			Assert.That(sanitized, Does.Contain("<local-path>"));
		}

		[Test]
		public void Sanitizer_RedactsBearerAndNamedCredentialValues()
		{
			string source = "Authorization: Bearer abc.def.ghi api_key=top-secret access_token=value";
			string sanitized = CollectionTechnicalReportSanitizer.SanitizeText(source);

			Assert.That(sanitized, Does.Not.Contain("abc.def.ghi"));
			Assert.That(sanitized, Does.Not.Contain("top-secret"));
			Assert.That(sanitized, Does.Not.Contain("access_token=value"));
			Assert.That(sanitized, Does.Contain("<redacted>"));
		}

		[Test]
		public void Serializer_EmitsStableSchemaAndEveryReviewItemIndependentOfUiFiltering()
		{
			var report = new CollectionTechnicalReportSnapshot("0.93-test")
			{
				Context = new CollectionTechnicalReportContext { Kind = "IncomingCollection", Generation = 4 },
				Progress = new CollectionTechnicalReportProgress { WorkflowStatus = "ready", EtaAvailable = false, EtaBasis = "unavailable" },
				Performance = CollectionPerformanceMetrics.Capture()
			};
			report.ReviewItems.Add(new CollectionTechnicalReportReviewItem
			{
				Severity = "Error", Kind = "Diagnostic", Code = "error.one", Explanation = "visible error"
			});
			report.ReviewItems.Add(new CollectionTechnicalReportReviewItem
			{
				Severity = "Info", Kind = "Progress", Code = "info.hidden", Explanation = "normally filtered info"
			});

			string json = CollectionTechnicalReportSerializer.Serialize(report);

			Assert.That(json, Does.Contain(CollectionTechnicalReportSnapshot.CurrentSchemaVersion));
			Assert.That(json, Does.Contain("error.one"));
			Assert.That(json, Does.Contain("info.hidden"));
			Assert.That(json, Does.Contain("0.93-test"));
			Assert.That(json, Does.Contain("nativeStateIndexBuildCount"));
			Assert.That(json, Does.Contain("reviewedSnapshotSerializedBytes"));
			Assert.That(json, Does.Contain("retainedManifestLoadCount"));
			Assert.That(json, Does.Contain("retainedArtifactVerificationCacheHitCount"));
			Assert.That(json, Does.Contain("uniqueRetainedArtifactOpenCount"));
			Assert.That(json, Does.Contain("archiveSourceReadCount"));
			Assert.That(json, Does.Contain("uniqueArchiveSourceMemberCount"));
			Assert.That(json, Does.Contain("uniqueArchiveSourcePathReadCount"));
			Assert.That(json, Does.Contain("archiveSourceReadBytes"));
			Assert.That(json, Does.Contain("preparedNativeIdentityRepeatCount"));
			Assert.That(json, Does.Contain("startupRecoveryMilliseconds"));
			Assert.That(json, Does.Contain("nxmUiDispatchMilliseconds"));
		}

		[Test]
		public void PerformanceMetrics_CountsArchiveSourceReadsByMemberAndPath()
		{
			CollectionPerformanceSnapshot before = CollectionPerformanceMetrics.Capture();
			CollectionMemberKey first = CollectionMemberKey.FromProvider("perf-" + Guid.NewGuid().ToString("N"));
			CollectionMemberKey second = CollectionMemberKey.FromProvider("perf-" + Guid.NewGuid().ToString("N"));

			CollectionPerformanceMetrics.RecordArchiveSourceRead(first, "textures\\a.dds", 10);
			CollectionPerformanceMetrics.RecordArchiveSourceRead(first, "textures\\a.dds", 10);
			CollectionPerformanceMetrics.RecordArchiveSourceRead(first, "textures\\b.dds", 20);
			CollectionPerformanceMetrics.RecordArchiveSourceRead(second, "meshes\\c.nif", 30);

			CollectionPerformanceSnapshot after = CollectionPerformanceMetrics.Capture();
			Assert.AreEqual(before.ArchiveSourceReadCount + 4, after.ArchiveSourceReadCount);
			Assert.AreEqual(before.ArchiveSourceReadBytes + 70, after.ArchiveSourceReadBytes);
			Assert.AreEqual(before.UniqueArchiveSourceMemberCount + 2, after.UniqueArchiveSourceMemberCount);
			Assert.AreEqual(before.UniqueArchiveSourcePathReadCount + 3, after.UniqueArchiveSourcePathReadCount);
		}

		[Test]
		public void Serializer_DoesNotRequireOperationOrPlanForEarlyFailureReport()
		{
			var report = new CollectionTechnicalReportSnapshot("0.93-test");
			report.UnavailableData.Add("operation: unavailable");
			report.Exceptions.Add(new CollectionTechnicalReportException
			{
				Source = "preview.unexpected-failure",
				Type = typeof(InvalidOperationException).FullName,
				Message = "preview failed"
			});

			string json = CollectionTechnicalReportSerializer.Serialize(report);

			Assert.That(json, Does.Contain("operation: unavailable"));
			Assert.That(json, Does.Contain("preview.unexpected-failure"));
			Assert.That(json, Does.Contain("\"operation\": null"));
		}
	}
}
