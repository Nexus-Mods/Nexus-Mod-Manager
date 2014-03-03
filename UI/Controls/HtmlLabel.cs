using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A label that allows use of HTML to format the text.
	/// </summary>
	public class HtmlLabel : WebBrowser
	{
		private static Regex HREF_MATCHER = new Regex(@"(?i)\b(href=[""'][^""']+[""'])");
		private static Regex TAG_MATCHER = new Regex(@"(?i)(<[^>]*>)");
		private static Regex LOOSE_URL_MATCHER = new Regex(@"(?i)\b((?:[a-z][\w-]+:(?:/{1,3}|[a-z0-9%])|www\d{0,3}[.]|[a-z0-9.\-]+[.][a-z]{2,4}/)(?:[^\s()<>]+|\(([^\s()<>]+|(\([^\s()<>]+\)))*\))+(?:\(([^\s()<>]+|(\([^\s()<>]+\)))*\)|[^\s`!()\[\]{};:'"".,<>?«»“”‘’]))");
		private static Regex STRICT_URL_MATCHER = new Regex(@"(?i)\b((?:https?://|www\d{0,3}[.]|[a-z0-9.\-]+[.][a-z]{2,4}/)(?:[^\s()<>]+|\(([^\s()<>]+|(\([^\s()<>]+\)))*\))+(?:\(([^\s()<>]+|(\([^\s()<>]+\)))*\)|[^\s`!()\[\]{};:'"".,<>?«»“”‘’]))");
		private string m_strText = null;

		#region Properties

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
				string strText = m_strText;
				if (!string.IsNullOrEmpty(strText))
				{
					MatchCollection colUrls = TAG_MATCHER.Matches(strText);
					if (colUrls.Count == 0)
					{
						//this appears to be plain text - so convert to html
						strText = strText.Replace("\r\n", "<br/>").Replace("\r", "<br/>").Replace("\n", "<br/>").Replace("\t", "    ");
					}
					if (DetectUrls)
					{
						Dictionary<string, string> dicProtectedUrls = new Dictionary<string, string>();
						for (Int32 i = colUrls.Count - 1; i >= 0; i--)
						{
							string strShieldText = "<SHIELD" + i + ">";
							strText = strText.Replace(colUrls[i].Value, strShieldText);
							dicProtectedUrls[strShieldText] = colUrls[i].Value;
						}
						colUrls = LOOSE_URL_MATCHER.Matches(strText);
						for (Int32 i = colUrls.Count - 1; i >= 0; i--)
						{
							strText = strText.Remove(colUrls[i].Index, colUrls[i].Length);
							strText = strText.Insert(colUrls[i].Index, String.Format("<a href=\"{0}\">{0}</a>", colUrls[i].Value));
						}
						foreach (string strKey in dicProtectedUrls.Keys)
							strText = strText.Replace(strKey, dicProtectedUrls[strKey]);
					}
				}
				string strStyle = String.Format(@"<style>
													body {{{{
														padding:3;
														margin:0;
														background-color:#{0:x6};
														font-family:{1};
														font-size:{2}pt;
														color:#{3:x6};
													}}}}
												</style>", BackColor.ToArgb() & 0x00ffffff, Font.FontFamily.Name, Font.SizeInPoints, ForeColor.ToArgb() & 0x00ffffff);
				string strHtml = String.Format("<html><head></head><body style=\"{0}\">{1}{2}{{0}}</body></html>", AllowSelection ? "" : "cursor:default;", strStyle, AllowSelection ? "" : "<script>document.onselectstart=new Function (\"return false\")</script>");

				Navigate("about:blank");
				Document.OpenNew(false);
				Document.Write(String.Format(strHtml, strText));
				Refresh();
				SetScrollbarVisibility();
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
		public bool AllowSelection { get; set; }

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
		/// Gets whether navigation is enabled.
		/// </summary>
		/// <remarks>
		/// Even though this returns <c>true</c>, navigation by clicking links has been disabled.
		/// </remarks>
		/// <value>Always <c>true</c>.</value>
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new bool AllowNavigation
		{
			get
			{
				return true;
			}
		}

		/// <summary>
		/// Gets whether navigation by dropping is enabled.
		/// </summary>
		/// <value>Always <c>false</c>.</value>
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new bool AllowWebBrowserDrop
		{
			get
			{
				return false;
			}
		}

		/// <summary>
		/// Gets whether the context menu is enabled.
		/// </summary>
		/// <value>Always <c>false</c>.</value>
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new bool IsWebBrowserContextMenuEnabled
		{
			get
			{
				return false;
			}
		}

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

		/// <summary>
		/// Gets whether browsing keyboard shorcuts are enabled.
		/// </summary>
		/// <value>Always <c>false</c>.</value>
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new bool WebBrowserShortcutsEnabled
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
		public bool DetectUrls { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public HtmlLabel()
		{
			base.IsWebBrowserContextMenuEnabled = false;
			base.TabStop = false;
			base.WebBrowserShortcutsEnabled = false;
			base.AllowNavigation = true;
			base.AllowWebBrowserDrop = false;
			AllowSelection = false;
			Dock = DockStyle.None;
			BackColor = SystemColors.Control;
			Font = new Font(FontFamily.GenericSansSerif, 8.25f);
			ForeColor = SystemColors.ControlText;
			Size = new Size(35, 20);
			ScrollBarsEnabled = false;
			DetectUrls = true;
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="WebBrowser.Navigating"/> event.
		/// </summary>
		/// <remarks>
		/// This cancels all navigation events that aren't caused by setting the <see cref="Text"/>
		/// property. If the navigation is caused by something other than setting the <see cref="Text"/>
		/// property, the new location is open in the default browser.
		/// </remarks>
		/// <param name="e">A <see cref="WebBrowserNavigatingEventArgs"/> describing the event arguments.</param>
		protected override void OnNavigating(WebBrowserNavigatingEventArgs e)
		{
			if (!"blank".Equals(e.Url.LocalPath, StringComparison.OrdinalIgnoreCase))
			{
				e.Cancel = true;
				Uri uriUrl = e.Url;
				if ("about".Equals(uriUrl.Scheme, StringComparison.OrdinalIgnoreCase))
					uriUrl = new Uri(Uri.UriSchemeHttp + Uri.SchemeDelimiter + uriUrl.LocalPath);
				System.Diagnostics.Process.Start(uriUrl.ToString());
			}
			base.OnNavigating(e);
		}

		/// <summary>
		/// Raises the <see cref="Control.SizeChanged"/> event.
		/// </summary>
		/// <remarks>
		/// This enables or disabled the scroll bars as required.
		/// </remarks>
		/// <param name="e">A <see cref="WebBrowserDocumentCompletedEventArgs"/> describing the event arguments.</param>
		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			SetScrollbarVisibility();
		}

		/// <summary>
		/// This enables or disabled the scroll bars as required.
		/// </summary>
		protected void SetScrollbarVisibility()
		{
			if ((Document == null) || (Document.Body == null))
				return;
			if ((Document.Body.ScrollRectangle.Height > ClientSize.Height) || (Document.Body.ScrollRectangle.Width > ClientSize.Width + 6))
				ScrollBarsEnabled = true;
			else
				ScrollBarsEnabled = false;
		}
	}
}
