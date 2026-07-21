using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Nexus.Client.Games
{
	/// <summary>
	/// An indexed list of file paths.
	/// </summary>
	public class FileSet : IEnumerable<string>
	{
		StringDictionary m_sdcFilePaths = new StringDictionary();

		/// <summary>
		/// Gets or sets the file path for the given key.
		/// </summary>
		/// <param name="p_strFileKey">The key corresponding to the desired file path.</param>
		/// <returns>The file path for the given key.</returns>
		public string this[string p_strFileKey]
		{
			get
			{
				if (!m_sdcFilePaths.ContainsKey(p_strFileKey))
					return null;
				return m_sdcFilePaths[p_strFileKey];
			}
			set
			{
				m_sdcFilePaths[p_strFileKey] = value;
			}
		}

		#region IEnumerable Members

		/// <summary>
		/// Gets an enumerator this iterates over the file paths.
		/// </summary>
		/// <returns>An enumerator this iterates over the file paths.</returns>
		public IEnumerator GetEnumerator()
		{
			return m_sdcFilePaths.Values.GetEnumerator();
		}

		#endregion

		#region IEnumerable<string> Members

		/// <summary>
		/// Gets an enumerator this iterates over the file paths.
		/// </summary>
		/// <returns>An enumerator this iterates over the file paths.</returns>
		IEnumerator<string> IEnumerable<string>.GetEnumerator()
		{
			foreach (string strFile in m_sdcFilePaths.Values)
				yield return strFile;
		}

		#endregion
	}
}
