using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.Sorter
{
	/// <summary>
	/// Marshals string arrays to and from unmanaged code.
	/// </summary>
	public class StringArrayManualMarshaler : IDisposable
	{
		private IntPtr m_ptrStringArray = IntPtr.Zero;
		private Int32 m_intArraySize = 0;
		private Encoding m_encEncoding = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strEncoding">The encoding of the string to marshal.</param>
		public StringArrayManualMarshaler(string p_strEncoding)
		{
			switch (p_strEncoding)
			{
				case "ASCII":
					m_encEncoding = Encoding.ASCII;
					break;
				case "BigEndianUnicode":
					m_encEncoding = Encoding.BigEndianUnicode;
					break;
				case "Default":
					m_encEncoding = Encoding.Default;
					break;
				case "Unicode":
					m_encEncoding = Encoding.Unicode;
					break;
				case "UTF32":
					m_encEncoding = Encoding.UTF32;
					break;
				case "UTF7":
					m_encEncoding = Encoding.UTF7;
					break;
				case "UTF8":
					m_encEncoding = Encoding.UTF8;
					break;
			}
		}

		#endregion

		#region ICustomMarshaler Members

		/// <summary>
		/// Marshals the given string to a pointer.
		/// </summary>
		/// <param name="ManagedObj">The string to marshal.</param>
		/// <returns>A pointer to the marshalled string.</returns>
		public IntPtr MarshalManagedToNative(string[] ManagedObj)
		{
			string[] strStrings = (string[])ManagedObj;
			if (strStrings == null)
				m_ptrStringArray = IntPtr.Zero;
			else
			{
				m_intArraySize = strStrings.Length;
				IntPtr[] ptrStrings = new IntPtr[m_intArraySize];
				for (Int32 i = 0; i < m_intArraySize; i++)
					ptrStrings[i] = StringMarshaler.GetNullTerminatedStringPointer(strStrings[i], m_encEncoding);
				m_ptrStringArray = Marshal.AllocHGlobal(m_intArraySize * IntPtr.Size);
				Marshal.Copy(ptrStrings, 0, m_ptrStringArray, m_intArraySize);
			}
			return m_ptrStringArray;
		}

		/// <summary>
		/// Marshals the given pointer to a string.
		/// </summary>
		/// <param name="pNativeData">The pointer to the data to marshal to a string.</param>
		/// <param name="p_intSize">The length of the array to marshal.</param>
		/// <returns>The marshaled string.</returns>
		public string[] MarshalNativeToManaged(IntPtr pNativeData, Int32 p_intSize)
		{
			if (pNativeData == IntPtr.Zero)
				return null;

			string[] strStrings = new string[p_intSize];
			for (Int32 i = 0; i < p_intSize; i++)
			{
				IntPtr ptrString = Marshal.ReadIntPtr(pNativeData, i * IntPtr.Size);
				List<byte> lstString = StringMarshaler.GetStringBytes(ptrString, m_encEncoding);
				strStrings[i] = m_encEncoding.GetString(lstString.ToArray(), 0, lstString.Count);
			}
			return strStrings;
		}

		#endregion

		#region IDisposable Members

		/// <summary>
		/// Disposes of the pointers that were allocated during marshalling.
		/// </summary>
		public void Dispose()
		{
			if (m_ptrStringArray != IntPtr.Zero)
			{
				for (Int32 i = 0; i < m_intArraySize; i++)
				{
					IntPtr ptrString = Marshal.ReadIntPtr(m_ptrStringArray, i * IntPtr.Size);
					Marshal.FreeHGlobal(ptrString);
				}
				Marshal.FreeHGlobal(m_ptrStringArray);
			}
		}

		#endregion
	}
}
