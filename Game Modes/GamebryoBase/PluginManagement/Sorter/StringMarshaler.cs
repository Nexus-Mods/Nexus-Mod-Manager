using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.Sorter
{
	/// <summary>
	/// Marshals strings to and from unmanaged code.
	/// </summary>
	public class StringMarshaler : ICustomMarshaler
	{
		/// <summary>
		/// Creates a marshaler for a string of the given encoding.
		/// </summary>
		/// <param name="p_strEncoding">The encoding of the string to marshal.</param>
		/// <returns>A marshaler for a string of the given encoding.</returns>
		public static ICustomMarshaler GetInstance(string p_strEncoding)
		{
			return new StringMarshaler(p_strEncoding);
		}

		/// <summary>
		/// Reads the bytes representing a string from the given pointer.
		/// </summary>
		/// <param name="pNativeData">The pointer to the string whose bytes are to be read.</param>
		/// <param name="p_encEncoding">The encoding of the string whose bytes are to be read.</param>
		/// <returns>The bytes representing a string from the given pointer.</returns>
		public static List<byte> GetStringBytes(IntPtr pNativeData, Encoding p_encEncoding)
		{
			List<byte> lstString = new List<byte>();
			Int32 intOffset = 0;
			byte bteCharacter = 0;
			//this works for UTF8, does it work for other encodings? Not all encoding as
			// null-byte terminated
			while ((bteCharacter = Marshal.ReadByte(pNativeData, intOffset++)) != 0)
				lstString.Add(bteCharacter);
			return lstString;
		}

		/// <summary>
		/// Returns a pointer to a unamanged, null terminated, copy the given string.
		/// </summary>
		/// <param name="p_strString">The string for which to create a pointer.</param>
		/// <param name="p_encEncoding">The encoding of the string.</param>
		/// <returns>A pointer to a unamanged, null terminated, copy the given string.</returns>
		public static IntPtr GetNullTerminatedStringPointer(string p_strString, Encoding p_encEncoding)
		{
			byte[] bteString = p_encEncoding.GetBytes(p_strString);
			byte[] bteNullTerminatedString = new byte[bteString.Length + 1];
			Array.Copy(bteString, bteNullTerminatedString, bteString.Length);
			IntPtr ptrString = Marshal.AllocHGlobal(bteNullTerminatedString.Length);
			Marshal.Copy(bteNullTerminatedString, 0, ptrString, bteNullTerminatedString.Length);
			return ptrString;
		}

		private IntPtr m_ptrString = IntPtr.Zero;
		private Encoding m_encEncoding = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strEncoding">The encoding of the string to marshal.</param>
		public StringMarshaler(string p_strEncoding)
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
		/// Cleans up the given managed data.
		/// </summary>
		/// <param name="ManagedObj">The managed object to clean up.</param>
		public void CleanUpManagedData(object ManagedObj)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Cleans up native data.
		/// </summary>
		/// <param name="pNativeData">The pointer to the native data to clean up.</param>
		public void CleanUpNativeData(IntPtr pNativeData)
		{
			if ((m_ptrString == pNativeData) && (pNativeData != IntPtr.Zero))
				Marshal.FreeHGlobal(pNativeData);
		}

		/// <summary>
		/// Gets the size of the native data being marshalled.
		/// </summary>
		/// <returns>The size of the native data being marshalled.</returns>
		public int GetNativeDataSize()
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Marshals the given string to a pointer.
		/// </summary>
		/// <param name="ManagedObj">The string to marshal.</param>
		/// <returns>A pointer to the marshalled string.</returns>
		public IntPtr MarshalManagedToNative(object ManagedObj)
		{
			if (!(ManagedObj is string))
				throw new ArgumentException("Can only marshal objects of type string.");
			string strString = (string)ManagedObj;
			if (strString == null)
				m_ptrString = IntPtr.Zero;
			else
			{
				m_ptrString = GetNullTerminatedStringPointer(strString, m_encEncoding);
			}
			return m_ptrString;
		}

		/// <summary>
		/// Marshals the given pointer to a string.
		/// </summary>
		/// <param name="pNativeData">The pointer to the data to marshal to a string.</param>
		/// <returns>The marshaled string.</returns>
		public object MarshalNativeToManaged(IntPtr pNativeData)
		{
			List<byte> lstString = GetStringBytes(pNativeData, m_encEncoding);
			return m_encEncoding.GetString(lstString.ToArray(), 0, lstString.Count);
		}

		#endregion
	}
}
