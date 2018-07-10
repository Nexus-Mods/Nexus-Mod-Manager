using System;
using System.IO;

namespace Nexus.Client.Mods.Formats.OMod
{
	public partial class OMod
	{
		/// <summary>
		/// Describes a file stored in the internally compressed streams of an OMod.
		/// </summary>
		protected class FileInfo
		{
			#region Properties

			/// <summary>
			/// Gets the name of the file.
			/// </summary>
			/// <value>The name of the file.</value>
			public string Name { get; private set; }

			/// <summary>
			/// Gets the CRC of the file.
			/// </summary>
			/// <value>The CRC of the file.</value>
			public UInt32 CRC { get; private set; }

			/// <summary>
			/// Gets the length of the file.
			/// </summary>
			/// <value>The length of the file.</value>
			public Int64 Length { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_strFileName">The name of the file.</param>
			/// <param name="p_uinCRC">The CRC of the file.</param>
			/// <param name="p_intLength">The length of the file.</param>
			public FileInfo(string p_strFileName, UInt32 p_uinCRC, Int64 p_intLength)
			{
				Name = p_strFileName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				CRC = p_uinCRC;
				Length = p_intLength;
			}

			#endregion

			/// <summary>
			/// Returns a string representation of the file info.
			/// </summary>
			/// <remarks>
			/// A <see cref="FileInfo"/> is represented by the name of the file.
			/// </remarks>
			/// <returns>The name of the file.</returns>
			public override string ToString()
			{
				return Name;
			}
		}
	}
}
