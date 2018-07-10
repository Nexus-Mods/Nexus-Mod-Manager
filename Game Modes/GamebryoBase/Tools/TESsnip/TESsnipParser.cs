using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Client.Games.Gamebryo.Tools.TESsnip
{
	internal delegate string dFormIDLookupS(string id);
	internal delegate string dFormIDLookupI(uint id);
	internal delegate string[] dFormIDScan(string type);

	/// <summary>
	/// Thrown when there is a problem parsing a TES plugin file.
	/// </summary>
	public class TESParserException : Exception
	{
		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="msg">The exception message.</param>
		public TESParserException(string msg) : base(msg) { }
	}

	/// <summary>
	/// The base record type found in TES plugins.
	/// </summary>
	public abstract class BaseRecord
	{
		/// <summary>
		/// Gets the name of the record.
		/// </summary>
		/// <value>The name of the record.</value>
		public string Name;

		/// <summary>
		/// Gets the size of the record.
		/// </summary>
		/// <value>The size of the record.</value>
		public abstract long Size { get; }

		/// <summary>
		/// Gets the size of the record.
		/// </summary>
		/// <value>The size of the record.</value>
		public abstract long Size2 { get; }

		private static byte[] input;
		private static byte[] output;
		private static MemoryStream ms;
		private static BinaryReader compReader;
		private static ICSharpCode.SharpZipLib.Zip.Compression.Inflater inf;
		
		/// <summary>
		/// Decompressed the data in the given stream.
		/// </summary>
		/// <param name="br">The stream containing the compressed data.</param>
		/// <param name="size">The number of byte to decompress.</param>
		/// <param name="outsize">The sixe of the decrompressed data.</param>
		/// <returns>A reader for the uncompressed data.</returns>
		protected static BinaryReader Decompress(BinaryReader br, int size, int outsize)
		{
			if (input.Length < size)
			{
				input = new byte[size];
			}
			if (output.Length < outsize)
			{
				output = new byte[outsize];
			}
			br.Read(input, 0, size);

			inf.SetInput(input, 0, size);
			try
			{
				inf.Inflate(output);
			}
			catch (ICSharpCode.SharpZipLib.SharpZipBaseException e)
			{
				//we ignore adler checksum mismatches, as I have a notion that they aren't always correctly
				// stored in the records.
				if (!e.Message.StartsWith("Adler"))
					throw e;
			}
			inf.Reset();

			ms.Position = 0;
			ms.Write(output, 0, outsize);
			ms.Position = 0;

			return compReader;
		}

		/// <summary>
		/// Initializes teh decompressor.
		/// </summary>
		protected static void InitDecompressor()
		{
			inf = new ICSharpCode.SharpZipLib.Zip.Compression.Inflater(false);
			ms = new MemoryStream();
			compReader = new BinaryReader(ms);
			input = new byte[0x1000];
			output = new byte[0x4000];
		}

		/// <summary>
		/// Closes teh decompressor.
		/// </summary>
		protected static void CloseDecompressor()
		{
			compReader.Close();
			compReader = null;
			inf = null;
			input = null;
			output = null;
		}

		/// <summary>
		/// Gets the description of the record.
		/// </summary>
		/// <returns>The description of the record.</returns>
		public abstract string GetDesc();

		/// <summary>
		/// Deletes the specified sub-record from the record.
		/// </summary>
		/// <param name="br">The record to delete.</param>
		public abstract void DeleteRecord(BaseRecord br);

		/// <summary>
		/// Adds the given sub-record to the record.
		/// </summary>
		/// <param name="br">The record to add.</param>
		public abstract void AddRecord(BaseRecord br);

		/// <summary>
		/// Gets the ids of the sub-records in the record.
		/// </summary>
		/// <param name="lower">Whether or not to lower-case the returned ids.</param>
		/// <returns>The ids of the sub-records in the record.</returns>
		internal abstract List<string> GetIDs(bool lower);


		internal abstract void SaveData(BinaryWriter bw);

		private static readonly byte[] RecByte = new byte[4];

		/// <summary>
		/// Reads the name of the given record.
		/// </summary>
		/// <param name="br">The record whose name is to be read.</param>
		/// <returns>The name of the given record.</returns>
		protected static string ReadRecName(BinaryReader br)
		{
			br.Read(RecByte, 0, 4);
			return "" + ((char)RecByte[0]) + ((char)RecByte[1]) + ((char)RecByte[2]) + ((char)RecByte[3]);
		}

		/// <summary>
		/// Writes a string to the given stream.
		/// </summary>
		/// <param name="bw">The stream to which to write the string.</param>
		/// <param name="s">The string to write.</param>
		protected static void WriteString(BinaryWriter bw, string s)
		{
			byte[] b = new byte[s.Length];
			for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
			bw.Write(b, 0, s.Length);
		}

		/// <summary>
		/// Clones the record.
		/// </summary>
		/// <returns>A clone of the record.</returns>
		public abstract BaseRecord Clone();
	}

	/// <summary>
	/// Encapsulates interacting with a TES plugin.
	/// </summary>
	public class TesPlugin : BaseRecord
	{
		/// <summary>
		/// Gets the records in the plugin file.
		/// </summary>
		/// <value>The records in the plugin file.</value>
		public readonly List<Rec> Records = new List<Rec>();

		/// <summary>
		/// Gets the size of the plugin.
		/// </summary>
		/// <value>Tthe size of the plugin.</value>
		public override long Size
		{
			get { long size = 0; foreach (Rec rec in Records) size += rec.Size2; return size; }
		}

		/// <summary>
		/// Gets the size of the plugin.
		/// </summary>
		/// <value>Tthe size of the plugin.</value>
		public override long Size2 { get { return Size; } }
		
		/// <summary>
		/// Gets a list of the plugin's master plugins.
		/// </summary>
		/// <value>A list of the plugin's master plugins.</value>
		public IList<string> Masters
		{
			get
			{
				List<string> lstMasters = new List<string>();
				foreach (SubRecord sr in ((Record)Records[0]).SubRecords)
				{
					switch (sr.Name)
					{
						case "MAST":
							lstMasters.Add(sr.GetStrData());
							break;
					}
				}
				return lstMasters;
			}
		}

		/// <summary>
		/// Deletes a record from the plugin.
		/// </summary>
		/// <param name="br">The record to delete.</param>
		public override void DeleteRecord(BaseRecord br)
		{
			Rec r = br as Rec;
			if (r == null) return;
			Records.Remove(r);
		}

		/// <summary>
		/// Adds a record to the plugin.
		/// </summary>
		/// <param name="br">The record to add.</param>
		/// <exception cref="TESParserException">Thrown if the type of the given record
		/// cannot be added to the plugin.</exception>
		public override void AddRecord(BaseRecord br)
		{
			Rec r = br as Rec;
			if (r == null) throw new TESParserException("Record to add was not of the correct type." +
				   Environment.NewLine + "Plugins can only hold Groups or Records.");
			Records.Add(r);
		}

		/// <summary>
		/// Loads the plugin from the given reader, optionally loading just the header.
		/// </summary>
		/// <param name="br">The reader containing the plugin to be loaded.</param>
		/// <param name="headerOnly">Whether or not to load just the header.</param>
		private void LoadPlugin(BinaryReader br, bool headerOnly)
		{
			string s;
            string TES;
			uint recsize;
			bool IsOblivion = false;

			InitDecompressor();

            // Temporary fix for TES3 plugins
			TES = ReadRecName(br);
            if (TES == "TES4")
                br.BaseStream.Position = 20;
            else if (TES == "TES3")
                br.BaseStream.Position = 16;
            else
                throw new Exception("File is not a valid TES plugin (Missing TES record)");
			
			s = ReadRecName(br);
			if (s == "HEDR")
			{
				IsOblivion = true;
			}
			else
			{
				s = ReadRecName(br);
                if (s != "HEDR") throw new Exception(string.Format("File is not a valid {0} plugin (Missing HEDR subrecord in the {0} record)", TES));
			}
			br.BaseStream.Position = 4;


            if (TES == "TES3")
            {
                Records.Add(new Record(TES, br));
            }
            else
            {
                recsize = br.ReadUInt32();
                Records.Add(new Record(TES, recsize, br, IsOblivion));
            }

			if (!headerOnly)
			{
				while (br.PeekChar() != -1)
				{
					s = ReadRecName(br);
					recsize = br.ReadUInt32();
					if (s == "GRUP") Records.Add(new GroupRecord(recsize, br, IsOblivion));
					else Records.Add(new Record(s, recsize, br, IsOblivion));
				}
			}

			CloseDecompressor();
		}

		/// <summary>
		/// Determines if the specified file is a master file.
		/// </summary>
		/// <param name="FilePath">The path to the file for which it is to be determined if it is a master file.</param>
		/// <returns><c>true</c> if the given file is a master file;
		/// <c>false</c> otherwise.</returns>
		public static bool GetIsEsm(string FilePath)
		{
			BinaryReader br = new BinaryReader(File.OpenRead(FilePath));
			try
			{
				string s = ReadRecName(br);
				if ((s != "TES4") && (s != "TES3")) return false;
				br.ReadInt32();

				if (s == "TES3")
				{
					for (int i = 0; i < 5; i++)
						br.ReadInt32();
				}

				return (br.ReadInt32() & 1) != 0;
			}
			catch
			{
				return false;
			}
			finally
			{
				br.Close();
			}
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given data.
		/// </summary>
		/// <param name="data">The plugin data.</param>
		/// <param name="name">The name of the plugin.</param>
		public TesPlugin(byte[] data, string name)
		{
			Name = name;
			BinaryReader br = new BinaryReader(new MemoryStream(data));
			try
			{
				LoadPlugin(br, false);
			}
			finally
			{
				br.Close();
			}
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given data.
		/// </summary>
		/// <param name="FilePath">The path to the plugin file.</param>
		/// <param name="headerOnly">Whether or not to only load the plugin header.</param>
		internal TesPlugin(string FilePath, bool headerOnly)
		{
			Name = Path.GetFileName(FilePath);
			FileInfo fi = new FileInfo(FilePath);
			BinaryReader br = new BinaryReader(fi.OpenRead());
			try
			{
				LoadPlugin(br, headerOnly);
			}
			finally
			{
				br.Close();
			}
		}

		/// <summary>
		/// The default constructor.
		/// </summary>
		public TesPlugin()
		{
			Name = "New plugin";
		}

		/// <summary>
		/// Gets the plugin's decription.
		/// </summary>
		/// <returns>The plugin's decription.</returns>
		public override string GetDesc()
		{
			return "[Fallout3 plugin]" + Environment.NewLine +
				"Filename: " + Name + Environment.NewLine +
				"File size: " + Size + Environment.NewLine +
				"Records: " + Records.Count;
		}

		/// <summary>
		/// Save any changes made to the plugin.
		/// </summary>
		/// <returns>The update plugin data.</returns>
		public byte[] Save()
		{
			MemoryStream ms = new MemoryStream();
			BinaryWriter bw = new BinaryWriter(ms);
			SaveData(bw);
			byte[] b = ms.ToArray();
			bw.Close();
			return b;
		}

		/// <summary>
		/// Saves any changes made to the plugin to the specified file.
		/// </summary>
		/// <remarks>
		/// If the exists it is overwritten, but the last modified time is
		///  maintained.
		/// </remarks>
		/// <param name="FilePath">The path to which to save the plugin.</param>
		internal void Save(string FilePath)
		{
			bool existed = false;
			DateTime timestamp = DateTime.Now;
			if (File.Exists(FilePath))
			{
				timestamp = new FileInfo(FilePath).LastWriteTime;
				existed = true;
				File.Delete(FilePath);
			}
			BinaryWriter bw = new BinaryWriter(File.OpenWrite(FilePath));
			try
			{
				SaveData(bw);
				Name = Path.GetFileName(FilePath);
			}
			finally
			{
				bw.Close();
			}
			try
			{
				if (existed)
				{
					new FileInfo(FilePath).LastWriteTime = timestamp;
				}
			}
			catch { }
		}

		/// <summary>
		/// Save the plugin data to the given writer.
		/// </summary>
		/// <param name="bw">The writer to which to save the data.</param>
		internal override void SaveData(BinaryWriter bw)
		{
			foreach (Rec r in Records) r.SaveData(bw);
		}

		/// <summary>
		/// Gets a list of the form ids in the plugin.
		/// </summary>
		/// <param name="lower">Whether to lower-case the ids.</param>
		/// <returns>A list of the form ids in the plugin.</returns>
		internal override List<string> GetIDs(bool lower)
		{
			List<string> list = new List<string>();
			foreach (Rec r in Records) list.AddRange(r.GetIDs(lower));
			return list;
		}

		/// <summary>
		/// CLones the plugin.
		/// </summary>
		/// <remarks>
		/// This method is not implemented.
		/// </remarks>
		/// <returns>A clone of the plugin.</returns>
		/// <exception cref="NotImplementedException">Thrown always.</exception>
		public override BaseRecord Clone()
		{
			throw new NotImplementedException("The method or operation is not implemented.");
		}

		/// <summary>
		/// Determines if the plugin contains the given form id.
		/// </summary>
		/// <param name="p_uintFormId">The form id to be searched for in the plugin.</param>
		/// <returns><c>true</c> if the plugin contains the given form id;
		/// <c>false</c> otherwise.</returns>
		public bool ContainsFormId(UInt32 p_uintFormId)
		{
			return ContainsFormId(p_uintFormId, Records);
		}

		/// <summary>
		/// Searched the given records for the given form id.
		/// </summary>
		/// <param name="p_uintFormId">The form id to be searched for.</param>
		/// <param name="p_lstRecords">The records in which to search for the given form id.</param>
		/// <returns><c>true</c> if the records contain the given form id;
		/// <c>false</c> otherwise.</returns>
		private bool ContainsFormId(uint p_uintFormId, List<Rec> p_lstRecords)
		{
			foreach (Rec rec in p_lstRecords)
			{
				if (rec is GroupRecord)
				{
					if (ContainsFormId(p_uintFormId, ((GroupRecord)rec).Records))
						return true;
				}
				else if (rec is Record)
				{
					if (((Record)rec).FormID == p_uintFormId)
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Gets the index of the specified master.
		/// </summary>
		/// <param name="p_strPluginName">The name of the master file whose index in the master list of this pugin
		/// is to be returned.</param>
		/// <returns>The index in the master list of this plugin of the specified master, or -1 if the specified
		/// master is not in the list.</returns>
		public Int32 GetMasterIndex(string p_strPluginName)
		{
			IList<string> lstMaster = Masters;
			for (Int32 i = 0; i < lstMaster.Count; i++)
				if (lstMaster[i].ToLowerInvariant().Equals(p_strPluginName.ToLowerInvariant()))
					return i;
			return -1;
		}

		/// <summary>
		/// Gets the master of the plugin at the given index.
		/// </summary>
		/// <param name="p_intIndex">The index of the master to return.</param>
		/// <returns>The master of the plugin at the given index.</returns>
		public string GetMaster(Int32 p_intIndex)
		{
			IList<string> lstMasters = Masters;
			if ((p_intIndex < 0) || (p_intIndex >= lstMasters.Count))
				return null;
			return lstMasters[p_intIndex];
		}
	}

	/// <summary>
	/// Encapsulates interacting with a plugin record that has a description.
	/// </summary>
	public abstract class Rec : BaseRecord
	{
		/// <summary>
		/// The descriptive name of the record.
		/// </summary>
		public string descriptiveName;

		/// <summary>
		/// Gets the descriptive name of the record.
		/// </summary>
		/// <value>The descriptive name of the record.</value>
		public string DescriptiveName { get { return descriptiveName == null ? Name : (Name + descriptiveName); } }
	}

	/// <summary>
	/// Encapsulates interacting with a record that is a group of other records.
	/// </summary>
	public sealed class GroupRecord : Rec
	{
		/// <summary>
		/// The records in the group.
		/// </summary>
		public readonly List<Rec> Records = new List<Rec>();
		private readonly byte[] data;

		/// <summary>
		/// The type of the group.
		/// </summary>
		public uint groupType;

		/// <summary>
		/// The modified time of the group.
		/// </summary>
		public uint dateStamp;

		/// <summary>
		/// The group flags.
		/// </summary>
		public uint flags;

		/// <summary>
		/// Gets the type of contents in the group.
		/// </summary>
		/// <value>The type of contents in the group.</value>
		public string ContentsType
		{
			get { return "" + (char)data[0] + (char)data[1] + (char)data[2] + (char)data[3]; }
		}

		/// <summary>
		/// Gets the size of the group record.
		/// </summary>
		/// <value>The size of the group record.</value>
		public override long Size
		{
			get { long size = 24; foreach (Rec rec in Records) size += rec.Size2; return size; }
		}

		/// <summary>
		/// Gets the size of the group record.
		/// </summary>
		/// <value>The size of the group record.</value>
		public override long Size2 { get { return Size; } }

		/// <summary>
		/// Deletes the specified sub-record from the record.
		/// </summary>
		/// <param name="br">The record to delete.</param>
		public override void DeleteRecord(BaseRecord br)
		{
			Rec r = br as Rec;
			if (r == null) return;
			Records.Remove(r);
		}

		/// <summary>
		/// Adds the given sub-record to the record.
		/// </summary>
		/// <param name="br">The record to add.</param>
		public override void AddRecord(BaseRecord br)
		{
			Rec r = br as Rec;
			if (r == null) throw new TESParserException("Record to add was not of the correct type." +
				   Environment.NewLine + "Groups can only hold records or other groups.");
			Records.Add(r);
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="Size">The size of the groop record.</param>
		/// <param name="br">The reader caontaining the group record data.</param>
		/// <param name="Oblivion">Whether the record is in Oblivion format.</param>
		internal GroupRecord(uint Size, BinaryReader br, bool Oblivion)
		{
			Name = "GRUP";
			data = br.ReadBytes(4);
			groupType = br.ReadUInt32();
			dateStamp = br.ReadUInt32();
			if (!Oblivion) flags = br.ReadUInt32();
			uint AmountRead = 0;
			while (AmountRead < Size - (Oblivion ? 20 : 24))
			{
				string s = TesPlugin.ReadRecName(br);
				uint recsize = br.ReadUInt32();
				if (s == "GRUP")
				{
					GroupRecord gr = new GroupRecord(recsize, br, Oblivion);
					AmountRead += recsize;
					Records.Add(gr);
				}
				else
				{
					Record r = new Record(s, recsize, br, Oblivion);
					AmountRead += (uint)(recsize + (Oblivion ? 20 : 24));
					Records.Add(r);
				}
			}
			if (AmountRead > (Size - (Oblivion ? 20 : 24)))
			{
				throw new TESParserException("Record block did not match the size specified in the group header");
			}
			if (groupType == 0)
			{
				descriptiveName = " (" + (char)data[0] + (char)data[1] + (char)data[2] + (char)data[3] + ")";
			}
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given data.
		/// </summary>
		/// <param name="data">The group record data.</param>
		public GroupRecord(string data)
		{
			Name = "GRUP";
			this.data = new byte[4];
			for (int i = 0; i < 4; i++) this.data[i] = (byte)data[i];
			descriptiveName = " (" + data + ")";
		}

		/// <summary>
		/// The copy constructor.
		/// </summary>
		/// <param name="gr">The group record to copy.</param>
		private GroupRecord(GroupRecord gr)
		{
			Name = "GRUP";
			data = (byte[])gr.data.Clone();
			groupType = gr.groupType;
			dateStamp = gr.dateStamp;
			flags = gr.flags;
			Records = new List<Rec>(gr.Records.Count);
			for (int i = 0; i < gr.Records.Count; i++) Records.Add((Rec)gr.Records[i].Clone());
			Name = gr.Name;
			descriptiveName = gr.descriptiveName;
		}

		private string GetSubDesc()
		{
			switch (groupType)
			{
				case 0:
					return "(Contains: " + (char)data[0] + (char)data[1] + (char)data[2] + (char)data[3] + ")";
				case 2:
				case 3:
					return "(Block number: " + (data[0] + data[1] * 256 + data[2] * 256 * 256 + data[3] * 256 * 256 * 256).ToString() + ")";
				case 4:
				case 5:
					return "(Coordinates: [" + (data[0] + data[1] * 256) + ", " + data[2] + data[3] * 256 + "])";
				case 1:
				case 6:
				case 7:
				case 8:
				case 9:
				case 10:
					return "(Parent FormID: 0x" + data[3].ToString("x2") + data[2].ToString("x2") + data[1].ToString("x2") + data[0].ToString("x2") + ")";
			}
			return null;
		}

		/// <summary>
		/// Gets the description of the record.
		/// </summary>
		/// <returns>The description of the record.</returns>
		public override string GetDesc()
		{
			string desc = "[Record group]" + Environment.NewLine + "Record type: ";
			switch (groupType)
			{
				case 0:
					desc += "Top " + GetSubDesc();
					break;
				case 1:
					desc += "World children " + GetSubDesc();
					break;
				case 2:
					desc += "Interior Cell Block " + GetSubDesc();
					break;
				case 3:
					desc += "Interior Cell Sub-Block " + GetSubDesc();
					break;
				case 4:
					desc += "Exterior Cell Block " + GetSubDesc();
					break;
				case 5:
					desc += "Exterior Cell Sub-Block " + GetSubDesc();
					break;
				case 6:
					desc += "Cell Children " + GetSubDesc();
					break;
				case 7:
					desc += "Topic Children " + GetSubDesc();
					break;
				case 8:
					desc += "Cell Persistent Childen " + GetSubDesc();
					break;
				case 9:
					desc += "Cell Temporary Children " + GetSubDesc();
					break;
				case 10:
					desc += "Cell Visible Distant Children " + GetSubDesc();
					break;
				default:
					desc += "Unknown";
					break;
			}
			return desc + Environment.NewLine +
				"Records: " + Records.Count.ToString() + Environment.NewLine +
				"Size: " + Size.ToString() + " bytes (including header)";
		}

		internal override void SaveData(BinaryWriter bw)
		{
			WriteString(bw, "GRUP");
			bw.Write((uint)Size);
			bw.Write(data);
			bw.Write(groupType);
			bw.Write(dateStamp);
			bw.Write(flags);
			foreach (Rec r in Records) r.SaveData(bw);
		}

		/// <summary>
		/// Gets the ids of the sub-records in the record.
		/// </summary>
		/// <param name="lower">Whether or not to lower-case the returned ids.</param>
		/// <returns>The ids of the sub-records in the record.</returns>
		internal override List<string> GetIDs(bool lower)
		{
			List<string> list = new List<string>();
			foreach (Record r in Records) list.AddRange(r.GetIDs(lower));
			return list;
		}

		/// <summary>
		/// Clones the record.
		/// </summary>
		/// <returns>A clone of the record.</returns>
		public override BaseRecord Clone()
		{
			return new GroupRecord(this);
		}

		/// <summary>
		/// Gets the record's data.
		/// </summary>
		/// <returns>The record's data.</returns>
		public byte[] GetData() { return (byte[])data.Clone(); }
		internal byte[] GetReadonlyData() { return data; }
		
		/// <summary>
		/// Sets the record's data.
		/// </summary>
		/// <param name="data">The new record data.</param>
		public void SetData(byte[] data)
		{
			if (data.Length != 4) throw new ArgumentException("data length must be 4");
			for (int i = 0; i < 4; i++) this.data[i] = data[i];
		}
	}

	/// <summary>
	/// Encapsulates working with a plugin record that represents a TES form.
	/// </summary>
	public sealed class Record : Rec
	{
		/// <summary>
		/// The record's sub-records.
		/// </summary>
		public readonly List<SubRecord> SubRecords = new List<SubRecord>();

		/// <summary>
		/// The flags of the record.
		/// </summary>
		public uint Flags1;

		/// <summary>
		/// The flags of the record.
		/// </summary>
		public uint Flags2;

		/// <summary>
		/// The flags of the record.
		/// </summary>
		public uint Flags3;
		
		/// <summary>
		/// The from id of the record.
		/// </summary>
		public uint FormID;

		/// <summary>
		/// Gets the size of the record without header.
		/// </summary>
		/// <value>The size of the record without header.</value>
		public override long Size
		{
			get
			{
				long size = 0;
				foreach (SubRecord rec in SubRecords) size += rec.Size2;
				return size;
			}
		}

		/// <summary>
		/// Gets the size of the record with header.
		/// </summary>
		/// <value>The size of the record with header.</value>
		public override long Size2
		{
			get
			{
				long size = 24;
				foreach (SubRecord rec in SubRecords) size += rec.Size2;
				return size;
			}
		}

		/// <summary>
		/// Deletes the specified sub-record from the record.
		/// </summary>
		/// <param name="br">The record to delete.</param>
		public override void DeleteRecord(BaseRecord br)
		{
			SubRecord sr = br as SubRecord;
			if (sr == null) return;
			SubRecords.Remove(sr);
		}

		/// <summary>
		/// Adds the given sub-record to the record.
		/// </summary>
		/// <param name="br">The record to add.</param>
		public override void AddRecord(BaseRecord br)
		{
			SubRecord sr = br as SubRecord;
			if (sr == null) throw new TESParserException("Record to add was not of the correct type." +
				   Environment.NewLine + "Records can only hold Subrecords.");
			SubRecords.Add(sr);
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="name">The name of the record.</param>
		/// <param name="Size">The size of the record.</param>
		/// <param name="br">The reader containing the record data.</param>
		/// <param name="Oblivion">Whether the recrod is in Oblivion format.</param>
		internal Record(string name, uint Size, BinaryReader br, bool Oblivion)
		{
			Name = name;
			Flags1 = br.ReadUInt32();
			FormID = br.ReadUInt32();
			Flags2 = br.ReadUInt32();
			if (!Oblivion) Flags3 = br.ReadUInt32();
			if ((Flags1 & 0x00040000) > 0)
			{
				Flags1 ^= 0x00040000;
				uint newSize = br.ReadUInt32();
				br = Decompress(br, (int)(Size - 4), (int)newSize);
				Size = newSize;
			}
			uint AmountRead = 0;
			while (AmountRead < Size)
			{
				string s = ReadRecName(br);
				uint i = 0;
				if (s == "XXXX")
				{
					br.ReadUInt16();
					i = br.ReadUInt32();
					s = ReadRecName(br);
				}
				SubRecord r = new SubRecord(s, br, i);
				AmountRead += (uint)(r.Size2);
				SubRecords.Add(r);
			}
			if (AmountRead > Size)
			{
				throw new TESParserException("Subrecord block did not match the size specified in the record header");
			}

			//br.BaseStream.Position+=Size;
			if (SubRecords.Count > 0 && SubRecords[0].Name == "EDID") descriptiveName = " (" + SubRecords[0].GetStrData() + ")";
		}

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="name">The name of the record.</param>
        /// <param name="br">The reader containing the record data.</param>
        internal Record(string name, BinaryReader br)
        {
            Name = name;
            br.BaseStream.Position += 12;
            uint AmountRead = 0;

            string s = ReadRecName(br);
            if (s == "HEDR")
            {
                br.BaseStream.Position += 10;
                SubRecord r = new SubRecord("CNAM", br, 32);
                AmountRead += (uint)(r.Size2);
                SubRecords.Add(r);
                br.BaseStream.Position -= 2;
                r = new SubRecord("SNAM", br, 256);
                AmountRead += (uint)(r.Size2);
                SubRecords.Add(r);
                br.BaseStream.Position += 4;
                s = ReadRecName(br);
                while (s == "MAST")
                {
                    r = new SubRecord(s, br, br.ReadUInt16());
                    SubRecords.Add(r);
                    br.BaseStream.Position += 16;
                    s = ReadRecName(br);
                }
            }
        }

		/// <summary>
		/// The copy constructor.
		/// </summary>
		/// <param name="r">The record to copy.</param>
		private Record(Record r)
		{
			SubRecords = new List<SubRecord>(r.SubRecords.Count);
			for (int i = 0; i < r.SubRecords.Count; i++) SubRecords.Add((SubRecord)r.SubRecords[i].Clone());
			Flags1 = r.Flags1;
			Flags2 = r.Flags2;
			Flags3 = r.Flags3;
			FormID = r.FormID;
			Name = r.Name;
			descriptiveName = r.descriptiveName;
		}

		/// <summary>
		/// The default constructor.
		/// </summary>
		public Record()
		{
			Name = "NEW_";
		}

		/// <summary>
		/// Clones the record.
		/// </summary>
		/// <returns>A clone of the record.</returns>
		public override BaseRecord Clone()
		{
			return new Record(this);
		}

		private string GetBaseDesc()
		{
			return "Type: " + Name + Environment.NewLine +
				"FormID: " + FormID.ToString("x8") + Environment.NewLine +
				"Flags 1: " + Flags1.ToString("x8") +
				(Flags1 == 0 ? "" : " (" + FlagDefs.GetRecFlags1Desc(Flags1) + ")") +
				Environment.NewLine +
				"Flags 2: " + Flags2.ToString("x8") + Environment.NewLine +
				"Flags 3: " + Flags3.ToString("x8") + Environment.NewLine +
				"Subrecords: " + SubRecords.Count.ToString() + Environment.NewLine +
				"Size: " + Size.ToString() + " bytes (excluding header)";
		}

		private string GetExtendedDesc(SubrecordStructure[] sss, dFormIDLookupI formIDLookup)
		{
			if (sss == null) return null;
			string s = RecordStructure.Records[Name].description + Environment.NewLine;
			for (int i = 0; i < sss.Length; i++)
			{
				if (sss[i].elements == null) return s;
				if (sss[i].notininfo) continue;
				s += Environment.NewLine + SubRecords[i].GetFormattedData(sss[i], formIDLookup);
			}
			return s;
		}

		/// <summary>
		/// Gets the record's description.
		/// </summary>
		/// <returns>The record's description.</returns>
		public override string GetDesc()
		{
			return "[Record]" + Environment.NewLine + GetBaseDesc();
		}


		internal string GetDesc(SubrecordStructure[] sss, dFormIDLookupI formIDLookup)
		{
			string start = "[Record]" + Environment.NewLine + GetBaseDesc();
			string end;
			try
			{
				end = GetExtendedDesc(sss, formIDLookup);
			}
			catch
			{
				end = "Warning: An error occured while processing the record. It may not conform to the strucure defined in RecordStructure.xml";
			}
			if (end == null) return start;
			else return start + Environment.NewLine + Environment.NewLine + "[Formatted information]" + Environment.NewLine + end;
		}

		internal override void SaveData(BinaryWriter bw)
		{
			WriteString(bw, Name);
			bw.Write((uint)Size);
			bw.Write(Flags1);
			bw.Write(FormID);
			bw.Write(Flags2);
			bw.Write(Flags3);
			foreach (SubRecord sr in SubRecords) sr.SaveData(bw);
		}

		/// <summary>
		/// Gets the ids of the sub-records in the record.
		/// </summary>
		/// <param name="lower">Whether or not to lower-case the returned ids.</param>
		/// <returns>The ids of the sub-records in the record.</returns>
		internal override List<string> GetIDs(bool lower)
		{
			List<string> list = new List<string>();
			foreach (SubRecord sr in SubRecords) list.AddRange(sr.GetIDs(lower));
			return list;
		}
	}

	/// <summary>
	/// Encapsulates insteracting with sub-records.
	/// </summary>
	public sealed class SubRecord : BaseRecord
	{
		private byte[] Data;

		/// <summary>
		/// Gets the size of the record without header.
		/// </summary>
		/// <value>The size of the record without header.</value>
		public override long Size { get { return Data.Length; } }
		
		/// <summary>
		/// Gets the size of the record with header.
		/// </summary>
		/// <value>The size of the record with header.</value>
		public override long Size2 { get { return 6 + Data.Length + (Data.Length > ushort.MaxValue ? 10 : 0); } }

		/// <summary>
		/// Gets the sub-record's data.
		/// </summary>
		/// <returns>The sub-record's data.</returns>
		public byte[] GetData()
		{
			return (byte[])Data.Clone();
		}

		internal byte[] GetReadonlyData() { return Data; }
		
		/// <summary>
		/// Sets the sub-record's data.
		/// </summary>
		/// <param name="data">The new data.</param>
		public void SetData(byte[] data)
		{
			Data = (byte[])data.Clone();
		}

		/// <summary>
		/// Sets the sub-record's data tot he given string.
		/// </summary>
		/// <param name="s">The string to which to set the sub-record's data.</param>
		/// <param name="nullTerminate">Whether to nul-terminate the given string.</param>
		public void SetStrData(string s, bool nullTerminate)
		{
			if (nullTerminate) s += '\0';
			Data = System.Text.Encoding.Default.GetBytes(s);
		}

		/// <summary>
		/// A simple constructor that initializes the object with ther given values.
		/// </summary>
		/// <param name="name">The name of the sub record.</param>
		/// <param name="br">The reader containing the sub-record's data.</param>
		/// <param name="size">The size of the sub-recrod.</param>
		internal SubRecord(string name, BinaryReader br, uint size)
		{
			Name = name;
			if (size == 0) size = br.ReadUInt16(); else br.BaseStream.Position += 2;
			Data = new byte[size];
			br.Read(Data, 0, Data.Length);
		}

		/// <summary>
		/// The copy constructor.
		/// </summary>
		/// <param name="sr">The sub-record to copy.</param>
		private SubRecord(SubRecord sr)
		{
			Name = sr.Name;
			Data = (byte[])sr.Data.Clone();
		}

		/// <summary>
		/// Clones the record.
		/// </summary>
		/// <returns>A clone of the record.</returns>
		public override BaseRecord Clone()
		{
			return new SubRecord(this);
		}

		/// <summary>
		/// The default constructor.
		/// </summary>
		public SubRecord()
		{
			Name = "NEW_";
			Data = new byte[0];
		}

		internal override void SaveData(BinaryWriter bw)
		{
			if (Data.Length > ushort.MaxValue)
			{
				WriteString(bw, "XXXX");
				bw.Write((ushort)4);
				bw.Write(Data.Length);
				WriteString(bw, Name);
				bw.Write((ushort)0);
				bw.Write(Data, 0, Data.Length);
			}
			else
			{
				WriteString(bw, Name);
				bw.Write((ushort)Data.Length);
				bw.Write(Data, 0, Data.Length);
			}
		}

		/// <summary>
		/// Gets the sub-record's description.
		/// </summary>
		/// <returns>The sub-record's description.</returns>
		public override string GetDesc()
		{
			return "[Subrecord]" + Environment.NewLine +
				"Name: " + Name + Environment.NewLine +
				"Size: " + Size.ToString() + " bytes (Excluding header)";
		}

		/// <summary>
		/// Deletes the specified sub-record from the record.
		/// </summary>
		/// <param name="br">The record to delete.</param>
		public override void DeleteRecord(BaseRecord br) { }

		/// <summary>
		/// Adds the given sub-record to the record.
		/// </summary>
		/// <param name="br">The record to add.</param>
		public override void AddRecord(BaseRecord br)
		{
			throw new TESParserException("Subrecords cannot contain additional data.");
		}

		/// <summary>
		/// Gets the sub-record's data as a string.
		/// </summary>
		/// <returns>The sub-record's data as a string.</returns>
		public string GetStrData()
		{
			string s = "";
			foreach (byte b in Data)
			{
				if (b == 0) break;
				s += (char)b;
			}
			return s;
		}

		/// <summary>
		/// Gets the sub-record's data as a hex number.
		/// </summary>
		/// <returns>The sub-record's data as a hex number.</returns>
		public string GetHexData()
		{
			string s = "";
			foreach (byte b in Data) s += b.ToString("X").PadLeft(2, '0') + " ";
			return s;
		}

		internal string GetFormattedData(SubrecordStructure ss, dFormIDLookupI formIDLookup)
		{
			int offset = 0;
			string s = ss.name + " (" + ss.desc + ")" + Environment.NewLine;
			try
			{
				for (int j = 0; j < ss.elements.Length; j++)
				{
					if (offset == Data.Length && j == ss.elements.Length - 1 && ss.elements[j].optional) break;
					string s2 = "";
					if (!ss.elements[j].notininfo) s2 += ss.elements[j].name + ": ";
					switch (ss.elements[j].type)
					{
						case ElementValueType.Int:
							string tmps = TypeConverter.h2si(Data[offset], Data[offset + 1], Data[offset + 2], Data[offset + 3]).ToString();
							if (!ss.elements[j].notininfo)
							{
								if (ss.elements[j].hexview) s2 += TypeConverter.h2i(Data[offset], Data[offset + 1], Data[offset + 2], Data[offset + 3]).ToString("X8");
								else s2 += tmps;
								if (ss.elements[j].options != null)
								{
									for (int k = 0; k < ss.elements[j].options.Length; k += 2)
									{
										if (tmps == ss.elements[j].options[k + 1]) s2 += " (" + ss.elements[j].options[k] + ")";
									}
								}
								else if (ss.elements[j].flags != null)
								{
									uint val = TypeConverter.h2i(Data[offset], Data[offset + 1], Data[offset + 2], Data[offset + 3]);
									string tmp2 = "";
									for (int k = 0; k < ss.elements[j].flags.Length; k++)
									{
										if ((val & (1 << k)) != 0)
										{
											if (tmp2.Length > 0) tmp2 += ", ";
											tmp2 += ss.elements[j].flags[k];
										}
									}
									if (tmp2.Length > 0) s2 += " (" + tmp2 + ")";
								}
							}
							offset += 4;
							break;
						case ElementValueType.Short:
							tmps = TypeConverter.h2ss(Data[offset], Data[offset + 1]).ToString();
							if (!ss.elements[j].notininfo)
							{
								if (ss.elements[j].hexview) s2 += TypeConverter.h2ss(Data[offset], Data[offset + 1]).ToString("X4");
								else s2 += tmps;
								if (ss.elements[j].options != null)
								{
									for (int k = 0; k < ss.elements[j].options.Length; k += 2)
									{
										if (tmps == ss.elements[j].options[k + 1]) s2 += " (" + ss.elements[j].options[k] + ")";
									}
								}
								else if (ss.elements[j].flags != null)
								{
									uint val = TypeConverter.h2s(Data[offset], Data[offset + 1]);
									string tmp2 = "";
									for (int k = 0; k < ss.elements[j].flags.Length; k++)
									{
										if ((val & (1 << k)) != 0)
										{
											if (tmp2.Length > 0) tmp2 += ", ";
											tmp2 += ss.elements[j].flags[k];
										}
									}
									if (tmp2.Length > 0) s2 += " (" + tmp2 + ")";
								}
							}
							offset += 2;
							break;
						case ElementValueType.Byte:
							tmps = Data[offset].ToString();
							if (!ss.elements[j].notininfo)
							{
								if (ss.elements[j].hexview) s2 += Data[offset].ToString("X2");
								else s2 += tmps;
								if (ss.elements[j].options != null)
								{
									for (int k = 0; k < ss.elements[j].options.Length; k += 2)
									{
										if (tmps == ss.elements[j].options[k + 1]) s2 += " (" + ss.elements[j].options[k] + ")";
									}
								}
								else if (ss.elements[j].flags != null)
								{
									int val = Data[offset];
									string tmp2 = "";
									for (int k = 0; k < ss.elements[j].flags.Length; k++)
									{
										if ((val & (1 << k)) != 0)
										{
											if (tmp2.Length > 0) tmp2 += ", ";
											tmp2 += ss.elements[j].flags[k];
										}
									}
									if (tmp2.Length > 0) s2 += " (" + tmp2 + ")";
								}
							}
							offset++;
							break;
						case ElementValueType.FormID:
							uint id = TypeConverter.h2i(Data[offset], Data[offset + 1], Data[offset + 2], Data[offset + 3]);
							if (!ss.elements[j].notininfo) s2 += id.ToString("X8");
							if (formIDLookup != null) s2 += ": " + formIDLookup(id);
							offset += 4;
							break;
						case ElementValueType.Float:
							if (!ss.elements[j].notininfo) s2 += TypeConverter.h2f(Data[offset], Data[offset + 1], Data[offset + 2], Data[offset + 3]).ToString();
							offset += 4;
							break;
						case ElementValueType.String:
							if (!ss.elements[j].notininfo)
							{
								while (Data[offset] != 0) s2 += (char)Data[offset++];
							}
							else
							{
								while (Data[offset] != 0) offset++;
							}
							offset++;
							break;
						case ElementValueType.fstring:
							s2 += GetStrData();
							break;
						case ElementValueType.Blob:
							s2 += GetHexData();
							break;
						default:
							throw new ApplicationException();
					}
					if (!ss.elements[j].notininfo) s2 += Environment.NewLine;
					if (offset < Data.Length && j == ss.elements.Length - 1 && ss.elements[j].repeat) j--;
					s += s2;
				}
			}
			catch
			{
				s += "Warning: Subrecord doesn't seem to match the expected structure" + Environment.NewLine;
			}
			return s;
		}


		/// <summary>
		/// Gets the ids of the sub-records in the record.
		/// </summary>
		/// <param name="lower">Whether or not to lower-case the returned ids.</param>
		/// <returns>The ids of the sub-records in the record.</returns>
		internal override List<string> GetIDs(bool lower)
		{
			List<string> list = new List<string>();
			if (Name == "EDID")
			{
				if (lower)
				{
					list.Add(this.GetStrData().ToLower());
				}
				else
				{
					list.Add(this.GetStrData());
				}
			}
			return list;
		}
	}

	internal static class FlagDefs
	{
		public static readonly string[] RecFlags1 = {
            "ESM file",
            null,
            null,
            null,
            null,
            "Deleted",
            null,
            null,
            null,
            "Casts shadows",
            "Quest item / Persistent reference",
            "Initially disabled",
            "Ignored",
            null,
            null,
            "Visible when distant",
            null,
            "Dangerous / Off limits (Interior cell)",
            "Data is compressed",
            "Can't wait",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
        };

		public static string GetRecFlags1Desc(uint flags)
		{
			string desc = "";
			bool b = false;
			for (int i = 0; i < 32; i++)
			{
				if ((flags & (uint)(1 << i)) > 0)
				{
					if (b) desc += ", ";
					b = true;
					desc += (RecFlags1[i] == null ? "Unknown (" + ((uint)(1 << i)).ToString("x") + ")" : RecFlags1[i]);
				}
			}
			return desc;
		}
	}
}
