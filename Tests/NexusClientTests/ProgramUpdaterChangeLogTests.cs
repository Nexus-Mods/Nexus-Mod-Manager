using System;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;
using Nexus.Client.Updating;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies release-note formatting without downloading releases or opening the update dialog.
	/// </summary>
	[TestFixture]
	public class ProgramUpdaterChangeLogTests
	{
		/// <summary>
		/// Both the historical and current GitHub heading formats retain their section contents.
		/// </summary>
		[TestCase("**New Features**\r\n- Feature detail\r\n**Bugfixes**\r\n- Fix detail")]
		[TestCase("# Release Notes\r\nIntro\r\n## New Features\r\n- Feature detail\r\n## Bugfixes & Improvements\r\n- Fix detail")]
		[TestCase("## NEW FEATURES\n- Feature detail\n## BUG FIXES\n- Fix detail")]
		public void ConstructChangeLog_RecognizedHeadings_RetainsDetails(string body)
		{
			string html = ConstructChangeLog(body);

			Assert.That(html, Does.Contain("<li>Feature detail</li>"));
			Assert.That(html, Does.Contain("<li>Fix detail</li>"));
			Assert.That(html, Does.Not.Contain("<li></li>"));
		}

		/// <summary>
		/// A release may contain only features or fixes without depending on the other heading.
		/// </summary>
		[TestCase("**Bugfixes**\n- Only detail")]
		[TestCase("## Bugfixes & Improvements\n- Only detail")]
		[TestCase("**New Features**\n- Only detail")]
		[TestCase("## New Features\n- Only detail")]
		[TestCase("## Bugfixes & Improvements\n### Game Storage\n- Only detail")]
		public void ConstructChangeLog_OneSection_RetainsDetails(string body)
		{
			string html = ConstructChangeLog(body);

			Assert.That(html, Does.Contain("<li>Only detail</li>"));
			Assert.That(html, Does.Not.Contain("<li></li>"));
			Assert.That(html.Split(new[] { "<h3>" }, StringSplitOptions.None).Length, Is.EqualTo(2));
		}

		/// <summary>
		/// Unrecognized release formats fall back to their text rather than producing empty sections.
		/// </summary>
		[TestCase("Release summary\n- Only detail")]
		[TestCase("## Maintenance\n- Only detail")]
		public void ConstructChangeLog_UnrecognizedHeadings_ShowsReleaseText(string body)
		{
			string html = ConstructChangeLog(body);

			Assert.That(html, Does.Contain("<li>Only detail</li>"));
			Assert.That(html, Does.Not.Contain("<h3>"));
		}

		/// <summary>
		/// Release text remains visible HTML text while basic Markdown emphasis is retained.
		/// </summary>
		[Test]
		public void ConstructChangeLog_ReleaseMarkup_IsEncoded()
		{
			string html = ConstructChangeLog("## Bugfixes & Improvements\n\n- Fixed **Continue** for `a<b` & <script>alert(1)</script>\n\n");

			Assert.That(html, Does.Contain("<strong>Continue</strong>"));
			Assert.That(html, Does.Contain("<code>a&lt;b</code> &amp; &lt;script&gt;"));
			Assert.That(html, Does.Not.Contain("<script>"));
			Assert.That(html, Does.Not.Contain("<li></li>"));
		}

		/// <summary>
		/// Missing notes and empty sections do not generate phantom list items.
		/// </summary>
		[TestCase(null)]
		[TestCase("")]
		[TestCase("**New Features**\n\n**Bugfixes**\n\n")]
		public void ConstructChangeLog_EmptyNotes_DoesNotGenerateEmptyItems(string body)
		{
			string html = ConstructChangeLog(body);

			Assert.That(html, Does.Not.Contain("<li>"));
			Assert.That(html, Does.Not.Contain("<h3>"));
			Assert.That(html, Does.Contain("</body></html>"));
		}

		/// <summary>
		/// Supplies an in-memory GitHub response to the real changelog construction path.
		/// </summary>
		private static string ConstructChangeLog(string body)
		{
			ProgramUpdater updater = (ProgramUpdater)FormatterServices.GetUninitializedObject(typeof(ProgramUpdater));
			typeof(ProgramUpdater).GetField("_releases", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(updater,
				new JArray(new JObject { ["tag_name"] = "0.93.12", ["body"] = body }));
			MethodInfo method = typeof(ProgramUpdater).GetMethod("ConstructChangeLog", BindingFlags.Instance | BindingFlags.NonPublic);
			return (string)method.Invoke(updater, new object[] { new Version("0.93.11"), new Version("0.93.12") });
		}
	}
}
