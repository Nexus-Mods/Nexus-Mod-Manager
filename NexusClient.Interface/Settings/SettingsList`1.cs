using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// A serializable list, for use in programme settings.
	/// </summary>
	public class SettingsList<T> : List<T>, IXmlSerializable
	{
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
			while (reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "item")
			{
				booIsEmpty = reader.IsEmptyElement;
				reader.ReadStartElement("item");
				T tValue = default(T);
				if (!booIsEmpty)
				{
					XmlSerializer xsrSerializer = new XmlSerializer(typeof(T));
					tValue = (T)xsrSerializer.Deserialize(reader);
					reader.ReadEndElement();
				}
				this.Add(tValue);
			}
			reader.ReadEndElement();
		}

		/// <summary>
		/// Serializes the object to XML.
		/// </summary>
		/// <param name="writer">The xml writer to which to serialize the object.</param>
		public void WriteXml(XmlWriter writer)
		{
			foreach (T tItem in this)
			{
				writer.WriteStartElement("item");
				XmlSerializer xsrSerializer = new XmlSerializer(typeof(T));
				xsrSerializer.Serialize(writer, tItem);
				writer.WriteEndElement();
			}
		}

		#endregion
	}
}
