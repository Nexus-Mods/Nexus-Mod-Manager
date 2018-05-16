using System;
using System.ComponentModel;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Web;
using System.Collections.Generic;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A label that allows use of HTML, BBCode, or RTF to format the text.
	/// </summary>
	public class FormattedLabel : Control
	{
		/// <summary>
		/// The current display mods of the label.
		/// </summary>
		protected enum Mode
		{
			/// <summary>
			/// Displaying plain text.
			/// </summary>
			Plain,

			/// <summary>
			/// Displaying RTF.
			/// </summary>
			RTF,

			/// <summary>
			/// Displaying HTML.
			/// </summary>
			HTML,

			/// <summary>
			/// Displaying BBCode.
			/// </summary>
			BBCode
		}

		private static Regex HTML_MATCHER = new Regex(@"(?i)(<[^>]*>)");
        	private static Regex HTML_DETECTION_MATCHER = new Regex(@"(?i)((<\s*br\s*/?\s*>)|(<\s*/[^>]*>))");
		private static Regex HTML_CLOSING_MATCHER = new Regex(@"(?i)(</[^>]*>)");
		private static Regex BBCODE_MATCHER = new Regex(@"(?i)(\[[^\]]*\])");
		private static Regex BBCODE_CLOSING_MATCHER = new Regex(@"(?i)(\[/[^\]]*\])");

		private HtmlLabel m_htmHtmlLabel = null;
		private AutosizeLabel m_aslRtfLabel = null;
		private bool m_booAllowSelection = false;
		private bool m_booDetectUrls = true;
		private string m_strText = null;

		#region Properties

		/// <summary>
		/// Gets the current mode of the label.
		/// </summary>
		/// <remarks>
		/// The mode indicates what type of formatting is being used.
		/// </remarks>
		/// <value>The current mode of the label.</value>
		protected Mode FormattingMode { get; private set; }

		/// <summary>
		/// Gets or sets the label's text.
		/// </summary>
		/// <value>The label's text.</value>
		[Browsable(true)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public override string Text
		{
			get
			{
				return m_strText;
			}
			set
			{
				m_strText = value;
				if (!String.IsNullOrEmpty(value))
					DetectFormattingMode(m_strText);
				switch (FormattingMode)
				{
					case Mode.HTML:
                        			if (m_htmHtmlLabel != null)
                            				m_htmHtmlLabel.Text = m_strText;
						break;
					case Mode.BBCode:
                        			if(m_htmHtmlLabel != null)
						    m_htmHtmlLabel.Text = TranslateBBCodeToHtml(m_strText);
						break;
					default:
						m_aslRtfLabel.Text = m_strText;
						break;
				}
			}
		}

		/// <summary>
		/// Gets or sets whether text selection is allowed.
		/// </summary>
		/// <value>Whether text selection is allowed.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(false)]
		public bool AllowSelection
		{
			get
			{
				return m_booAllowSelection;
			}
			set
			{
				m_booAllowSelection = value;
				if (m_aslRtfLabel != null)
					m_aslRtfLabel.AllowSelection = value;
				if (m_htmHtmlLabel != null)
					m_htmHtmlLabel.AllowSelection = value;
			}
		}

		/// <summary>
		/// Gets or sets the <see cref="Font"/> of the label's text.
		/// </summary>
		/// <remarks>
		/// This value may be overridden by formatting passed to <see cref="Text"/>.
		/// </remarks>
		/// <value>The <see cref="Font"/> of the label's text.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public override Font Font
		{
			get
			{
				return base.Font;
			}
			set
			{
				base.Font = value;
				if (m_aslRtfLabel != null)
					m_aslRtfLabel.Font = value;
				if (m_htmHtmlLabel != null)
					m_htmHtmlLabel.Font = value;
			}
		}


		/// <summary>
		/// Gets or sets the background colour of the label.
		/// </summary>
		/// <remarks>
		/// This value may be overridden by formatting passed to <see cref="Text"/>.
		/// </remarks>
		/// <value>The background colour of the label.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public override Color BackColor
		{
			get
			{
				return base.BackColor;
			}
			set
			{
				base.BackColor = value;
				if (m_aslRtfLabel != null)
					m_aslRtfLabel.BackColor = value;
				if (m_htmHtmlLabel != null)
					m_htmHtmlLabel.BackColor = value;
			}
		}

		/// <summary>
		/// Gets or sets the text colour of the label.
		/// </summary>
		/// <remarks>
		/// This value may be overridden by formatting passed to <see cref="Text"/>.
		/// </remarks>
		/// <value>The text colour of the label.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public override Color ForeColor
		{
			get
			{
				return base.ForeColor;
			}
			set
			{
				base.ForeColor = value;
				if (m_aslRtfLabel != null)
					m_aslRtfLabel.ForeColor = value;
				if (m_htmHtmlLabel != null)
					m_htmHtmlLabel.ForeColor = value;
			}
		}

		/// <summary>
		/// Gets or sets the dock style of the control.
		/// </summary>
		/// <value>The dock style of the control.</value>
		[DefaultValue(typeof(DockStyle), "None")]
		public new DockStyle Dock
		{
			get
			{
				return base.Dock;
			}
			set
			{
				base.Dock = value;
			}
		}

		#region Feature Disabling

		/// <summary>
		/// Gets whether the control has a tab stop.
		/// </summary>
		/// <value>Always <c>false</c>.</value>
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new bool TabStop
		{
			get
			{
				return false;
			}
		}

		#endregion

		/// <summary>
		/// Gets or sets whether or not URLs in the label text should be
		/// turned into active links.
		/// </summary>
		/// <value>Whether or not URLs in the label text should be
		/// turned into active links.</value>
		[Browsable(true)]
		[DefaultValue(true)]
		[Category("Behavior")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public bool DetectUrls
		{
			get
			{
				return m_booDetectUrls;
			}
			set
			{
				m_booDetectUrls = value;
				if (m_aslRtfLabel != null)
					m_aslRtfLabel.DetectUrls = value;
				if (m_htmHtmlLabel != null)
					m_htmHtmlLabel.DetectUrls = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public FormattedLabel()
		{
			FormattingMode = Mode.Plain;
			InitializeRtfLabel();
			SwitchMode();
		}

		#endregion

		/// <summary>
		/// Initializes the label to use to display HTML.
		/// </summary>
		private void InitializeHtmlLabel()
		{
			if (m_htmHtmlLabel != null)
				return;
			m_htmHtmlLabel = new HtmlLabel();
			m_htmHtmlLabel.Dock = DockStyle.Fill;
			m_htmHtmlLabel.AllowSelection = AllowSelection;
			m_htmHtmlLabel.Font = Font;
			m_htmHtmlLabel.BackColor = BackColor;
			m_htmHtmlLabel.ForeColor = ForeColor;
			m_htmHtmlLabel.DetectUrls = DetectUrls;
			Controls.Add(m_htmHtmlLabel);
		}

		/// <summary>
		/// Initializes the label to use to display RTF and plain text.
		/// </summary>
		private void InitializeRtfLabel()
		{
			if (m_aslRtfLabel != null)
				return;
			m_aslRtfLabel = new AutosizeLabel();
			m_aslRtfLabel.Dock = DockStyle.Fill;
			m_aslRtfLabel.AllowSelection = AllowSelection;
			m_aslRtfLabel.Font = Font;
			m_aslRtfLabel.BackColor = BackColor;
			m_aslRtfLabel.ForeColor = ForeColor;
			m_aslRtfLabel.DetectUrls = DetectUrls;
			m_aslRtfLabel.ScrollBars = RichTextBoxScrollBars.Vertical;
			Controls.Add(m_aslRtfLabel);
		}

		private void DetectFormattingMode(string p_strText)
		{
			Mode mdeOldMode = FormattingMode;
			//check for closing tags is more accurate than tags in general
			// often <...> and [...] are used as literals
            // <br> is also a good indicator
            Int32 intHtmlMatchCount = HTML_DETECTION_MATCHER.Matches(p_strText).Count;
			Int32 intBBCodeMatchCount = BBCODE_CLOSING_MATCHER.Matches(p_strText).Count;
			if ((intHtmlMatchCount > 0) && (intHtmlMatchCount > intBBCodeMatchCount))
				FormattingMode = Mode.HTML;
			else if (intBBCodeMatchCount > 0)
				FormattingMode = Mode.BBCode;
			else if (p_strText.StartsWith(@"{\rtf"))
				FormattingMode = Mode.RTF;
			else
				FormattingMode = Mode.Plain;
			if (mdeOldMode != FormattingMode)
				SwitchMode();
		}

		/// <summary>
		/// Switches display modes.
		/// </summary>
		private void SwitchMode()
		{
			switch (FormattingMode)
			{
				case Mode.HTML:
				case Mode.BBCode:
					InitializeHtmlLabel();
					m_htmHtmlLabel.BringToFront();
					break;
				default:
					InitializeRtfLabel();
					m_aslRtfLabel.BringToFront();
					break;
			}
		}

		/// <summary>
		/// Translates BBCode to HTML.
		/// </summary>
		/// <param name="p_strText">The code to translate.</param>
		/// <returns>The HTML representation of the given BBCode.</returns>
		private string TranslateBBCodeToHtml(string p_strText)
		{
			if (String.IsNullOrEmpty(p_strText))
				return p_strText;
			string strHtml = HttpUtility.HtmlEncode(p_strText);
			strHtml = strHtml.Replace(@"\", "");
			strHtml = strHtml.Replace("\r\n", "<br/>");
			//this line remove closing item tags, used in [list]s
			strHtml = strHtml.Replace("[/*]", "");

			Dictionary<string, string> dicReplacements = new Dictionary<string, string> {
				{@"\[b\](.*?)\[\/b\]",@"<strong>$1</strong>"}, // bold
				{@"\[i\](.*?)\[\/i\]", @"<em>$1</em>"},   // italic
				{@"\[u\](.*?)\[\/u\]", @"<u>$1</u>"},   // underline
				{@"\[s\](.*?)\[\/s\]", @"<s>$1</s>"},   // strikethrough
				{@"\[url\=(.*?)\](.*?)\[\/url\]",@"<a href=$1>$2</a>"},    // link
				{@"\[img](.*?)\[\/img\]",@"<img src=""$1"" style=""margin: 5px;"" />"},    // image, no alignment
				{@"\[img\=(.*?)\](.*?)\[\/img\]",@"<img src=""$2"" align=""$1"" style=""margin: 5px;"" />"},    // image aligned -- LEGACY
				{@"\[aimg\=(.*?)\](.*?)\[\/aimg\]",@"<img src=""$2"" align=""$1"" style=""margin: 5px;"" />"},    // image aligned
				{@"\[img align\=(.*?)\](.*?)\[\/img\]",@"<img src=""$2"" align=""$1"" style=""margin: 5px;"" />"},    // image aligned -- LEGACY
				{@"\[quote\](.*?)\[\/quote\]",@"<div class=""quote""><div class=""quote_text"">QUOTE</div>$1</div>"},    // quote
				{@"\[quote(.*?)\](.*?)\[\/quote\]",@"<div class=""quote""><div class=""quote_text"">QUOTE</div>$2</div>"},    // quote (from forums)
				{@"\[code\](.*?)\[\/code\]",@"<code>$1</code>"}, // code
				{@"\[heading\](.*?)\[\/heading\]",@"<h4>$1</h4>"},    //heading
				{@"\[ol\](.*?)\[\/ol\]",@"<ol class=""content_list"">$1</ol>"},    // ordered list -- LEGACY, should not be used any more
				{@"\[list\=1\](.+?)\[\/list\]",@"<ol class=""content_list"">$1</ol>"},    // ordered list
				{@"\[ul\](.*?)\[\/ul\]",@"<ul class=""disc"">$1</ul>"},    // unordered list -- LEGACY, should not be used any more
				{@"\[list\](.+?)\[\/list\]",@"<ul class=""disc"">$1</ul>"},    // unordered list
				{@"\[\*\](.*?)\<br \/\>",@"<li>$1</li>"},    // list item
				{@"\[line\]",@"<div class=""line""></div>"},    // horizontal rule
				{@"\[color\=(.*?)\](.*?)\[\/color\]",@"<font color=$1>$2</font>"}, // color
				{@"\[font\=(.*?)\](.*?)\[\/font\]",@"<font face=$1>$2</font>"}, // font face
				{@"\[center\](.*?)\[\/center\]",@"<div align=""center"">$1</div>"}, // centre align
				{@"\[right\](.*?)\[\/right\]",@"<div align=""right"">$1</div>"}, // right align
				{@"\[youtube\](\(?)(.*\.+youtube+\..*)\[\/youtube\]",@"<a href=""$2"">$2</a>"},  // youtube
				{@"\[youtube\](.*?)\[\/youtube\]",@"<a href=""https://www.youtube.com/watch?v=$1"">https://www.youtube.com/watch?v=$1</a>"},  // youtube
				{@"\[size=(.*?)\](.*?)\[\/size\]",@"<font size=""$1"">$2</font>"}, //size
				{@"\[spoiler\](.*?)\[\/spoiler\]",@"<strong>SPOILER:</strong> <span style=""color: #404040; border-bottom: 1px solid white;"">$1</span>"} //spoiler
			};

			foreach (KeyValuePair<string, string> kvpReplacement in dicReplacements)
			{
				Regex rgxCode = new Regex(kvpReplacement.Key, RegexOptions.IgnoreCase | RegexOptions.Singleline);
				strHtml = rgxCode.Replace(strHtml, kvpReplacement.Value);
			}

			string strStyles = @"<style>
									body {
										background-color: rgb(64, 64, 64); font-family: ""Trebuchet MS"", Helvetica, sans-serif;
									}
									.quote {
										margin: 30px; padding: 10px; font-size: 13px; border-top-color: rgb(122, 122, 122); border-bottom-color: rgb(122, 122, 122); border-top-width: 1px; border-bottom-width: 1px; border-top-style: solid; border-bottom-style: solid;
									}
									ul.disc li {
										margin-left: 20px;
									}
									div.line {
										width: 100%; height: 1px; clear: both; margin-bottom: 10px;
									}
									div.line hr {
										display: none;
									}
									.bb-content {
										color: rgb(255, 255, 255); overflow: hidden; padding-bottom: 3px; font-size: 13px;
									}
									.bb-content ul {
										font-size: 13px;
									}
									.bb-content ol {
										font-size: 13px;
									}
									.bb-content h2 {
										font-size: 16px; margin-top: 20px;
									}
									</style>";

			return strStyles + "<div class=\"bb-content\">" + strHtml + "</div>";
		}
	}
}
