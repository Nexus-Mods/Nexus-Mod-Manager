using System;
using System.Drawing;
using System.Text;

namespace Nexus.Client.ModRepositories
{
	/// <summary>
	/// A repository's fileserver info.
	/// </summary>
	public class FileServerZone
	{
		private string m_strFileServerName = String.Empty;
		private string m_strFileServerID = String.Empty;
		private Image m_imgFileServerFlag = null;
		private Int32 m_intAffinityID = 0;

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

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor the initializes the object.
		/// </summary>
		public FileServerZone()
		{
			m_strFileServerID = "default";
			m_strFileServerName = "Default";
			m_intAffinityID = 0;
			m_imgFileServerFlag = new Bitmap(16, 11);
		}

		/// <summary>
		/// A simple constructor the initializes the object with the given parameters.
		/// </summary>
		/// <param name="p_strFileServerID">The fileserver ID.</param>
		/// <param name="p_strFileServerName">The fileserver name.</param>
		/// <param name="p_intAffinityID">The fileserver affinity.</param>
		public FileServerZone(string p_strFileServerID, string p_strFileServerName, Int32 p_intAffinityID)
		{
			m_strFileServerID = p_strFileServerID;
			m_strFileServerName = p_strFileServerName;
			m_intAffinityID = p_intAffinityID;
			m_imgFileServerFlag = new Bitmap(16, 11);
		}

		/// <summary>
		/// A simple constructor the initializes the object with the given parameters.
		/// </summary>
		/// <param name="p_strFileServerID">The fileserver ID.</param>
		/// <param name="p_strFileServerName">The fileserver name.</param>
		/// <param name="p_intAffinityID">The fileserver affinity.</param>
		/// <param name="p_imgFileServerFlag">The fileserver image.</param>
		public FileServerZone(string p_strFileServerID, string p_strFileServerName, Int32 p_intAffinityID, Image p_imgFileServerFlag)
		{
			m_strFileServerID = p_strFileServerID;
			m_strFileServerName = p_strFileServerName;
			m_intAffinityID = p_intAffinityID;
			m_imgFileServerFlag = p_imgFileServerFlag;
		}

		#endregion
	}
}
