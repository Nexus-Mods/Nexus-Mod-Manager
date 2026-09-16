namespace UtilTests
{
    using System;
    using System.Reflection;
    using Nexus.Client.Util.Downloader;
    using NUnit.Framework;

    /// <summary>
    /// Verifies that the low-level downloader never exposes signed URL authorization material in diagnostics.
    /// </summary>
    [TestFixture]
    public class FileDownloaderDiagnosticTests
    {
        /// <summary>
        /// Ensures all query values, fragments, and embedded user information are removed regardless of provider naming.
        /// </summary>
        [Test]
        public void SanitizeUriForDiagnostics_StripsProviderSpecificAuthorizationMaterial()
        {
            MethodInfo sanitizer = typeof(FileDownloader).GetMethod(
                "SanitizeUriForDiagnostics",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(sanitizer);

            var uri = new Uri("https://user:password@cdn.example.test/file?X-Amz-Signature=signed-value&custom_auth=secret-value#fragment-token");
            string sanitized = (string)sanitizer.Invoke(null, new object[] { uri });

            StringAssert.Contains("https://cdn.example.test/file", sanitized);
            StringAssert.DoesNotContain("user", sanitized);
            StringAssert.DoesNotContain("password", sanitized);
            StringAssert.DoesNotContain("signed-value", sanitized);
            StringAssert.DoesNotContain("secret-value", sanitized);
            StringAssert.DoesNotContain("fragment-token", sanitized);
        }
    }
}
