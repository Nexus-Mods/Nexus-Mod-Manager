﻿﻿//#define ENABLE_BROWSER_CONTEXT_MENU // when defined the brower's context menu will be active (mostly used for debugging: view source, etc)
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using Nexus.Client;

namespace Nexus.UI.Controls
{
    /// <summary>
    /// A label that allows use of HTML to format the text.
    /// </summary>
    public class HtmlLabel : UserControl //: WebBrowser
    {
        const string NAVIGATION_PAGE = "about:blank";
        //private static Regex HREF_MATCHER = new Regex(@"(?i)\b(href=[""'][^""']+[""'])");
        private static Regex TAG_MATCHER = new Regex(@"(?i)(<[^>]*>)");
        private static Regex LOOSE_URL_MATCHER = new Regex(@"(?i)\b((?:[a-z][\w-]+:(?:/{1,3}|[a-z0-9%])|www\d{0,3}[.]|[a-z0-9.\-]+[.][a-z]{2,4}/)(?:[^\s()<>]+|\(([^\s()<>]+|(\([^\s()<>]+\)))*\))+(?:\(([^\s()<>]+|(\([^\s()<>]+\)))*\)|[^\s`!()\[\]{};:'"".,<>?«»“”‘’]))");
        //private static Regex STRICT_URL_MATCHER = new Regex(@"(?i)\b((?:https?://|www\d{0,3}[.]|[a-z0-9.\-]+[.][a-z]{2,4}/)(?:[^\s()<>]+|\(([^\s()<>]+|(\([^\s()<>]+\)))*\))+(?:\(([^\s()<>]+|(\([^\s()<>]+\)))*\)|[^\s`!()\[\]{};:'"".,<>?«»“”‘’]))");
        private string m_strText = null;
        private WebBrowser m_browser = null;
        #region Properties

        /// <summary>
        /// Gets or sets the label's text.
        /// </summary>
        /// <value>The label's text.</value>
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public new string Text {
            get {
                return m_strText;
            }
            set {
                m_strText = value;
                if (!string.IsNullOrEmpty (m_strText)) {
                    m_browser.Navigate (NAVIGATION_PAGE);
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
        public override Font Font {
            get {
                return m_browser.Font;
            }
            set {
                m_browser.Font = value;
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
        public override Color BackColor {
            get {
                return m_browser.BackColor;
            }
            set {
                m_browser.BackColor = value;
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
        public override Color ForeColor {
            get {
                return m_browser.ForeColor;
            }
            set {
                m_browser.ForeColor = value;
            }
        }

        /// <summary>
        /// Gets or sets the dock style of the control.
        /// </summary>
        /// <value>The dock style of the control.</value>
        [Browsable(true)]
        [DefaultValue(typeof(DockStyle), "None")]
        public new DockStyle Dock {
            get {
                return m_browser.Dock;
            }
            set {
                m_browser.Dock = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="Nexus.UI.Controls.HtmlLabel"/> scroll bars enabled.
        /// </summary>
        /// <value><c>true</c> if scroll bars enabled; otherwise, <c>false</c>.</value>
        [Browsable(true)]
        [DefaultValue (true)]
        public bool ScrollBarsEnabled {
            get {
                return m_browser.ScrollBarsEnabled;
            }
            set {
                m_browser.ScrollBarsEnabled = value;
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
        public bool AllowNavigation
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
        public bool AllowWebBrowserDrop
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
        public bool IsWebBrowserContextMenuEnabled
        {
            get
            {
                #if ENABLE_BROWSER_CONTEXT_MENU
                return true;
                #else
                return false;
                #endif
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
        public bool WebBrowserShortcutsEnabled
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
            base.Dock = DockStyle.Fill;
            InitBrowswer();
            this.Controls.Add(m_browser);
        }

        private void InitBrowswer ()
        {
            m_browser = new WebBrowser();
            #if ENABLE_BROWSER_CONTEXT_MENU
            #warning ENABLE_BROWSER_CONTEXT_MENU is defined (selected features from the context menu will cause crashes)
            m_browser.IsWebBrowserContextMenuEnabled = true;
            #else
            m_browser.IsWebBrowserContextMenuEnabled = false;
            #endif
            m_browser.TabStop = false;
            m_browser.WebBrowserShortcutsEnabled = false;
            m_browser.AllowNavigation = true;
            m_browser.AllowWebBrowserDrop = false;
            AllowSelection = false;
            Dock = DockStyle.None;
            BackColor = SystemColors.Control;
            Font = new Font(FontFamily.GenericSansSerif, 8.25f);
            ForeColor = SystemColors.ControlText;
            m_browser.Size = new Size(250, 250);
            m_browser.ScrollBarsEnabled = false;
            DetectUrls = true;
            m_browser.ScriptErrorsSuppressed = false;
            m_browser.Visible = true;
            m_browser.Navigating +=  browser_Navigating;
            m_browser.DocumentCompleted += browser_DocumentCompleted;
        }
        void browser_Navigating (object sender, WebBrowserNavigatingEventArgs e)
        {
            if (e.Url.ToString().ToLower() != NAVIGATION_PAGE.ToLower()) {
                e.Cancel = true;
                System.Diagnostics.Process.Start(e.Url.ToString());
            }
        }
        void browser_DocumentCompleted (object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            UpdateDocument();
        }

        void UpdateDocument ()
        {
            string strText = this.Text;
            if (!string.IsNullOrEmpty (strText)) {
                MatchCollection colUrls = TAG_MATCHER.Matches (strText);
                if (colUrls.Count == 0) {
                    //this appears to be plain text - so convert to html
                    strText = strText.Replace ("\r\n", "<br/>").Replace ("\r", "<br/>").Replace ("\n", "<br/>").Replace ("\t", "    ");
                }
                if (DetectUrls) {
                    Dictionary<string, string> dicProtectedUrls = new Dictionary<string, string> ();
                    for (Int32 i = colUrls.Count - 1; i >= 0; i--) {
                        string strShieldText = "<SHIELD" + i + ">";
                        strText = strText.Replace (colUrls [i].Value, strShieldText);
                        dicProtectedUrls [strShieldText] = colUrls [i].Value;
                    }
                    colUrls = LOOSE_URL_MATCHER.Matches (strText);
                    for (Int32 i = colUrls.Count - 1; i >= 0; i--) {
                        strText = strText.Remove (colUrls [i].Index, colUrls [i].Length);
                        strText = strText.Insert (colUrls [i].Index, String.Format ("<a href=\"{0}\">{0}</a>", colUrls [i].Value));
                    }
                    foreach (string strKey in dicProtectedUrls.Keys)
                        strText = strText.Replace (strKey, dicProtectedUrls [strKey]);
                }
                string strStyle = String.Format (@"<style>
                                                    body {{{{
                                                        padding:3;
                                                        margin:0;
                                                        background-color:#{0:x6};
                                                        font-family:{1};
                                                        font-size:{2}pt;
                                                        color:#{3:x6};
                                                    }}}}
                                                </style>", BackColor.ToArgb () & 0x00ffffff, Font.FontFamily.Name, Font.SizeInPoints, ForeColor.ToArgb () & 0x00ffffff);
                string strHtml = String.Format ("<html><head></head><body style=\"{0}\">{1}{2}{{0}}</body></html>", string.Format ("{0};overflow: auto;", AllowSelection ? "" : "cursor:default"), strStyle, AllowSelection ? "" : "<script>document.onselectstart=new Function (\"return false\")</script>");

                if(m_browser.Document != null)
                {
                    var doc = m_browser.Document.OpenNew (false);
                    if(doc != null)
                    {
                        doc.Write (String.Format (strHtml, strText));
                    }
                }
            }
        }
	public bool DocumentBodyIsNull()
	{
		if (m_browser.Document == null || m_browser.Document.Body == null) { return true;  }
		return false;
	}

	public Rectangle GetDocumentBodyScrollRectangle()
	{
		Rectangle rect = new Rectangle();
		if (m_browser.Document != null && m_browser.Document.Body != null)
		{
				rect = m_browser.Document.Body.ScrollRectangle;
		}
		return rect;
	}
        #endregion
    }
}