namespace Nexus.Client.ModRepositories
{
    using System;
    using System.Drawing;

    /// <summary>
    /// A repository's fileserver info.
    /// </summary>
    public class FileServerZone
	{
		private string m_strFileServerName = String.Empty;
		private string m_strFileServerID = String.Empty;
		private Image m_imgFileServerFlag = null;
		private Int32 m_intAffinityID = 0;
		private bool m_booPremium = false;

		#region Properties

		/// <summary>
		/// Gets the name of the fileserver.
		/// </summary>
		/// <value>The name of the fileserver.</value>
		public string FileServerName
		{
			get
			{
				return m_strFileServerName;
			}
			private set
			{
				m_strFileServerName = value;
			}
		}

		/// <summary>
		/// Gets the ID of the fileserver.
		/// </summary>
		/// <value>The ID of the fileserver.</value>
		public string FileServerID
		{
			get
			{
				return m_strFileServerID;
			}
			private set
			{
				m_strFileServerID = value;
			}
		}

		/// <summary>
		/// Gets the image of the fileserver.
		/// </summary>
		/// <value>The image of the fileserver.</value>
		public Image FileServerFlag
		{
			get
			{
				return m_imgFileServerFlag;
			}
			private set
			{
				m_imgFileServerFlag = value;
			}
		}

		/// <summary>
		/// Gets the affinity of the fileserver, affinity is used to choose the nearest alternative.
		/// </summary>
		/// <value>The affinity of the fileserver, affinity is used to choose the nearest alternative.</value>
		public Int32 FileServerAffinity
		{
			get
			{
				return m_intAffinityID;
			}
			private set
			{
				m_intAffinityID = value;
			}
		}

		/// <summary>
		/// Gets the premium status of the fileserver.
		/// </summary>
		/// <value>The premium status of the fileserver.</value>
		public bool IsPremium
		{
			get
			{
				return m_booPremium;
			}
			private set
			{
				m_booPremium = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor the initializes the object.
		/// </summary>
		public FileServerZone()
		{
			m_strFileServerID = "default";
			m_strFileServerName = "Default (CDN)";
			m_intAffinityID = 0;
			m_imgFileServerFlag = new Bitmap(16, 11);
			m_booPremium = false;
		}

		/// <summary>
		/// A simple constructor the initializes the object with the given parameters.
		/// </summary>
		/// <param name="p_strFileServerID">The fileserver ID.</param>
		/// <param name="p_strFileServerName">The fileserver name.</param>
		/// <param name="p_intAffinityID">The fileserver affinity.</param>
		/// <param name="p_booPremium">The fileserver premium status.</param>
		public FileServerZone(string p_strFileServerID, string p_strFileServerName, Int32 p_intAffinityID, bool p_booPremium)
		{
			m_strFileServerID = p_strFileServerID;
			m_strFileServerName = p_strFileServerName;
			m_intAffinityID = p_intAffinityID;
			m_imgFileServerFlag = new Bitmap(16, 11);
			m_booPremium = p_booPremium;
		}

		/// <summary>
		/// A simple constructor the initializes the object with the given parameters.
		/// </summary>
		/// <param name="p_strFileServerID">The fileserver ID.</param>
		/// <param name="p_strFileServerName">The fileserver name.</param>
		/// <param name="p_intAffinityID">The fileserver affinity.</param>
		/// <param name="p_imgFileServerFlag">The fileserver image.</param>
		/// <param name="p_booPremium">The fileserver premium status.</param>
		public FileServerZone(string p_strFileServerID, string p_strFileServerName, Int32 p_intAffinityID, Image p_imgFileServerFlag, bool p_booPremium)
		{
			m_strFileServerID = p_strFileServerID;
			m_strFileServerName = p_strFileServerName;
			m_intAffinityID = p_intAffinityID;
			m_imgFileServerFlag = p_imgFileServerFlag;
			m_booPremium = p_booPremium;
		}

		#endregion
	}
}
