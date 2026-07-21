using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// Stores the position of windows.
	/// </summary>
	public class WindowPositions : IXmlSerializable
	{
		/// <summary>
		/// The location info for a window.
		/// </summary>
		public class LocationInfo : IXmlSerializable
		{
			#region Properties

			/// <summary>
			/// Gets or set where the window is located.
			/// </summary>
			/// <value>Where the window is located.</value>
			public Point Location { get; protected set; }

			/// <summary>
			/// Gets or sets the window size.
			/// </summary>
			/// <value>The window size.</value>
			public Size Size { get; protected set; }

			/// <summary>
			/// Gets or sets whether the window is maximized.
			/// </summary>
			/// <value>Whether the window is maximized.</value>
			public bool IsMaximized { get; protected set; }

			#endregion

			#region Constructors

			/// <summary>
			/// The default constructor.
			/// </summary>
			public LocationInfo()
			{
			}

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_pntLocation">Where the window is located.</param>
			/// <param name="p_szeSize">The window size.</param>
			/// <param name="p_booIsMaximized">Whether the window is maximized.</param>
			public LocationInfo(Point p_pntLocation, Size p_szeSize, bool p_booIsMaximized)
			{
				Location = p_pntLocation;
				Size = p_szeSize;
				IsMaximized = p_booIsMaximized;
			}

			#endregion

			#region IXmlSerializable Members

			/// <summary>
			/// This method is reserved, and returns <lang dref="null"/> as required.
			/// </summary>
			/// <returns><lang dref="null"/>, as required.</returns>
			public XmlSchema GetSchema()
			{
				return null;
			}

			/// <summary>
			/// Deserializes the object from XML.
			/// </summary>
			/// <param name="reader">The xml reader from which to deserialize the object.</param>
			public void ReadXml(XmlReader reader)
			{
				bool booIsEmpty = reader.IsEmptyElement;
				reader.ReadStartElement();
				if (booIsEmpty)
					return;

				reader.ReadStartElement("location");
				XmlSerializer xsrSerializer = new XmlSerializer(Location.GetType());
				Location = (Point)xsrSerializer.Deserialize(reader);
				reader.ReadEndElement();

				reader.ReadStartElement("size");
				xsrSerializer = new XmlSerializer(Size.GetType());
				Size = (Size)xsrSerializer.Deserialize(reader);
				reader.ReadEndElement();

				reader.ReadStartElement("isMaximized");
				xsrSerializer = new XmlSerializer(IsMaximized.GetType());
				IsMaximized = (bool)xsrSerializer.Deserialize(reader);
				reader.ReadEndElement();

				reader.ReadEndElement();
			}

			/// <summary>
			/// Serializes the object to XML.
			/// </summary>
			/// <param name="writer">The xml writer to which to serialize the object.</param>
			public void WriteXml(XmlWriter writer)
			{
				writer.WriteStartElement("location");
				XmlSerializer xsrSerializer = new XmlSerializer(Location.GetType());
				xsrSerializer.Serialize(writer, Location);
				writer.WriteEndElement();

				writer.WriteStartElement("size");
				xsrSerializer = new XmlSerializer(Size.GetType());
				xsrSerializer.Serialize(writer, Size);
				writer.WriteEndElement();

				writer.WriteStartElement("isMaximized");
				xsrSerializer = new XmlSerializer(IsMaximized.GetType());
				xsrSerializer.Serialize(writer, IsMaximized);
				writer.WriteEndElement();
			}

			#endregion
		}

		private Dictionary<string, LocationInfo> m_dicPositions = new Dictionary<string, LocationInfo>();

		/// <summary>
		/// Sets the given window's position based on the stored values.
		/// </summary>
		/// <param name="p_strWindowName">The name of the window settings to use to position the given window.</param>
		/// <param name="p_frmWindow">The window to position.</param>
		public void GetWindowPosition(string p_strWindowName, Form p_frmWindow)
		{
			if (!m_dicPositions.ContainsKey(p_strWindowName))
				return;
			LocationInfo lifPosition = m_dicPositions[p_strWindowName];
			if (lifPosition.IsMaximized)
				p_frmWindow.WindowState = FormWindowState.Maximized;
			else
			{
				Screen[] scrScreens = Screen.AllScreens;
				foreach (Screen scrScreen in scrScreens)
					if (scrScreen.WorkingArea.Contains(lifPosition.Location))
					{
						p_frmWindow.Location = lifPosition.Location;
						p_frmWindow.StartPosition = FormStartPosition.Manual;
						break;
					}
				p_frmWindow.ClientSize = lifPosition.Size;
			}
		}

		/// <summary>
		/// Stores the given window's position using the given name.
		/// </summary>
		/// <param name="p_strWindowName">The name under which to store the window settings.</param>
		/// <param name="p_frmWindow">The window whose settings are to be stored.</param>
		public void SetWindowPosition(string p_strWindowName, Form p_frmWindow)
		{
			if (p_frmWindow.WindowState == FormWindowState.Minimized)
				return;
			m_dicPositions[p_strWindowName] = new LocationInfo(p_frmWindow.Location, p_frmWindow.ClientSize, p_frmWindow.WindowState == FormWindowState.Maximized);
		}

		#region IXmlSerializable Members

		/// <summary>
		/// This method is reserved, and returns <lang dref="null"/> as required.
		/// </summary>
		/// <returns><lang dref="null"/>, as required.</returns>
		public XmlSchema GetSchema()
		{
			return null;
		}

		/// <summary>
		/// Deserializes the object from XML.
		/// </summary>
		/// <param name="reader">The xml reader from which to deserialize the object.</param>
		public void ReadXml(XmlReader reader)
		{
			bool booIsEmpty = reader.IsEmptyElement;
			reader.ReadStartElement();
			if (booIsEmpty)
				return;
			while (reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "position")
			{
				string strWindowName = reader["window"];
				reader.ReadStartElement("position");

				XmlSerializer xsrLocationInfo = new XmlSerializer(typeof(LocationInfo));
				m_dicPositions[strWindowName] = (LocationInfo)xsrLocationInfo.Deserialize(reader);

				reader.ReadEndElement();
			}
			reader.ReadEndElement();
		}

		/// <summary>
		/// Serializes the object to XML.
		/// </summary>
		/// <param name="writer">The xml writer to which to serialize the object.</param>
		public void WriteXml(XmlWriter writer)
		{
			foreach (KeyValuePair<string, LocationInfo> kvpPosition in m_dicPositions)
			{
				writer.WriteStartElement("position");
				writer.WriteAttributeString("window", kvpPosition.Key);

				XmlSerializer xsrLocationInfo = new XmlSerializer(typeof(LocationInfo));
				xsrLocationInfo.Serialize(writer, kvpPosition.Value);

				writer.WriteEndElement();
			}
		}

		#endregion
	}
}
