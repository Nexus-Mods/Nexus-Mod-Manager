using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using Balloon.NET;

namespace Nexus.Client.TipsManagement
{
	public class BalloonManager
	{
		private BalloonHelp m_bmBalloonHelp;
		private List<ITips> m_lstTips = null;
		private List<ITips> m_lstCurrentTips = null;
		private List<ITips> m_lstTutorial = null;
		private int m_intCurrentTip = 0;
		private IEnvironmentInfo m_eiEnvironmentInfo = null;

		public event EventHandler ShowNextClick;
		public event EventHandler ShowPreviousClick;
		public event EventHandler CloseClick;

		#region Properties

		public BalloonHelp balloonHelp
		{
			get
			{
				return m_bmBalloonHelp;
			}
		}

		public ITips CurrentTip
		{
			get
			{
				return (m_lstCurrentTips != null) ? m_lstCurrentTips.Find(x => x.TipId == m_intCurrentTip) : null;
			}
		}

		public string TipSection
		{
			get
			{
				ITips itTip = CurrentTip;
				return (itTip != null) ? itTip.TipSection : String.Empty;
			}
		}

		public string TipObject
		{
			get
			{
				ITips itTip = (m_lstCurrentTips != null) ? m_lstCurrentTips.Find(x => x.TipId == m_intCurrentTip) : null;
				return (itTip != null) ? itTip.TipObject : String.Empty;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public BalloonManager(bool p_booSupportsPlugin)
		{
			m_lstTips = LoadTips(p_booSupportsPlugin);
		}

		#endregion

		#region Tips Navigation

		/// <summary>
		/// The initial tips check.
		/// </summary>
		public void CheckTips(int p_X, int p_Y, bool p_booCheckForTipsOnStartup, string p_strVersion)
		{
			SetTipList(p_strVersion);

			if ((m_lstCurrentTips != null) && (m_lstCurrentTips.Count > 0))
			{
				try
				{
					RecreateBalloon(this, EventArgs.Empty);

					m_bmBalloonHelp.Caption = "";
					m_bmBalloonHelp.Content = "There are new Tips";
					m_bmBalloonHelp.previousButton.Visible = false;
					m_bmBalloonHelp.Icon = SystemIcons.Exclamation;
					m_intCurrentTip = 1;
					if (p_booCheckForTipsOnStartup)
						m_bmBalloonHelp.ShowBalloon(p_X, p_Y);
				}
				catch
				{
				}
			}
		}

		/// <summary>
		/// Manages the next tip to show.
		/// </summary>
		public void ShowNextTip(Point p_pCoords)
		{

			if (m_bmBalloonHelp != null)
			{
				m_bmBalloonHelp.Close();
			}
			if (m_lstCurrentTips.Count == 0)
			{
				try
				{
					RecreateBalloon(this, EventArgs.Empty);

					m_bmBalloonHelp.Caption = "Tips";
					m_bmBalloonHelp.Content = "No Tips for this version";
					m_bmBalloonHelp.previousButton.Visible = false;
					m_bmBalloonHelp.nextButton.Visible = false;
					IntPtr Hicon = Nexus.Client.Properties.Resources.dialog_ok_4.GetHicon();
					Icon newIcon = Icon.FromHandle(Hicon);
					m_bmBalloonHelp.Icon = newIcon;
					m_bmBalloonHelp.ShowBalloon(p_pCoords.X, p_pCoords.Y);
				}
				catch
				{
				}
			}
			else
			{
				try
				{
					RecreateBalloon(this, EventArgs.Empty);

					if (m_intCurrentTip < 2)
						m_bmBalloonHelp.previousButton.Visible = false;

					if (m_intCurrentTip == m_lstCurrentTips.Count)
						m_bmBalloonHelp.nextButton.Visible = false;

					m_bmBalloonHelp.Caption = "Tips   " + m_intCurrentTip + "/" + m_lstCurrentTips.Count;
					m_bmBalloonHelp.Content = CurrentTip.TipText;

					m_bmBalloonHelp.Icon = SystemIcons.Asterisk;
					m_bmBalloonHelp.ShowBalloon(p_pCoords.X, p_pCoords.Y);
					if (++m_intCurrentTip > m_lstCurrentTips.Count)
						m_intCurrentTip = 1;
				}
				catch
				{
				}
			}
		}

		/// <summary>
		/// Manages the next click button.
		/// </summary>
		private void balloonHelp_ClickNext(object sender, EventArgs e)
		{
			ShowNextClick(this, new EventArgs());
		}

		/// <summary>
		/// Manages the previous click button.
		/// </summary>
		private void balloonHelp_ClickPrevious(object sender, EventArgs e)
		{
			SetPreviousTip(false);
			ShowPreviousClick(this, new EventArgs());
		}

		/// <summary>
		/// Manages the close click button.
		/// </summary>
		private void m_bmBalloonHelp_ClickClose(object sender, EventArgs e)
		{
			CloseClick(this, new EventArgs());
		}

		/// <summary>
		/// Set the previous tip
		/// </summary>
		public void SetPreviousTip(bool p_booResizing)
		{
			int intOffset = Convert.ToInt32(!p_booResizing);
			m_intCurrentTip = --m_intCurrentTip <= 0 ? (m_lstCurrentTips.Count - intOffset) : (m_intCurrentTip - intOffset);
		}

		/// <summary>
		/// Create the balloon object
		/// </summary>
		private void RecreateBalloon(object sender, EventArgs e)
		{
			m_bmBalloonHelp = new BalloonHelp();
			m_bmBalloonHelp.Disposed += new EventHandler(this.RecreateBalloon);
			m_bmBalloonHelp.ClickNext += balloonHelp_ClickNext;
			m_bmBalloonHelp.ClickPrevious += balloonHelp_ClickPrevious;
			m_bmBalloonHelp.ClickClose += m_bmBalloonHelp_ClickClose;
		}

		#endregion

		/// <summary>
		/// Loads the data from the Tips XML file.
		/// </summary>
		public List<ITips> LoadTips(bool p_booSupportsPlugin)
		{
			XDocument p_docTips = XDocument.Parse(GetResourceTextFile("Tips.xml"));
			List<ITips> m_tslTips = new List<ITips>();
			XElement xelTipsList = p_docTips.Descendants("tipsList").FirstOrDefault();
			if (xelTipsList != null)
			{
				foreach (XElement xelTip in xelTipsList.Elements("tips"))
				{
					bool booPluginOnly = false;
					string strTipObject = xelTip.Attribute("object").Value;
					string strTipSection = xelTip.Attribute("section").Value;
					string strTipText = xelTip.Element("text").Value;
					string strVersion = xelTip.Attribute("version").Value;
					try
					{
						booPluginOnly = Convert.ToBoolean(xelTip.Attribute("pluginsonly").Value);
					}
					catch { }

					if (booPluginOnly && !p_booSupportsPlugin)
						continue;

					m_tslTips.Add(new Tips(Convert.ToInt32(xelTip.Attribute("ID").Value), strTipText, strTipSection, strTipObject, strVersion));
				}
			}

			return m_tslTips;
		}

		/// <summary>
		/// Open the tips.xml file in a stream returning a string.
		/// </summary>
		/// <param name="filename">The name of the file.</param>
		public string GetResourceTextFile(string filename)
		{
			string result = string.Empty;

			using (Stream stream = this.GetType().Assembly.
					 GetManifestResourceStream("Nexus.Client.Resources." + filename))
			{
				using (StreamReader sr = new StreamReader(stream))
				{
					result = sr.ReadToEnd();
				}
			}
			return result;
		}

		/// <summary>
		/// Gets the list (distinct) of all versions.
		/// </summary>
		public IEnumerable<string> GetVersionList()
		{
			IEnumerable<string> ieList = m_lstTips.Select(x => x.TipVersion).Distinct();
			return ieList;
		}

		/// <summary>
		/// Updates the Current Tips list.
		/// </summary>
		public void SetTipList(string p_strVersion)
		{
			List<ITips> listTips = new List<ITips>();
			listTips = m_lstTips.Where(x => x.TipVersion == p_strVersion).ToList() ?? null;

			if ((listTips != null) && (listTips.Count > 0))
				m_lstCurrentTips = listTips;

			m_intCurrentTip = 1;
		}
	}
}
