using System;
using NUnit.Framework;
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
				Progress = new CollectionTechnicalReportProgress { WorkflowStatus = "ready", EtaAvailable = false, EtaBasis = "unavailable" }
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
