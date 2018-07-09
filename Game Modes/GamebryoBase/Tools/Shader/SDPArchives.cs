using System;
using System.IO;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo.Tools.Shader
{
	/// <summary>
	/// Encapsulates working with shader archive files.
	/// </summary>
	public class SDPArchives
	{
		private IGameModeEnvironmentInfo m_gmiGameModeInfo = null;
		private FileUtil m_futFileUtility=null;

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_gmiGameModeInfo">The environment info of the current game mode.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		public SDPArchives(IGameModeEnvironmentInfo p_gmiGameModeInfo, FileUtil p_futFileUtility)
		{
			m_gmiGameModeInfo = p_gmiGameModeInfo;
			m_futFileUtility = p_futFileUtility;
		}

		/// <summary>
		/// Gets the path of the specified shader package.
		/// </summary>
		/// <param name="package">The id of the package whose path is to be returned.</param>
		/// <returns>The path of the specified shader package.</returns>
		public string GetPath(int package)
		{
			string strShaderPath = Path.Combine(m_gmiGameModeInfo.InstallationPath, "data");
			strShaderPath = Path.Combine(strShaderPath, "shaders");
			strShaderPath = Path.Combine(strShaderPath, "shaderpackage" + package.ToString().PadLeft(3, '0') + ".sdp");
			return strShaderPath;
		}

		/// <summary>
		/// Replaces the specified shader data.
		/// </summary>
		/// <param name="file">The shader file whose data is to be replaced.</param>
		/// <param name="shader">The shader in the file that is to be replaced.</param>
		/// <param name="newdata">The data with which to replaced the existing shader data.</param>
		/// <param name="OldData">Returns the existing shader data.</param>
		/// <param name="crc">The CRC of the new shader data. Used to verify the replacement; use 0 if no validation is desired.</param>
		/// <returns><c>true</c> if the shader was replaced; <c>false</c> otherwise.</returns>
		private bool ReplaceShader(string file, string shader, byte[] newdata, out byte[] OldData, uint crc)
		{
			string tempshader = Path.Combine(m_futFileUtility.CreateTempDirectory(), "tempshader");

			DateTime timeStamp = File.GetLastWriteTime(file);
			File.Delete(tempshader);
			File.Move(file, tempshader);
			BinaryReader br = new BinaryReader(File.OpenRead(tempshader), System.Text.Encoding.Default);
			BinaryWriter bw = new BinaryWriter(File.Create(file), System.Text.Encoding.Default);
			bw.Write(br.ReadInt32());
			int num = br.ReadInt32();
			bw.Write(num);
			long sizeoffset = br.BaseStream.Position;
			bw.Write(br.ReadInt32());
			bool found = false;
			OldData = null;
			for (int i = 0; i < num; i++)
			{
				char[] name = br.ReadChars(0x100);
				int size = br.ReadInt32();
				byte[] data = br.ReadBytes(size);

				bw.Write(name);
				string sname = "";
				for (int i2 = 0; i2 < 100; i2++) { if (name[i2] == '\0') break; sname += name[i2]; }
				if (!found && sname == shader)
				{
					ICSharpCode.SharpZipLib.Checksums.Crc32 ccrc = new ICSharpCode.SharpZipLib.Checksums.Crc32();
					ccrc.Update(data);
					if (crc == 0 || ccrc.Value == crc)
					{
						bw.Write(newdata.Length);
						bw.Write(newdata);
						found = true;
						OldData = data;
					}
					else
					{
						bw.Write(size);
						bw.Write(data);
					}
				}
				else
				{
					bw.Write(size);
					bw.Write(data);
				}
			}
			bw.BaseStream.Position = sizeoffset;
			bw.Write((int)(bw.BaseStream.Length - 12));
			br.Close();
			bw.Close();
			File.Delete(tempshader);
			File.SetLastWriteTime(file, timeStamp);
			return found;
		}

		/// <summary>
		/// Edits the specified shader.
		/// </summary>
		/// <param name="package">The id of the package containing the shader to edit.</param>
		/// <param name="name">The name of the shader to edit.</param>
		/// <param name="newData">The data with which to replaced the existing shader data.</param>
		/// <param name="oldData">Returns the existing shader data.</param>
		/// <returns><c>true</c> if the shader was edited; <c>false</c> otherwise.</returns>
		public bool EditShader(int package, string name, byte[] newData, out byte[] oldData)
		{
			string path = GetPath(package);
			if (!File.Exists(path)) { oldData = null; return false; }
			return ReplaceShader(path, name, newData, out oldData, 0);
		}

		/// <summary>
		/// Restores the specified shader to the given data.
		/// </summary>
		/// <param name="package">The id of the package containing the shader to restore.</param>
		/// <param name="name">The name of the shader to restore.</param>
		/// <param name="data">The data with which to replaced the existing shader data.</param>
		/// <param name="crc">The CRC of the new shader data. Used to verify the replacement; use 0 if no validation is desired.</param>
		/// <returns><c>true</c> if the shader was restored; <c>false</c> otherwise.</returns>
		public bool RestoreShader(int package, string name, byte[] data, uint crc)
		{
			byte[] unused;
			string path = GetPath(package);
			if (!File.Exists(path)) return false;
			return ReplaceShader(path, name, data, out unused, crc);
		}

		/// <summary>
		/// Gets the specified shader's data.
		/// </summary>
		/// <param name="package">The id of the package containing the shader to retrieve.</param>
		/// <param name="shader">The name of the shader to retrieve.</param>
		/// <returns>The specified shader's data.</returns>
		public byte[] GetShader(int package, string shader)
		{
			string file = GetPath(package);
			if (!File.Exists(file))
				return null;

			BinaryReader br = new BinaryReader(File.OpenRead(file), System.Text.Encoding.Default);
			br.ReadInt32();
			int num = br.ReadInt32();
			long sizeoffset = br.BaseStream.Position;
			br.ReadInt32();
			bool found = false;
			byte[] OldData = null;
			for (int i = 0; i < num; i++)
			{
				char[] name = br.ReadChars(0x100);
				int size = br.ReadInt32();
				byte[] data = br.ReadBytes(size);

				string sname = "";
				for (int i2 = 0; i2 < 100; i2++) { if (name[i2] == '\0') break; sname += name[i2]; }
				if (!found && sname == shader)
				{
					found = true;
					OldData = data;
				}
			}
			br.Close();
			return OldData;
		}
	}
}
