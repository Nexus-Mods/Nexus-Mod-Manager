using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;

namespace Nexus.Client.Util
{
	/// <summary>
	/// Provides methods to interact with INI files.
	/// </summary>
	public static class IniMethods
	{
		/// <summary>
		/// Reads an INI value as a string.
		/// </summary>
		/// <param name="section">The section containing the value to be read.</param>
		/// <param name="key">The key of the value to be read.</param>
		/// <param name="def">The default value to use if the key is not present in the INI file.</param>
		/// <param name="path">The absolute path of the INI file.</param>
		/// <returns>The sepecifed INI value, as a string.</returns>
		public static string GetPrivateProfileString(string section, string key, string def, string path)
		{
			string[] strText = File.ReadAllLines(path);
			bool booInSection = false;
			for (Int32 i = 0; i < strText.Length; i++)
			{
				string strLine = strText[i].Trim();
				if (strLine.StartsWith("[") && strLine.EndsWith("]"))
					booInSection = strLine.Equals(String.Format("[{0}]", section), StringComparison.CurrentCultureIgnoreCase);
				else if (booInSection)
				{
					string[] strValue = strText[i].Split(new char[] { '=' }, 2);
					if (strValue[0].Trim().Equals(key, StringComparison.CurrentCultureIgnoreCase))
						return strValue[1];
				}
			}
			return def;
		}

		/// <summary>
		/// Reads an INI value as an <see cref="Int32"/>.
		/// </summary>
		/// <param name="section">The section containing the value to be read.</param>
		/// <param name="key">The key of the value to be read.</param>
		/// <param name="def">The default value to use if the key is not present in the INI file.</param>
		/// <param name="path">The absolute path of the INI file.</param>
		/// <returns>The sepecifed INI value, as an <see cref="Int32"/>.</returns>
		public static int GetPrivateProfileInt32(string section, string key, int def, string path)
		{
			string strValue = GetPrivateProfileString(section, key, def.ToString(), path);
			return Int32.Parse(strValue);
		}

		/// <summary>
		/// Reads an INI value as an <see cref="UInt64"/>.
		/// </summary>
		/// <param name="section">The section containing the value to be read.</param>
		/// <param name="key">The key of the value to be read.</param>
		/// <param name="def">The default value to use if the key is not present in the INI file.</param>
		/// <param name="path">The absolute path of the INI file.</param>
		/// <returns>The sepecifed INI value, as an <see cref="UInt64"/>.</returns>
		public static UInt64 GetPrivateProfileUInt64(string section, string key, UInt64 def, string path)
		{
			string strValue = GetPrivateProfileString(section, key, def.ToString(), path);
			return UInt64.Parse(strValue);
		}

		/// <summary>
		/// Writes the given ring value to an INI file.
		/// </summary>
		/// <param name="section">The section containing the value to be written.</param>
		/// <param name="key">The key of the value to be written.</param>
		/// <param name="val">The vaue to write.</param>
		/// <param name="path">The absolute path of the INI file.</param>
		public static void WritePrivateProfileString(string section, string key, string val, string path)
		{
			string[] strText = File.ReadAllLines(path);
			bool booInSection = false;
			Int32 intSectionStart = -1;
			for (Int32 i = 0; i < strText.Length; i++)
			{
				string strLine = strText[i].Trim();
				if (strLine.StartsWith("[") && strLine.EndsWith("]"))
				{
					booInSection = strLine.Equals(String.Format("[{0}]", section), StringComparison.CurrentCultureIgnoreCase);
					if (booInSection && (intSectionStart < 0))
						intSectionStart = i;
				}
				else if (booInSection)
				{
					string[] strValue = strText[i].Split(new char[] { '=' }, 2);
					if (strValue[0].Trim().Equals(key, StringComparison.CurrentCultureIgnoreCase))
					{
						strValue[1] = val;
						strText[i] = String.Join("=", strValue);
						File.WriteAllLines(path, strText);
						return;
					}
				}
			}
			//the section, and hence the key, was not found
			if (intSectionStart < 0)
			{
				StringBuilder stbValue = new StringBuilder();
				stbValue.AppendLine();
				stbValue.AppendFormat("[{0}]", section).AppendLine();
				stbValue.AppendFormat("{0}={1}", key, val);
				File.AppendAllText(path, stbValue.ToString());
				return;
			}
			//the key was not found, but the section was
			string[] strNewText = new string[strText.Length + 1];
			Array.Copy(strText, strNewText, intSectionStart + 1);
			Array.Copy(strText, intSectionStart + 1, strNewText, intSectionStart + 2, strText.Length - intSectionStart - 1);
			strNewText[intSectionStart + 1] = String.Format("{0}={1}", key, val);
			File.WriteAllLines(path, strText);
		}

		/// <summary>
		/// Writes the given <see cref="Int32"/> value to an INI file.
		/// </summary>
		/// <param name="section">The section containing the value to be written.</param>
		/// <param name="key">The key of the value to be written.</param>
		/// <param name="val">The vaue to write.</param>
		/// <param name="path">The absolute path of the INI file.</param>
		public static void WritePrivateProfileInt32(string section, string key, int val, string path)
		{
			WritePrivateProfileString(section, key, val.ToString(), path);
		}
	}
}
