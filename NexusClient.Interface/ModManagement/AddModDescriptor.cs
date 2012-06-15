using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Describes a mod that is being added to the mod manager.
	/// </summary>
	public class AddModDescriptor : IXmlSerializable
	{
		#region Properties

		/// <summary>
		/// Gets the uri that points to the external source of the mod.
		/// </summary>
		/// <remarks>
		/// The external source is the mod's entry in a mod repository. The source provides
		/// info about the mod, as well as a location from which the mod can be downloaded.
		/// </remarks>
		/// <value>The uri that points to the external source of the mod.</value>
		public Uri SourceUri { get; private set; }

		/// <summary>
		/// Gets or sets the source path of the mod.
		/// </summary>
		/// <remarks>
		/// The source path is the path to the mod's main source file on the local file system.
		/// This is the path that will be used to build the mod.
		/// </remarks>
		/// <value>The source path of the mod.</value>
		public string SourcePath { get; set; }

		/// <summary>
		/// Gets or sets the default source path of the mod.
		/// </summary>
		/// <remarks>
		/// The default source path is used if the download files don't specify a soruce path file name.
		/// </remarks>
		/// <value>The default source path of the mod.</value>
		/// <see cref="SourcePath"/>
		public string DefaultSourcePath { get; set; }

		/// <summary>
		/// Gets the list of files that still need to be downloaded to build the mod.
		/// </summary>
		/// <value>The list of files that still need to be downloaded to build the mod.</value>
		public List<Uri> DownloadFiles { get; private set; }

		/// <summary>
		/// Gets the list of files that have been downloaded to build the mod.
		/// </summary>
		/// <value>The list of files that have been downloaded to build the mod.</value>
		public List<string> DownloadedFiles { get; private set; }

		/// <summary>
		/// Gets or set the status of the task that is adding the mod.
		/// </summary>
		/// <value>The status of the task that is adding the mod.</value>
		public TaskStatus Status { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public AddModDescriptor()
		{
			DownloadFiles = new List<Uri>();
			DownloadedFiles = new List<string>();
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_uriSourceUri">The uri that points to the external source of the mod.</param>
		/// <param name="p_strDefaultSourcePath">The default source path of the mod.</param>
		/// <param name="p_enmDownloadFiles">The list of files that still need to be downloaded to build the mod.</param>
		/// <param name="p_tstStatus">The status of the task that is adding the mod.</param>
		public AddModDescriptor(Uri p_uriSourceUri, string p_strDefaultSourcePath, IEnumerable<Uri> p_enmDownloadFiles, TaskStatus p_tstStatus)
			: this()
		{
			SourceUri = p_uriSourceUri;
			DefaultSourcePath = p_strDefaultSourcePath;
			if (p_enmDownloadFiles != null)
				DownloadFiles.AddRange(p_enmDownloadFiles);
			Status = p_tstStatus;
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

			reader.ReadStartElement("sourceUri");
			XmlSerializer xsrSerializer = new XmlSerializer(typeof(string));
			SourceUri = new Uri((string)xsrSerializer.Deserialize(reader));
			reader.ReadEndElement();

			reader.ReadStartElement("sourcePath");
			xsrSerializer = new XmlSerializer(typeof(string));
			SourcePath = (string)xsrSerializer.Deserialize(reader);
			reader.ReadEndElement();

			reader.ReadStartElement("defaultSourcePath");
			xsrSerializer = new XmlSerializer(typeof(string));
			DefaultSourcePath = (string)xsrSerializer.Deserialize(reader);
			reader.ReadEndElement();

			reader.ReadStartElement("status");
			xsrSerializer = new XmlSerializer(typeof(TaskStatus));
			Status = (TaskStatus)xsrSerializer.Deserialize(reader);
			reader.ReadEndElement();

			booIsEmpty = reader.IsEmptyElement;
			reader.ReadStartElement("downloadFiles");
			if (!booIsEmpty)
			{
				while (reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "file")
				{
					DownloadFiles.Add(new Uri(reader["path"]));
					booIsEmpty = reader.IsEmptyElement;
					reader.ReadStartElement("file");
					if (!booIsEmpty)
						reader.ReadEndElement();
				}
				reader.ReadEndElement();
			}

			booIsEmpty = reader.IsEmptyElement;
			reader.ReadStartElement("downloadedFiles");
			if (!booIsEmpty)
			{
				while (reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "file")
				{
					DownloadedFiles.Add(reader["path"]);
					booIsEmpty = reader.IsEmptyElement;
					reader.ReadStartElement("file");
					if (!booIsEmpty)
						reader.ReadEndElement();
				}
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
			writer.WriteStartElement("sourceUri");
			XmlSerializer xsrSerializer = new XmlSerializer(typeof(string));
			xsrSerializer.Serialize(writer, SourceUri.ToString());
			writer.WriteEndElement();

			writer.WriteStartElement("sourcePath");
			xsrSerializer = new XmlSerializer(typeof(string));
			xsrSerializer.Serialize(writer, SourcePath);
			writer.WriteEndElement();

			writer.WriteStartElement("defaultSourcePath");
			xsrSerializer = new XmlSerializer(typeof(string));
			xsrSerializer.Serialize(writer, DefaultSourcePath);
			writer.WriteEndElement();

			writer.WriteStartElement("status");
			xsrSerializer = new XmlSerializer(typeof(TaskStatus));
			xsrSerializer.Serialize(writer, Status);
			writer.WriteEndElement();

			writer.WriteStartElement("downloadFiles");
			foreach (Uri uriFile in DownloadFiles)
			{
				writer.WriteStartElement("file");
				writer.WriteAttributeString("path", uriFile.ToString());
				writer.WriteEndElement();
			}
			writer.WriteEndElement();

			writer.WriteStartElement("downloadedFiles");
			foreach (string strFile in DownloadedFiles)
			{
				writer.WriteStartElement("file");
				writer.WriteAttributeString("path", strFile);
				writer.WriteEndElement();
			}
			writer.WriteEndElement();
		}

		#endregion

		/// <summary>
		/// Returns a summary of the descriptor.
		/// </summary>
		/// <returns>A summary of the descriptor.</returns>
		public override string ToString()
		{
			return String.Format("{0} => {1} ({2})", SourceUri.ToString(), SourcePath, DownloadFiles.Count);
		}
	}
}
