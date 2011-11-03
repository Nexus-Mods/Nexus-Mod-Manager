using System;
using System.Drawing;
using System.IO;

namespace Nexus.Client.Plugins
{
	/// <summary>
	/// Encapsulates the information about a plugin.
	/// </summary>
	public class Plugin
	{
		#region Properties

		/// <summary>
		/// Gets the description of the plugin.
		/// </summary>
		/// <value>The description of the plugin.</value>
		public string Description { get; private set; }

		/// <summary>
		/// Gets the image of the plugin.
		/// </summary>
		/// <value>The picture of the plugin.</value>
		public Image Picture { get; private set; }

		/// <summary>
		/// Gets the filename of the plugin.
		/// </summary>
		/// <value>The filename of the plugin.</value>
		public string Filename { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strPath">The filename of the plugin.</param>
		/// <param name="p_strDescription">The description of the plugin.</param>
		/// <param name="p_imgPicture">The picture of the plugin.</param>
		public Plugin(string p_strPath, string p_strDescription, Image p_imgPicture)
		{
			Filename = p_strPath;
			Description = p_strDescription;
			Picture = p_imgPicture;
		}

		#endregion

		/// <summary>
		/// Uses the filename to represent the plugin.
		/// </summary>
		/// <returns>The filename.</returns>
		public override string ToString()
		{
			return String.Format("{0} ({1})", Path.GetFileName(Filename), Filename);
		}
	}
}
