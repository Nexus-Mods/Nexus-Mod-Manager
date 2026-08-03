namespace NexusClientTests
{
	using Nexus.Client.ModRepositories;
	using NUnit.Framework;

	/// <summary>
	/// Verifies sanitization and readable rendering of Nexus description markup.
	/// </summary>
	[TestFixture]
	public class NexusDescriptionFormatterTests
	{
		/// <summary>
		/// Confirms that plain text remains readable and line breaks are preserved.
		/// </summary>
		[Test]
		public void ToSafeHtml_PlainText_PreservesTextAndLineBreaks()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("First line\r\nSecond line");

			StringAssert.Contains("First line<br>Second line", result);
		}

		/// <summary>
		/// Confirms that common Nexus BBCode is rendered without exposing raw tags.
		/// </summary>
		[Test]
		public void ToSafeHtml_BbCode_ConvertsFormattingAndBreaks()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("[b]Bold[/b]<br />[i]Italic[/i]");

			StringAssert.Contains("<b>Bold</b><br><i>Italic</i>", result);
			StringAssert.DoesNotContain("[b]", result);
			StringAssert.DoesNotContain("&lt;br", result);
		}

		/// <summary>
		/// Confirms that named links remain readable without injecting external HTML.
		/// </summary>
		[Test]
		public void ToSafeHtml_NamedUrl_PreservesLabelAndDestination()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("[url=https://www.nexusmods.com/test/mods/1]Nexus page[/url]");

			StringAssert.Contains("Nexus page (https://www.nexusmods.com/test/mods/1)", result);
			StringAssert.DoesNotContain("[url", result);
		}

		/// <summary>
		/// Confirms that supported HTML formatting is retained while script markup is removed.
		/// </summary>
		[Test]
		public void ToSafeHtml_Html_RemovesUnsafeTags()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("<b>Safe</b><script>alert('x')</script><br>After");

			StringAssert.Contains("<b>Safe</b>alert(&#39;x&#39;)<br>After", result);
			StringAssert.DoesNotContain("script", result);
		}

		/// <summary>
		/// Confirms that remote images are represented as text rather than loaded into the preview.
		/// </summary>
		[Test]
		public void ToSafeHtml_ImageTag_ProducesTextReference()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("[img]https://example.com/image.png[/img]");

			StringAssert.Contains("Image: https://example.com/image.png", result);
			StringAssert.DoesNotContain("<img", result);
		}

		/// <summary>
		/// Confirms that nested HTML entities from legacy descriptions are decoded before rendering.
		/// </summary>
		[Test]
		public void ToSafeHtml_DoubleEncodedEntities_RendersReadableCharacters()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("Set it to &amp;quot;Yes&amp;quot;.");

			StringAssert.Contains("Set it to &quot;Yes&quot;.", result);
			StringAssert.DoesNotContain("&amp;quot;", result);
		}

		/// <summary>
		/// Confirms that decoding nested entities does not allow encoded script markup into the preview.
		/// </summary>
		[Test]
		public void ToSafeHtml_DoubleEncodedUnsafeHtml_RemainsSanitized()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("&amp;lt;script&amp;gt;alert(1)&amp;lt;/script&amp;gt;");

			StringAssert.Contains("alert(1)", result);
			StringAssert.DoesNotContain("script", result);
		}

		/// <summary>
		/// Confirms that legacy Nexus heading, list-item, and line tags do not leak into the preview.
		/// </summary>
		[Test]
		public void ToSafeHtml_LegacyStructureTags_ConvertsWithoutRawMarkup()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("[heading]Installation[/heading][list][*]First[/*][*]Second[/*][/list][line]");

			StringAssert.Contains("<b>Installation</b>", result);
			StringAssert.Contains("• First", result);
			StringAssert.Contains("• Second", result);
			StringAssert.Contains("<hr>", result);
			StringAssert.DoesNotContain("[heading]", result);
			StringAssert.DoesNotContain("[/*]", result);
			StringAssert.DoesNotContain("[line]", result);
		}

		/// <summary>
		/// Confirms that legacy YouTube tags become readable safe destinations.
		/// </summary>
		[Test]
		public void ToSafeHtml_YouTubeTag_ProducesReadableReference()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("[youtube]jn9qWvSiz-4[/youtube]");

			StringAssert.Contains("YouTube: https://www.youtube.com/watch?v=jn9qWvSiz-4", result);
			StringAssert.DoesNotContain("[youtube]", result);
		}

		/// <summary>
		/// Confirms that both inline and aligned Nexus image syntaxes are converted to safe references.
		/// </summary>
		[Test]
		public void ToSafeHtml_LegacyImageTags_ProducesTextReferences()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml("[img=https://example.com/inline.png][img align=center]https://example.com/aligned.png[/img]");

			StringAssert.Contains("Image: https://example.com/inline.png", result);
			StringAssert.Contains("Image: https://example.com/aligned.png", result);
			StringAssert.DoesNotContain("[img", result);
		}

		/// <summary>
		/// Confirms that empty descriptions show a useful placeholder.
		/// </summary>
		[Test]
		public void ToSafeHtml_EmptyDescription_ShowsPlaceholder()
		{
			string result = NexusDescriptionFormatter.ToSafeHtml(null);

			StringAssert.Contains("No description available.", result);
		}
	}
}
