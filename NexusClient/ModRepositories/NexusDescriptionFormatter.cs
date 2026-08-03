namespace Nexus.Client.ModRepositories
{
	using System;
	using System.Net;
	using System.Text.RegularExpressions;

	/// <summary>
	/// Converts mixed Nexus Mods BBCode and HTML descriptions into sanitized HTML for DevExpress previews.
	/// </summary>
	public static class NexusDescriptionFormatter
	{
		private const string EmptyDescriptionHtml = "<div class='empty'>No description available.</div>";
		private static readonly Regex HtmlAnchorRegex = new Regex(@"&lt;a\b(?<attributes>.*?)&gt;(?<text>.*?)&lt;/a\s*&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlImageRegex = new Regex(@"&lt;img\b(?<attributes>.*?)/?&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlBreakRegex = new Regex(@"&lt;\s*br\s*/?\s*&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex HtmlParagraphStartRegex = new Regex(@"&lt;\s*(?:p|div)\b.*?&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlParagraphEndRegex = new Regex(@"&lt;\s*/\s*(?:p|div)\s*&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex HtmlListStartRegex = new Regex(@"&lt;\s*(?:ul|ol)\b.*?&gt;|&lt;\s*/\s*(?:ul|ol)\s*&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlListItemStartRegex = new Regex(@"&lt;\s*li\b.*?&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlListItemEndRegex = new Regex(@"&lt;\s*/\s*li\s*&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex HtmlQuoteTagRegex = new Regex(@"&lt;\s*(?<close>/?)\s*blockquote\b.*?&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlCodeTagRegex = new Regex(@"&lt;\s*(?<close>/?)\s*(?:code|pre)\b.*?&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlHeadingTagRegex = new Regex(@"&lt;\s*(?<close>/?)\s*h[1-6]\b.*?&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlFormattingTagRegex = new Regex(@"&lt;\s*(?<close>/?)\s*(?<tag>b|strong|i|em|u|s|strike)\b.*?&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex HtmlHorizontalRuleRegex = new Regex(@"&lt;\s*hr\s*/?\s*&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex HtmlCommentRegex = new Regex(@"&lt;!--[\s\S]*?--&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex RemainingHtmlTagRegex = new Regex(@"&lt;\s*/?\s*(?:a|img|br|p|div|ul|ol|li|blockquote|code|pre|h[1-6]|b|strong|i|em|u|s|strike|hr|script|style|iframe|object|embed|form|input|button|meta|link|table|tbody|thead|tfoot|tr|td|th|span|section|article|header|footer|nav|aside|details|summary|video|audio|source|canvas|svg|path|font|center|small|big|sub|sup|mark|del|ins|dl|dt|dd)\b[\s\S]*?&gt;", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeNamedUrlRegex = new Regex(@"\[url\s*=\s*(?<url>[^\]]+)\](?<text>.*?)\[/url\]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex BbCodeBareUrlRegex = new Regex(@"\[url\](?<url>.*?)\[/url\]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex BbCodeImageRegex = new Regex(@"\[img(?:=[^\]]+)?\](?<url>.*?)\[/img\]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex BbCodeFormattingTagRegex = new Regex(@"\[(?<close>/?)\s*(?<tag>b|strong|i|em|u|s|strike)\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeQuoteTagRegex = new Regex(@"\[(?<close>/?)\s*quote(?:=[^\]]+)?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeCodeTagRegex = new Regex(@"\[(?<close>/?)\s*(?:code|pre)\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeHeadingTagRegex = new Regex(@"\[(?<close>/?)\s*h[1-6]\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeListTagRegex = new Regex(@"\[/?\s*(?:list|olist|ul|ol)\s*(?:=[^\]]+)?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeListItemRegex = new Regex(@"\[\*\]|\[li\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeListItemEndRegex = new Regex(@"\[/li\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeBreakRegex = new Regex(@"\[br\s*/?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodeHorizontalRuleRegex = new Regex(@"\[hr\s*/?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex BbCodePresentationTagRegex = new Regex(@"\[/?\s*(?:center|left|right|color|size|font|spoiler|indent)(?:=[^\]]+)?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex RemainingKnownBbCodeRegex = new Regex(@"\[/?\s*(?:url|img|b|strong|i|em|u|s|strike|quote|code|pre|list|olist|ul|ol|li|center|left|right|color|size|font|spoiler|indent|h[1-6])(?:=[^\]]+)?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex ExcessiveLineBreakRegex = new Regex(@"\n{3,}", RegexOptions.Compiled);
		private static readonly Regex TrailingWhitespaceRegex = new Regex(@"[ \t]+\n", RegexOptions.Compiled);

		/// <summary>
		/// Converts a Nexus description into sanitized HTML suitable for a DevExpress <c>HtmlContentControl</c>.
		/// </summary>
		/// <param name="description">The raw plain-text, BBCode, HTML, or mixed description.</param>
		/// <returns>Sanitized HTML that preserves readable formatting without executing external markup.</returns>
		public static string ToSafeHtml(string description)
		{
			if (String.IsNullOrWhiteSpace(description))
				return EmptyDescriptionHtml;

			string value = description.Replace("\r\n", "\n").Replace('\r', '\n');
			value = WebUtility.HtmlEncode(WebUtility.HtmlDecode(value)).Replace("$", "&#36;");
			value = HtmlAnchorRegex.Replace(value, ReplaceHtmlAnchor);
			value = HtmlImageRegex.Replace(value, ReplaceHtmlImage);
			value = HtmlBreakRegex.Replace(value, "\n");
			value = HtmlParagraphStartRegex.Replace(value, String.Empty);
			value = HtmlParagraphEndRegex.Replace(value, "\n\n");
			value = HtmlListStartRegex.Replace(value, String.Empty);
			value = HtmlListItemStartRegex.Replace(value, "\n• ");
			value = HtmlListItemEndRegex.Replace(value, String.Empty);
			value = HtmlQuoteTagRegex.Replace(value, ReplaceQuoteTag);
			value = HtmlCodeTagRegex.Replace(value, ReplaceCodeTag);
			value = HtmlHeadingTagRegex.Replace(value, ReplaceHeadingTag);
			value = HtmlFormattingTagRegex.Replace(value, ReplaceFormattingTag);
			value = HtmlHorizontalRuleRegex.Replace(value, "\n<hr>\n");
			value = HtmlCommentRegex.Replace(value, String.Empty);
			value = RemainingHtmlTagRegex.Replace(value, String.Empty);
			value = BbCodeNamedUrlRegex.Replace(value, ReplaceNamedBbCodeUrl);
			value = BbCodeBareUrlRegex.Replace(value, ReplaceBareBbCodeUrl);
			value = BbCodeImageRegex.Replace(value, ReplaceBbCodeImage);
			value = BbCodeFormattingTagRegex.Replace(value, ReplaceFormattingTag);
			value = BbCodeQuoteTagRegex.Replace(value, ReplaceQuoteTag);
			value = BbCodeCodeTagRegex.Replace(value, ReplaceCodeTag);
			value = BbCodeHeadingTagRegex.Replace(value, ReplaceHeadingTag);
			value = BbCodeListTagRegex.Replace(value, String.Empty);
			value = BbCodeListItemRegex.Replace(value, "\n• ");
			value = BbCodeListItemEndRegex.Replace(value, String.Empty);
			value = BbCodeBreakRegex.Replace(value, "\n");
			value = BbCodeHorizontalRuleRegex.Replace(value, "\n<hr>\n");
			value = BbCodePresentationTagRegex.Replace(value, String.Empty);
			value = RemainingKnownBbCodeRegex.Replace(value, String.Empty);
			value = TrailingWhitespaceRegex.Replace(value, "\n");
			value = ExcessiveLineBreakRegex.Replace(value, "\n\n");
			value = value.Trim().Replace("\n", "<br>");

			return String.IsNullOrEmpty(value) ? EmptyDescriptionHtml : "<div class='description'>" + value + "</div>";
		}

		/// <summary>
		/// Converts an encoded HTML anchor into readable text followed by its safe destination.
		/// </summary>
		/// <param name="match">The encoded anchor match.</param>
		/// <returns>The readable link representation.</returns>
		private static string ReplaceHtmlAnchor(Match match)
		{
			string url = ExtractAttribute(WebUtility.HtmlDecode(match.Groups["attributes"].Value), "href");
			return CreateUrlReference(url, match.Groups["text"].Value);
		}

		/// <summary>
		/// Converts an encoded HTML image into a readable source reference without loading remote content.
		/// </summary>
		/// <param name="match">The encoded image match.</param>
		/// <returns>The readable image-source representation.</returns>
		private static string ReplaceHtmlImage(Match match)
		{
			string url = ExtractAttribute(WebUtility.HtmlDecode(match.Groups["attributes"].Value), "src");
			return CreateImageReference(url);
		}

		/// <summary>
		/// Converts a named BBCode URL into readable text followed by its safe destination.
		/// </summary>
		/// <param name="match">The BBCode URL match.</param>
		/// <returns>The readable link representation.</returns>
		private static string ReplaceNamedBbCodeUrl(Match match)
		{
			return CreateUrlReference(WebUtility.HtmlDecode(match.Groups["url"].Value), match.Groups["text"].Value);
		}

		/// <summary>
		/// Converts a bare BBCode URL into readable text.
		/// </summary>
		/// <param name="match">The bare BBCode URL match.</param>
		/// <returns>The encoded safe URL or original readable content.</returns>
		private static string ReplaceBareBbCodeUrl(Match match)
		{
			string encodedUrl = match.Groups["url"].Value.Trim();
			return CreateUrlReference(WebUtility.HtmlDecode(encodedUrl), encodedUrl);
		}

		/// <summary>
		/// Converts a BBCode image into a readable source reference without loading remote content.
		/// </summary>
		/// <param name="match">The BBCode image match.</param>
		/// <returns>The readable image-source representation.</returns>
		private static string ReplaceBbCodeImage(Match match)
		{
			return CreateImageReference(WebUtility.HtmlDecode(match.Groups["url"].Value));
		}

		/// <summary>
		/// Converts supported emphasis tags to their safe HTML equivalents.
		/// </summary>
		/// <param name="match">The formatting-tag match.</param>
		/// <returns>The normalized safe formatting tag.</returns>
		private static string ReplaceFormattingTag(Match match)
		{
			string tag = match.Groups["tag"].Value.ToLowerInvariant();
			if (tag == "strong")
				tag = "b";
			else if (tag == "em")
				tag = "i";
			else if (tag == "strike")
				tag = "s";

			return "<" + (match.Groups["close"].Success && match.Groups["close"].Value.Length > 0 ? "/" : String.Empty) + tag + ">";
		}

		/// <summary>
		/// Converts quote tags to a styled safe block.
		/// </summary>
		/// <param name="match">The quote-tag match.</param>
		/// <returns>The safe quote block tag.</returns>
		private static string ReplaceQuoteTag(Match match)
		{
			return match.Groups["close"].Success && match.Groups["close"].Value.Length > 0 ? "</div>\n" : "\n<div class='quote'>";
		}

		/// <summary>
		/// Converts code tags to a styled safe block.
		/// </summary>
		/// <param name="match">The code-tag match.</param>
		/// <returns>The safe code block tag.</returns>
		private static string ReplaceCodeTag(Match match)
		{
			return match.Groups["close"].Success && match.Groups["close"].Value.Length > 0 ? "</div>\n" : "\n<div class='code'>";
		}

		/// <summary>
		/// Converts heading tags to bold blocks without preserving untrusted heading attributes.
		/// </summary>
		/// <param name="match">The heading-tag match.</param>
		/// <returns>The safe heading representation.</returns>
		private static string ReplaceHeadingTag(Match match)
		{
			return match.Groups["close"].Success && match.Groups["close"].Value.Length > 0 ? "</b>\n" : "\n<b>";
		}

		/// <summary>
		/// Creates a readable URL reference while allowing only HTTP, HTTPS, and NXM destinations.
		/// </summary>
		/// <param name="url">The candidate destination.</param>
		/// <param name="encodedText">The already encoded visible link text.</param>
		/// <returns>The readable reference with unsafe destinations omitted.</returns>
		private static string CreateUrlReference(string url, string encodedText)
		{
			string displayText = String.IsNullOrWhiteSpace(encodedText) ? String.Empty : encodedText.Trim();
			Uri uri;
			if (!TryCreateSafeUri(url, out uri))
				return displayText;

			string encodedUrl = WebUtility.HtmlEncode(uri.ToString()).Replace("$", "&#36;");
			if (String.IsNullOrEmpty(displayText) || String.Equals(WebUtility.HtmlDecode(displayText), uri.ToString(), StringComparison.OrdinalIgnoreCase))
				return encodedUrl;

			return displayText + " (" + encodedUrl + ")";
		}

		/// <summary>
		/// Creates a readable image-source reference while preventing remote image loading.
		/// </summary>
		/// <param name="url">The candidate image source.</param>
		/// <returns>The encoded image-source reference, or an empty string when invalid.</returns>
		private static string CreateImageReference(string url)
		{
			Uri uri;
			return TryCreateSafeUri(url, out uri) ? "Image: " + WebUtility.HtmlEncode(uri.ToString()).Replace("$", "&#36;") : String.Empty;
		}

		/// <summary>
		/// Extracts a quoted or unquoted attribute value from decoded HTML attributes.
		/// </summary>
		/// <param name="attributes">The decoded attribute text.</param>
		/// <param name="attributeName">The attribute to retrieve.</param>
		/// <returns>The attribute value, or an empty string.</returns>
		private static string ExtractAttribute(string attributes, string attributeName)
		{
			if (String.IsNullOrWhiteSpace(attributes))
				return String.Empty;

			Match match = Regex.Match(attributes, "(?:^|\\s)" + Regex.Escape(attributeName) + "\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase);
			return match.Success ? match.Groups["value"].Value : String.Empty;
		}

		/// <summary>
		/// Tries to create an absolute URI using only schemes safe for display as external references.
		/// </summary>
		/// <param name="value">The candidate URI text.</param>
		/// <param name="uri">The resulting safe URI.</param>
		/// <returns><c>true</c> when the URI uses HTTP, HTTPS, or NXM.</returns>
		private static bool TryCreateSafeUri(string value, out Uri uri)
		{
			uri = null;
			if (String.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim().Trim('\'', '"'), UriKind.Absolute, out uri))
				return false;

			return String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
				String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
				String.Equals(uri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase);
		}
	}
}
