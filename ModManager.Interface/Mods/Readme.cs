using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Util;

namespace Nexus.Client.Mods
{
	/// <summary>
	/// The possible formats for the readme file.
	/// </summary>
	public enum ReadmeFormat
	{
		/// <summary>
		/// Plain text.
		/// </summary>
		PlainText,

		/// <summary>
		/// Rich text format.
		/// </summary>
		RichText,

		/// <summary>
		/// HTML
		/// </summary>
		HTML,

		/// <summary>
		/// DOC
		/// </summary>
		DOC,

		/// <summary>
		/// PDF
		/// </summary>
		PDF
	}

	/// <summary>
	/// Describes the readme file of a mod.
	/// </summary>
	public class Readme : ObservableObject
	{
		/// <summary>
		/// The mapping of valid extensions to their respective readme formats.
		/// </summary>
		private static Dictionary<string, ReadmeFormat> m_dicFormats = new Dictionary<string, ReadmeFormat>(StringComparer.InvariantCultureIgnoreCase)
																		{
																			{".txt", ReadmeFormat.PlainText},
																			{".rtf", ReadmeFormat.RichText},
																			{".html", ReadmeFormat.HTML},
																			{".htm", ReadmeFormat.HTML},
																			{".doc", ReadmeFormat.DOC},
																			{".docx", ReadmeFormat.DOC},
																			{".pdf", ReadmeFormat.PDF}
																		};

		/// <summary>
		/// The mapping of invalid filenames to their respective readme formats.
		/// </summary>
		private static List<string> m_lstFilenames = new List<string>	{
																			{"script.txt"},
																		};

		/// <summary>
		/// Get the list of valid extensions.
		/// </summary>
		/// <value>The list of valid extensions.</value>
		public static string[] ValidExtensions
		{
			get
			{
				return new List<string>(m_dicFormats.Keys).ToArray();
			}
		}

		private ReadmeFormat m_fmtFormat = ReadmeFormat.PlainText;
		private string m_strText = null;

		#region Properties

		/// <summary>
		/// Gets or sets the extension of the readme.
		/// </summary>
		/// <value>The extension of the readme.</value>
		public string Extension
		{
			get
			{
				foreach (KeyValuePair<string, ReadmeFormat> kvpFormat in m_dicFormats)
					if (kvpFormat.Value.Equals(m_fmtFormat))
						return kvpFormat.Key;
				throw new Exception("Unexpected value for ReadmeFormat enum.");
			}
			set
			{
				string strValue = (!value.StartsWith(".")) ? "." + value : value;
				if (!m_dicFormats.ContainsKey(strValue))
					throw new ArgumentException("Unrecognized extension: " + value);
				Format = m_dicFormats[strValue];
			}
		}

		/// <summary>
		/// Gets or sets the readme format.
		/// </summary>
		/// <value>The readme format.</value>
		public ReadmeFormat Format
		{
			get
			{
				return m_fmtFormat;
			}
			set
			{
				SetPropertyIfChanged(ref m_fmtFormat, value, () => Format);
			}
		}

		/// <summary>
		/// Gets or sets the readme text.
		/// </summary>
		/// <value>The readme text.</value>
		public string Text
		{
			get
			{
				return m_strText;
			}
			set
			{
				SetPropertyIfChanged(ref m_strText, value, () => Text);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_fmtFormat">The readme format.</param>
		/// <param name="p_strText">The readme text.</param>
		public Readme(ReadmeFormat p_fmtFormat, string p_strText)
		{
			Format = p_fmtFormat;
			Text = p_strText;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strPath">The path of the readme file. This is used to determine the <see cref="Format"/>.</param>
		/// <param name="p_strText">The readme text.</param>
		public Readme(string p_strPath, string p_strText)
		{
			Extension = Path.GetExtension(p_strPath);
			Text = p_strText;
		}

		#endregion

		/// <summary>
		/// Determines if the specified readme file is of a recognized format.
		/// </summary>
		/// <param name="p_strPath">The path of the readme file.</param>
		/// <returns><c>true</c> if the given path has a valid extension;
		/// <c>false</c> otherwise.</returns>
		public static bool IsValidReadme(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath))
				return false;
			if (!IsValidFilename(Path.GetFileName(p_strPath.ToLower())))
				return false;
			if (IsConfigFile(Path.GetFileName(p_strPath)))
				return false;
			return IsValidExtension(Path.GetExtension(p_strPath.ToLower()));
		}

		/// <summary>
		/// Determines if the given extension is a valid readme extension.
		/// </summary>
		/// <param name="p_strExtension">The extension whose validity is to be determined.</param>
		/// <returns><c>true</c> if the given extension is a valid readme extension;
		/// <c>false</c> otherwise.</returns>
		public static bool IsValidExtension(string p_strExtension)
		{
			if (!p_strExtension.StartsWith("."))
				p_strExtension = "." + p_strExtension;
			return m_dicFormats.ContainsKey(p_strExtension);
		}

		/// <summary>
		/// Determines if the given filename is a valid readme file.
		/// </summary>
		/// <param name="p_strExtension">The filename whose validity is to be determined.</param>
		/// <returns><c>true</c> if the given filename is a valid readme file;
		/// <c>false</c> otherwise.</returns>
		public static bool IsValidFilename(string p_strFilename)
		{
			return !m_lstFilenames.Contains(p_strFilename);
		}

		public static bool IsConfigFile(string p_strFilename)
		{
			return p_strFilename.IndexOf("config", StringComparison.InvariantCultureIgnoreCase) >= 0;
		}
	}
}
