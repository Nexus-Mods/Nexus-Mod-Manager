using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using System;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// A dictionary that stores keyed settings.
	/// </summary>
	public class KeyedSettings<T> : Dictionary<string, T>, IXmlSerializable
	{
		#region Properties

		/// <summary>
		/// Gets the name of the key name to use when serializing the dictionary.
		/// </summary>
		/// <value>The name of the key name to use when serializing the dictionary.</value>
		protected virtual string XmlKeyName
		{
			get
			{
				return "key";
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public KeyedSettings()
			: base(StringComparer.InvariantCultureIgnoreCase)
		{
		}

		/// <summary>
		/// Initializes the keyed settings with the values in the given dictionary.
		/// </summary>
		/// <param name="p_dicValues">The values with which to initializes this object.</param>
		public KeyedSettings(Dictionary<string, T> p_dicValues)
			: base(p_dicValues, StringComparer.InvariantCultureIgnoreCase)
		{
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
			while (reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "item")
			{
				string strKey = reader[XmlKeyName];
				booIsEmpty = reader.IsEmptyElement;
				reader.ReadStartElement("item");
				T tValue = default(T);
				if (!booIsEmpty)
				{
					XmlSerializer xsrSerializer = new XmlSerializer(typeof(T));
					tValue = (T)xsrSerializer.Deserialize(reader);
					reader.ReadEndElement();
				}
				this[strKey] = tValue;
			}
			reader.ReadEndElement();
		}

		/// <summary>
		/// Serializes the object to XML.
		/// </summary>
		/// <param name="writer">The xml writer to which to serialize the object.</param>
		public void WriteXml(XmlWriter writer)
		{
			foreach (KeyValuePair<string, T> kvpItem in this)
			{
				writer.WriteStartElement("item");
				writer.WriteAttributeString(XmlKeyName, (string)kvpItem.Key);

				try
				{
					XmlSerializer xsrSerializer = new XmlSerializer(typeof(T));
					xsrSerializer.Serialize(writer, kvpItem.Value);
				}
				catch (Exception e)
				{
					string g = e.ToString();
				}

				writer.WriteEndElement();
			}
		}

		#endregion

		/// <summary>
		/// Gets or sets the value for the given key.
		/// </summary>
		/// <remarks>
		/// This implementation returns the default value of <typeparamref name="T"/> instead
		/// of throwing an exceiption of the given key is not in the dictionary.
		/// </remarks>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The value for the given key.</returns>
		public new T this[string p_strKey]
		{
			get
			{
				if (!ContainsKey(p_strKey))
					return default(T);
				return base[p_strKey];
			}
			set
			{
				base[p_strKey] = value;
			}
		}
	}
}
