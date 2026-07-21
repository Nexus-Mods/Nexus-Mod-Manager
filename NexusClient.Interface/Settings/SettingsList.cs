using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// Extends the <see cref="StringCollection"/> to add implicit conversions to and from
	/// similar data structures.
	/// </summary>
	[Editor("System.Windows.Forms.Design.StringCollectionEditor", typeof(System.Drawing.Design.UITypeEditor))]
	public class SettingsList : StringCollection, IEnumerable<string>
	{
		#region IEnumerable<string> Members

		/// <summary>
		/// This class decorates <see cref="StringEnumerator"/> to make it appear
		/// as a <see cref="IEnumerator{T}"/>.
		/// </summary>
		private class EnumeratorOfString : IEnumerator<string>
		{
			private StringEnumerator m_senEnumerator = null;

			#region Contructors

			/// <summary>
			/// A simple contructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_senEnumerator">The <see cref="StringEnumerator"/> to decorate.</param>
			public EnumeratorOfString(StringEnumerator p_senEnumerator)
			{
				m_senEnumerator = p_senEnumerator;
			}

			#endregion

			#region IEnumerator<string> Members

			/// <summary>
			/// Gets the current value in the enumeration.
			/// </summary>
			/// <value>The current value in the enumeration.</value>
			public string Current
			{
				get
				{
					return m_senEnumerator.Current;
				}
			}

			#endregion

			#region IDisposable Members

			/// <summary>
			/// Disposes of the object.
			/// </summary>
			public void Dispose()
			{
				Reset();
			}

			#endregion

			#region IEnumerator Members

			/// <summary>
			/// Gets the current value in the enumeration.
			/// </summary>
			/// <value>The current value in the enumeration.</value>
			object System.Collections.IEnumerator.Current
			{
				get
				{
					return m_senEnumerator.Current;
				}
			}

			/// <summary>
			/// Moves to the next item in the enumeration.
			/// </summary>
			/// <returns><c>true</c> if there is another item; <c>false</c> otherwise.</returns>
			public bool MoveNext()
			{
				return m_senEnumerator.MoveNext();
			}

			/// <summary>
			/// Resets the enumeration.
			/// </summary>
			public void Reset()
			{
				m_senEnumerator.Reset();
			}

			#endregion
		}

		/// <summary>
		/// Gets an enumerator for the items in the list.
		/// </summary>
		/// <returns>An enumerator for the items in the list.</returns>
		public new IEnumerator<string> GetEnumerator()
		{
			return new EnumeratorOfString(base.GetEnumerator());
		}

		#endregion

		#region String List Conversions

		/// <summary>
		/// Implicitly converts <see cref="SettingsList"/> to a string array.
		/// </summary>
		/// <param name="arr">The <see cref="SettingsList"/> to convert to a string array.</param>
		/// <returns>A string array containing the strings in the given <see cref="SettingsList"/>.</returns>
		public static implicit operator string[](SettingsList arr)
		{
			if (arr == null)
				return null;
			List<string> lstValues = new List<string>(arr);
			return lstValues.ToArray();
		}

		/// <summary>
		/// Implicitly converts a string array to a <see cref="SettingsList"/>.
		/// </summary>
		/// <param name="values">The string array to convert to a <see cref="SettingsList"/>.</param>
		/// <returns>A <see cref="SettingsList"/> containing the strings in the given string array.</returns>
		public static implicit operator SettingsList(string[] values)
		{
			if (values == null)
				return null;
			SettingsList sslValues = new SettingsList();
			sslValues.AddRange(values);
			return sslValues;
		}

		/// <summary>
		/// Implicitly converts a <see cref="List{String}"/> to a <see cref="SettingsList"/>.
		/// </summary>
		/// <param name="values">The <see cref="List{String}"/> to convert to a <see cref="SettingsList"/>.</param>
		/// <returns>A <see cref="SettingsList"/> containing the strings in the given <see cref="List{String}"/>.</returns>
		public static implicit operator SettingsList(List<string> values)
		{
			if (values == null)
				return null;
			SettingsList sslValues = new SettingsList();
			sslValues.AddRange(values.ToArray());
			return sslValues;
		}

		#endregion

		#region Int32 List Conversions

		/// <summary>
		/// Implicitly converts <see cref="SettingsList"/> to an <see cref="Int32"/> array.
		/// </summary>
		/// <param name="arr">The <see cref="SettingsList"/> to convert to an <see cref="Int32"/> array.</param>
		/// <returns>An <see cref="Int32"/> array containing the values in the given <see cref="SettingsList"/>.</returns>
		public static implicit operator Int32[](SettingsList arr)
		{
			return (arr == null) ? null : ((List<Int32>)arr).ToArray();
		}

		/// <summary>
		/// Implicitly converts <see cref="SettingsList"/> to a <see cref="List{Int32}"/>.
		/// </summary>
		/// <param name="arr">The <see cref="SettingsList"/> to convert to a <see cref="List{Int32}"/>.</param>
		/// <returns>A <see cref="List{Int32}"/> containing the values in the given <see cref="SettingsList"/>.</returns>
		public static implicit operator List<Int32>(SettingsList arr)
		{
			if (arr == null)
				return null;
			List<Int32> lstValues = new List<Int32>();
			Int32 intValue = 0;
			for (Int32 i = 0; i < arr.Count; i++)
			{
				intValue = 0;
				Int32.TryParse(arr[i], out intValue);
				lstValues.Add(intValue);
			}
			return lstValues;
		}

		/// <summary>
		/// Implicitly converts an <see cref="Int32"/> array to a <see cref="SettingsList"/>.
		/// </summary>
		/// <param name="values">The <see cref="Int32"/> array to convert to a <see cref="SettingsList"/>.</param>
		/// <returns>A <see cref="SettingsList"/> containing the values in the given <see cref="Int32"/> array.</returns>
		public static implicit operator SettingsList(Int32[] values)
		{
			if (values == null)
				return null;
			SettingsList sslValues = new SettingsList();
			foreach (Int32 intValue in values)
				sslValues.Add(intValue.ToString());
			return sslValues;
		}

		/// <summary>
		/// Implicitly converts a <see cref="List{Int32}"/> to a <see cref="SettingsList"/>.
		/// </summary>
		/// <param name="values">The <see cref="List{Int32}"/> to convert to a <see cref="SettingsList"/>.</param>
		/// <returns>A <see cref="SettingsList"/> containing the values in the given <see cref="List{Int32}"/>.</returns>
		public static implicit operator SettingsList(List<Int32> values)
		{
			if (values == null)
				return null;
			SettingsList sslValues = new SettingsList();
			foreach (Int32 intValue in values)
				sslValues.Add(intValue.ToString());
			return sslValues;
		}

		#endregion
	}
}
