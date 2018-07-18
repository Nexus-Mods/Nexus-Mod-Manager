using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Windows.Forms;
using System.IO;
using System.Reflection;

namespace Nexus.Client.Games.Gamebryo.Tools.TESsnip.ScriptCompiler
{
	struct Pair<A, B>
	{
		public A a;
		public B b;

		public Pair(A a, B b) { this.a = a; this.b = b; }

		public A Key { get { return a; } set { a = value; } }
		public B Value { get { return b; } set { b = value; } }

		public override string ToString()
		{
			return a.ToString();
		}
	}

	/// <summary>
	/// Compiles scripts for TES plugin files.
	/// </summary>
	public static class ScriptCompiler
	{
		#region Setup
		private enum VarType { Int, Float, Ref, String, Axis, Enum, Short, None }
		private class FunctionSig
		{
			public readonly ushort opcode;
			public readonly VarType ret;
			public readonly VarType[] args;
			public readonly string[] reftypes;
			public readonly string[] argdescs;
			public readonly int requiredArgs;
			public readonly string desc;
			public readonly bool allowref;
			public readonly bool fose;
			public readonly int paddingbytes;
			public readonly bool skipArgs;

			public override string ToString()
			{
				return ret.ToString() + " func(" + args.Length + ")";
			}

			private static VarType GetVarType(string s)
			{
				switch (s)
				{
					case "int": return VarType.Int;
					case "float": return VarType.Float;
					case "ref": return VarType.Ref;
					case "string": return VarType.String;
					case "axis": return VarType.Axis;
					case "enum": return VarType.Enum;
					case "short": return VarType.Short;
					default: throw new RecordXmlException("Invalid variable type: " + s);
				}
			}
			public FunctionSig(XmlNode n, bool block)
			{
				opcode = ushort.Parse(n.Attributes.GetNamedItem("opcode").Value, System.Globalization.NumberStyles.AllowHexSpecifier, null);
				XmlNode n2;
				n2 = n.Attributes.GetNamedItem("desc");
				if (n2 == null) desc = "";
				else desc = n2.Value;
				n2 = n.Attributes.GetNamedItem("fose");
				if (n2 != null && n2.Value == "true") fose = true;
				else fose = false;
				n2 = n.Attributes.GetNamedItem("skipargs");
				if (n2 != null && n2.Value == "true") skipArgs = true;
				else skipArgs = false;
				if (!block)
				{
					n2 = n.Attributes.GetNamedItem("ret");
					if (n2 == null) ret = VarType.None;
					else ret = GetVarType(n2.Value);
					n2 = n.Attributes.GetNamedItem("allowref");
					if (n2 != null && n2.Value == "false") allowref = false;
					else allowref = true;
					n2 = n.Attributes.GetNamedItem("paddingbytes");
					if (n2 != null) paddingbytes = byte.Parse(n2.Value);
				}
				args = new VarType[n.ChildNodes.Count];
				reftypes = new string[n.ChildNodes.Count];
				argdescs = new string[n.ChildNodes.Count];
				for (int i = 0; i < n.ChildNodes.Count; i++)
				{
					n2 = n.ChildNodes[i];
					args[i] = GetVarType(n2.Attributes.GetNamedItem("type").Value);
					argdescs[i] = n2.Attributes.GetNamedItem("name").Value;
					if (args[i] == VarType.Ref)
					{
						n2 = n2.Attributes.GetNamedItem("reftype");
						if (n2 != null) reftypes[i] = n2.Value;
					}
					else if (args[i] == VarType.Enum)
					{
						reftypes[i] = n2.Attributes.GetNamedItem("enumtype").Value;
					}

					if (block && (args[i] != VarType.Ref && args[i] != VarType.Short && args[i] != VarType.Int)) throw new RecordXmlException("Arguments to block types must be short, int or ref");
				}
				n2 = n.Attributes.GetNamedItem("requiredargs");
				if (n2 == null) requiredArgs = args.Length;
				else requiredArgs = int.Parse(n2.Value);
			}
		}

		private struct Operator
		{
			public readonly string end;
			public readonly int precedence;
			//public readonly bool rtl;
			public readonly byte[] opcode;

			public Operator(string end, int precedence, bool rtl, string opcode)
			{
				this.end = end;
				this.precedence = precedence;
				//this.rtl=rtl;
				this.opcode = System.Text.Encoding.ASCII.GetBytes(opcode);
			}
		}

		private static bool Inited;
		private static readonly Dictionary<string, Dictionary<string, ushort>> enumList = new Dictionary<string, Dictionary<string, ushort>>();
		private static readonly Dictionary<string, FunctionSig> blockList = new Dictionary<string, FunctionSig>();
		private static readonly Dictionary<string, FunctionSig> functionList = new Dictionary<string, FunctionSig>();
		private static readonly Dictionary<string, Operator> biOps = new Dictionary<string, Operator>();
		private static readonly Dictionary<string, Operator> uniOps = new Dictionary<string, Operator>();

		private static readonly Dictionary<string, uint> globals = new Dictionary<string, uint>();
		private static readonly Dictionary<string, Pair<uint, string>> edidList = new Dictionary<string, Pair<uint, string>>();
		private static readonly Dictionary<string, Dictionary<string, ushort>> farVars = new Dictionary<string, Dictionary<string, ushort>>();

		private static void AddFunction(string name, string sname, FunctionSig sig)
		{
			name = name.ToLowerInvariant();
			if (functionList.ContainsKey(name))
				MessageBox.Show("Function " + name + " has already been added.");
			functionList.Add(name, sig);
			TokenStream.AddFunction(name);
			if (sname != null)
			{
				sname = sname.ToLowerInvariant();
				if (functionList.ContainsKey(sname))
					MessageBox.Show("Function short name " + sname + " has already been added.");
				functionList.Add(sname, sig);
				TokenStream.AddFunction(sname);
			}
		}
		private static void Init()
		{
			Inited = true;

			biOps.Add("*", new Operator(null, 4, false, "*"));
			biOps.Add("/", new Operator(null, 4, false, "/"));
			biOps.Add("+", new Operator(null, 5, false, "+"));
			biOps.Add("-", new Operator(null, 5, false, "-"));
			biOps.Add("<", new Operator(null, 7, false, "<"));
			biOps.Add("<=", new Operator(null, 7, false, "<="));
			biOps.Add(">=", new Operator(null, 7, false, ">="));
			biOps.Add(">", new Operator(null, 7, false, ">"));
			biOps.Add("==", new Operator(null, 8, false, "=="));
			biOps.Add("!=", new Operator(null, 8, false, "!="));
			biOps.Add("&&", new Operator(null, 12, false, "&&"));
			biOps.Add("||", new Operator(null, 13, false, "||"));

			//rtl should be false for all uni ops
			uniOps.Add("-", new Operator(null, 3, false, "~"));

			try
			{
				XmlDocument doc = new XmlDocument();

				Assembly asmRecordStructure = Assembly.GetExecutingAssembly();
				Stream stream = asmRecordStructure.GetManifestResourceStream("Nexus.Client.Games.Fallout3.Tools.TESsnip.ScriptCompiler.ScriptFunctions.xml");
				doc.Load(stream);

				XmlNode root = null;
				foreach (XmlNode n in doc.ChildNodes)
				{
					if (n.NodeType == XmlNodeType.Element)
					{
						root = n;
						break;
					}
				}
				if (root == null)
				{
					throw new RecordXmlException("Root node was missing");
				}
				XmlNode n2;
				string sname;
				foreach (XmlNode n in root.ChildNodes)
				{
					if (n.NodeType == XmlNodeType.Comment) continue;
					if (n.Name == "Enum")
					{
						Dictionary<string, ushort> Enum = new Dictionary<string, ushort>();
						foreach (XmlNode n3 in n.ChildNodes)
						{
							if (n3.NodeType == XmlNodeType.Comment) continue;
							if (n3.Name != "Element") throw new RecordXmlException("Expected Element");
							Enum[n3.Attributes.GetNamedItem("name").Value.ToLowerInvariant()] = ushort.Parse(n3.Attributes.GetNamedItem("value").Value);
						}
						enumList[n.Attributes.GetNamedItem("name").Value] = Enum;
					}
					else if (n.Name == "Func")
					{
						n2 = n.Attributes.GetNamedItem("short");
						if (n2 != null) sname = n2.Value; else sname = null;
						AddFunction(n.Attributes.GetNamedItem("name").Value, sname, new FunctionSig(n, false));
					}
					else if (n.Name == "Block")
					{
						blockList[n.Attributes.GetNamedItem("name").Value.ToLowerInvariant()] = new FunctionSig(n, true);
					}
					else
					{
						throw new RecordXmlException("Expected Enum, Function or Block");
					}
				}
			}
			catch (Exception ex)
			{
				System.Windows.Forms.MessageBox.Show("An error occured while parsing ScriptFunctions.xml.\n" + ex);
			}
		}

		private static void RecursePlugin(Rec r, uint mask, uint id, Dictionary<uint, Record> records, List<Pair<uint, Record>> quests, List<Pair<uint, Record>> refs)
		{
			if (r is Record)
			{
				Record r2 = (Record)r;
				if (r2.descriptiveName == null) return;
				if ((r2.FormID & 0xff000000) != mask || r2.descriptiveName == null) return;
				records[(r2.FormID & 0xffffff) + id] = r2;

				if (r2.Name == "QUST")
				{
					foreach (SubRecord sr in r2.SubRecords)
					{
						if (sr.Name == "SCRI")
						{
							byte[] bytes = sr.GetReadonlyData();
							uint formid = (TypeConverter.h2i(bytes[0], bytes[1], bytes[2], bytes[3]));
							if ((formid & 0xff000000) != mask) return;
							quests.Add(new Pair<uint, Record>((formid & 0xffffff) + id, r2));
						}
					}
				}
				else if (r2.Name == "REFR" || r2.Name == "ACHR" || r2.Name == "ACRE")
				{
					if (r2.SubRecords.Count > 2 && r2.SubRecords[0].Name == "EDID" && r2.SubRecords[1].Name == "NAME")
					{
						byte[] bytes = r2.SubRecords[1].GetReadonlyData();
						uint formid = (TypeConverter.h2i(bytes[0], bytes[1], bytes[2], bytes[3]));
						if ((formid & 0xff000000) != mask) return;
						refs.Add(new Pair<uint, Record>((formid & 0xffffff) + id, r2));
					}
				}
			}
			else
			{
				foreach (Rec r2 in ((GroupRecord)r).Records) RecursePlugin(r2, mask, id, records, quests, refs);
			}
		}
		private static void RecursePlugin(Rec r, Dictionary<uint, Record> records, List<Pair<uint, Record>> quests, List<Pair<uint, Record>> refs)
		{
			if (r is Record)
			{
				Record r2 = (Record)r;
				if (r2.descriptiveName == null) return;
				records[r2.FormID] = r2;

				if (r2.Name == "QUST")
				{
					foreach (SubRecord sr in r2.SubRecords)
					{
						if (sr.Name == "SCRI")
						{
							byte[] bytes = sr.GetReadonlyData();
							uint formid = (TypeConverter.h2i(bytes[0], bytes[1], bytes[2], bytes[3]));
							quests.Add(new Pair<uint, Record>(formid, r2));
						}
					}
				}
				else if (r2.Name == "REFR" || r2.Name == "ACHR" || r2.Name == "ACRE")
				{
					if (r2.SubRecords.Count > 0 && r2.SubRecords[1].Name == "NAME")
					{
						byte[] bytes = r2.SubRecords[1].GetReadonlyData();
						uint formid = (TypeConverter.h2i(bytes[0], bytes[1], bytes[2], bytes[3]));
						refs.Add(new Pair<uint, Record>(formid, r2));
					}
				}
			}
			else
			{
				foreach (Rec r2 in ((GroupRecord)r).Records) RecursePlugin(r2, records, quests, refs);
			}
		}

		/// <summary>
		/// Performs the setup of the compiler for the given plugins.
		/// </summary>
		/// <param name="plugins">The plugins for which to setup the compiler.</param>
		public static void Setup(TesPlugin[] plugins)
		{
			if (!Inited) Init();
			TokenStream.Reset();
			edidList.Clear();
			globals.Clear();
			farVars.Clear();
			Dictionary<uint, Record> records = new Dictionary<uint, Record>();
			uint mask;
			List<Pair<uint, Record>> quests = new List<Pair<uint, Record>>();
			List<Pair<uint, Record>> refs = new List<Pair<uint, Record>>();
			Dictionary<uint, uint> RefLookupTable = new Dictionary<uint, uint>();
			for (uint i = 0; i < plugins.Length - 1; i++)
			{
				if (plugins[i] == null) continue;
				if (plugins[i].Records.Count == 0 || plugins[i].Records[0].Name != "TES4") continue;
				mask = 0;
				foreach (SubRecord sr in ((Record)plugins[i].Records[0]).SubRecords) if (sr.Name == "MAST") mask++;
				mask <<= 24;
				uint id = i << 24;
				foreach (Rec r in plugins[i].Records) RecursePlugin(r, mask, id, records, quests, refs);

				foreach (Pair<uint, Record> recs in refs)
				{
					if (RefLookupTable.ContainsKey(recs.Key) && RefLookupTable[recs.Key] != 0) quests.Add(new Pair<uint, Record>(RefLookupTable[recs.Key], recs.Value));
					else if (records.ContainsKey(recs.Key))
					{
						Record r = records[recs.Key];
						uint formID = 0;
						foreach (SubRecord sr in r.SubRecords)
						{
							if (sr.Name == "SCRI")
							{
								byte[] bytes = sr.GetReadonlyData();
								uint formid = (TypeConverter.h2i(bytes[0], bytes[1], bytes[2], bytes[3]));
								if ((formid & 0xff000000) == mask) { formID = (formid & 0xffffff) + id; break; }
							}
						}
						quests.Add(new Pair<uint, Record>(formID, recs.Value));
					}
				}
				RefLookupTable.Clear();
			}
			foreach (Rec r in plugins[plugins.Length - 1].Records) RecursePlugin(r, records, quests, refs);
			foreach (Pair<uint, Record> recs in refs)
			{
				if (RefLookupTable.ContainsKey(recs.Key) && RefLookupTable[recs.Key] != 0) quests.Add(new Pair<uint, Record>(RefLookupTable[recs.Key], recs.Value));
				else if (records.ContainsKey(recs.Key))
				{
					Record r = records[recs.Key];
					uint formID = 0;
					foreach (SubRecord sr in r.SubRecords)
					{
						if (sr.Name == "SCRI")
						{
							byte[] bytes = sr.GetReadonlyData();
							formID = (TypeConverter.h2i(bytes[0], bytes[1], bytes[2], bytes[3]));
							break;
						}
					}
					quests.Add(new Pair<uint, Record>(formID, recs.Value));
				}
			}

			foreach (KeyValuePair<uint, Record> recs in records)
			{
				string edid = recs.Value.SubRecords[0].GetStrData().ToLowerInvariant();
				if (recs.Value.Name == "GLOB")
				{
					TokenStream.AddGlobal(edid);
					globals[edid] = recs.Key;
				}
				else
				{
					TokenStream.AddEdid(edid);
					edidList[edid] = new Pair<uint, string>(recs.Key, recs.Value.Name);
				}
			}
			edidList["player"] = new Pair<uint, string>(0x14, "NPC_");
			TokenStream.AddEdid("player");
			edidList["playerref"] = new Pair<uint, string>(0x14, "NPC_");
			TokenStream.AddEdid("playerref");

			Dictionary<string, ushort> vars = new Dictionary<string, ushort>();
			foreach (Pair<uint, Record> quest in quests)
			{
				if (!records.ContainsKey(quest.Key)) continue;
				Record scpt = records[quest.Key];
				string edid = quest.Value.SubRecords[0].GetStrData();
				for (int i = 0; i < scpt.SubRecords.Count - 1; i++)
				{
					if (scpt.SubRecords[i].Name == "SLSD")
					{
						byte[] bytes = scpt.SubRecords[i].GetReadonlyData();
						vars[scpt.SubRecords[i + 1].GetStrData().ToLowerInvariant()] = TypeConverter.h2s(bytes[0], bytes[1]);
					}
				}
				if (vars.Count > 0)
				{
					farVars[edid.ToLowerInvariant()] = vars;
					vars = new Dictionary<string, ushort>();
				}
			}
		}
		#endregion

		private class LocalVar
		{
			public readonly VarType type;
			public readonly int index;
			public int refid;

			public LocalVar(int index, Token t)
			{
				this.index = index;
				switch (t.keyword)
				{
					case Keywords.Int:
						type = VarType.Int;
						break;
					case Keywords.Float:
						type = VarType.Float;
						break;
					case Keywords.Ref:
						type = VarType.Ref;
						break;
					default:
						throw new Exception("Should never happen: Invalid type passed to local variable constructor");
				}
			}
		}

		private static TokenStream ts;
		private static Record r;
		private static SubRecord schr;
		private static SubRecord scda;
		private static readonly Dictionary<string, LocalVar> locals = new Dictionary<string, LocalVar>();
		private static readonly List<LocalVar> localList = new List<LocalVar>();
		private static readonly Dictionary<string, ushort> edidRefs = new Dictionary<string, ushort>();
		private static int refcount;
		private static BinaryWriter bw;
		private static readonly List<string> errors = new List<string>();

		private static bool ReturnError(string msg, out string error)
		{
			error = ts.Line.ToString() + ": " + msg;
			ts = null;
			r = null;
			return false;
		}
		private static bool OutputErrors(out string msg)
		{
			msg = "";
			foreach (string s in errors) msg += s + Environment.NewLine;
			ts = null;
			r = null;
			return false;
		}
		private static void AddError(string msg)
		{
			errors.Add(ts.Line.ToString() + ": " + msg);
		}

		private static void HandleVariables()
		{
			Token[] smt = ts.PeekNextStatement();
			SubRecord slsd;
			SubRecord scvr;
			while (smt.Length > 0 && smt[0].IsType())
			{
				ts.PopNextStatement();
				if (smt.Length != 2 || smt[1].type != TokenType.Unknown)
				{
					AddError("Expected <type> <variable name>");
					smt = ts.PeekNextStatement();
					continue;
				}
				slsd = new SubRecord();
				slsd.Name = "SLSD";
				byte[] data = new byte[24];
				TypeConverter.si2h(locals.Count + 1, data, 0);
				if (smt[0].IsKeyword(Keywords.Int)) data[16] = 1;
				slsd.SetData(data);
				r.AddRecord(slsd);
				scvr = new SubRecord();
				scvr.Name = "SCVR";
				scvr.SetStrData(smt[1].utoken, true);
				r.AddRecord(scvr);

				LocalVar lv = new LocalVar(locals.Count + 1, smt[0]);
				locals.Add(smt[1].token, lv);
				localList.Add(lv);
				ts.AddLocal(smt[1].token);

				smt = ts.PeekNextStatement();
			}
		}

		private static Token[] TrimStatement(Token[] smt, int size)
		{
			Token[] smt2 = new Token[smt.Length - size];
			for (int i = 0; i < smt2.Length; i++) smt2[i] = smt[i + size];
			return smt2;
		}

		private static void EmitByte(byte code)
		{
			bw.Write(code);
		}
		private static void Emit(ushort code)
		{
			bw.Write(code);
		}
		private static void EmitLong(uint code)
		{
			bw.Write(code);
		}
		private enum RefType { Standard, Expression, Standalone/*, Block*/ }
		private static void EmitRefLabel(Token t, RefType type)
		{
			if (t.type == TokenType.Global)
			{
				EmitByte(0x47);
			}
			else
			{
				switch (type)
				{
					case RefType.Standard: Emit(0x1c); break;
					case RefType.Expression: EmitByte(0x72); break;
					case RefType.Standalone: EmitByte(0x5a); break;
				}
			}
			if (t.type == TokenType.Local)
			{
				LocalVar var = locals[t.token];
				if (var.refid == 0) AddError("Variable was not of type ref");
				else Emit((ushort)var.refid);
			}
			else if (t.type == TokenType.edid || t.type == TokenType.Global)
			{
				if (!edidRefs.ContainsKey(t.token))
				{
					SubRecord sr = new SubRecord();
					sr.Name = "SCRO";
					if (t.type == TokenType.edid) sr.SetData(TypeConverter.i2h(edidList[t.token].Key));
					else sr.SetData(TypeConverter.i2h(globals[t.token]));
					r.AddRecord(sr);
					refcount++;
					edidRefs[t.token] = (ushort)refcount;
				}
				Emit(edidRefs[t.token]);
			}
			else
			{
				AddError("Expected ref variable or edid");
			}
		}
		private static void EmitBegin(Token[] smt)
		{
			Emit(0x10);
			if (!blockList.ContainsKey(smt[1].token))
			{
				AddError("Unknown block type");
				EmitLong(0);
				EmitLong(0);
				return;
			}
			FunctionSig fs = blockList[smt[1].token];
			long pos = bw.BaseStream.Length;
			Emit(0);
			Emit(fs.opcode);
			EmitLong(0);
			if (smt.Length > fs.args.Length + 2) AddError("Too many arguments to 'begin' block");
			//for(int i=0;i<fs.paddingbytes;i++) EmitByte(0);
			if (fs.args.Length > 0)
			{
				Emit((ushort)(smt.Length - 2));
				for (int i = 2; i < smt.Length; i++)
				{
					switch (fs.args[i - 2])
					{
						case VarType.Short:
							if (smt[i].type != TokenType.Integer) AddError("Block argument: Expected short");
							else Emit(ushort.Parse(smt[i].token));
							break;
						case VarType.Int:
							if (smt[i].type != TokenType.Integer) AddError("Block argument: Expected integer");
							else
							{
								EmitByte(0x73);
								EmitLong(uint.Parse(smt[i].token));
							}
							break;
						case VarType.Ref:
							if (smt[i].type != TokenType.edid) AddError("Block argument: Expected edid");
							else
							{
								EmitRefLabel(smt[i], RefType.Expression);
							}
							break;
						default:
							AddError("Sanity check failed. VarType of block argument was invalid");
							break;
					}
				}
			}
			bw.BaseStream.Position = pos;
			Emit((ushort)(bw.BaseStream.Length - (pos + 2)));
			bw.BaseStream.Position = bw.BaseStream.Length;
		}
		private class ExpressionParseException : Exception { public ExpressionParseException(string msg) : base(msg) { } }
		private enum ExpressionType { Ref, Numeric, If }
		private static void EmitExpressionValue(Token t, Queue<Token> smt, ExpressionType type)
		{
			EmitByte(0x20);
			bool hadRef = false;
			switch (t.type)
			{
				case TokenType.edid:
					if (smt.Count > 0 && smt.Peek().IsSymbol("."))
					{
						EmitRefLabel(t, RefType.Expression);
						smt.Dequeue();
						if (smt.Count == 0) throw new ExpressionParseException("Unexpected end of line");
						if (farVars.ContainsKey(t.token))
						{
							Dictionary<string, ushort> fars = farVars[t.token];
							t = smt.Dequeue();
							if (fars.ContainsKey(t.token))
							{
								EmitByte(0x73);
								Emit(fars[t.token]);
								break;
							}
						}
						else t = smt.Dequeue();
						hadRef = true;
						goto case TokenType.Function;
					}
					else
					{
						if (type == ExpressionType.Numeric) AddError("Reference type not valid here");
						EmitRefLabel(t, RefType.Standalone);
					}
					break;
				case TokenType.Local:
					LocalVar lv = locals[t.token];
					if (lv.type == VarType.Ref && smt.Count > 0 && smt.Peek().IsSymbol(".")) goto case TokenType.edid;
					if (lv.type == VarType.Ref && type == ExpressionType.Numeric) AddError("Reference type not valid here");
					if (lv.type != VarType.Ref && type == ExpressionType.Ref) AddError("A reference assignment must consist of a single edid or function");
					if (lv.type == VarType.Int)
					{
						EmitByte(0x73);
					}
					else
					{
						EmitByte(0x66);
					}
					Emit((ushort)lv.index);
					break;
				case TokenType.Global:
					if (type == ExpressionType.Ref) AddError("A reference assignment must consist of a single edid or function");
					EmitRefLabel(t, RefType.Expression);
					break;
				case TokenType.Float:
				case TokenType.Integer:
					if (type == ExpressionType.Ref && t.token != "0") AddError("A reference assignment must consist of a single edid or function");
					bw.Write(System.Text.Encoding.ASCII.GetBytes(t.token));
					break;
				case TokenType.Function:
					//FunctionSig fs=functionList[t.token];
					//if(fs.requiredArgs!=fs.args.Length) throw new ExpressionParseException("functions with variable argument count cannot be used in expressions");
					//if(fs.ret==VarType.None) throw new ExpressionParseException("Functions with no return type cannot be used in expressions");
					//if(smt.Count<fs.args.Length) throw new ExpressionParseException("Not enough parameters to function");
					Token[] args = new Token[smt.Count + 1];
					args[0] = t;
					for (int i = 1; i < args.Length; i++) args[i] = smt.Dequeue();
					EmitFunctionCall(ref args, true, hadRef, type == ExpressionType.Ref);
					for (int i = 0; i < args.Length; i++) smt.Enqueue(args[i]);
					break;
				default:
					AddError("Expected <local>|<global>|<number>|<function>");
					break;
			}
		}
		private static bool EmitExpression2(Queue<Token> smt, int precedence, bool endonbracket, ExpressionType type)
		{
			if (smt.Count == 0) throw new ExpressionParseException("Unexpected end of line");
			Token t = smt.Dequeue();
			if (t.type == TokenType.Symbol)
			{
				if (type == ExpressionType.Ref)
				{
					throw new ExpressionParseException("A reference assignment must consist of a single edid or function");
				}
				if (t.IsSymbol("("))
				{
					EmitExpression2(smt, int.MaxValue, true, type);
				}
				else if (uniOps.ContainsKey(t.token))
				{
					bool b = EmitExpression2(smt, uniOps[t.token].precedence, endonbracket, type);
					EmitByte(0x20);
					bw.Write(uniOps[t.token].opcode);
					if (b) return true;
				}
				else throw new ExpressionParseException("Syntax error");
			}
			else EmitExpressionValue(t, smt, type);
		start:
			if (smt.Count == 0)
			{
				if (endonbracket) AddError("Expected ')'");
				return true;
			}
			if (type == ExpressionType.Ref)
			{
				throw new ExpressionParseException("A reference assignment must consist of a single edid or function");
			}
			t = smt.Peek();
			if (t.type != TokenType.Symbol) throw new ExpressionParseException("Syntax error");
			if (t.IsSymbol(")"))
			{
				smt.Dequeue();
				if (endonbracket) return true;
				else throw new ExpressionParseException("Syntax error: misplaced ')'");
			}
			if (biOps.ContainsKey(t.token))
			{
				if (biOps[t.token].precedence <= precedence)
				{
					bool dontRepeat = false;
					smt.Dequeue();
					if (biOps[t.token].end == null)
					{
						if (EmitExpression2(smt, biOps[t.token].precedence, endonbracket, type))
						{
							dontRepeat = true;
						}
					}
					else
					{
						EmitExpression2(smt, biOps[t.token].precedence, endonbracket, type);
					}
					EmitByte(0x20);
					bw.Write(biOps[t.token].opcode);
					if (dontRepeat) return true;
					goto start;
				}
				else return false;
			}
			else throw new ExpressionParseException("Syntax error");
		}
		private static void EmitExpression(Token[] smt, ExpressionType type)
		{
			long pos = bw.BaseStream.Length;
			Emit(0);
			try
			{
				EmitExpression2(new Queue<Token>(smt), int.MaxValue, false, type);
			}
			catch (ExpressionParseException ex)
			{
				AddError(ex.Message);
			}

			bw.BaseStream.Position = pos;
			Emit((ushort)(bw.BaseStream.Length - (pos + 2)));
			bw.BaseStream.Position = bw.BaseStream.Length;
		}
		private static void EmitFunctionCall(ref Token[] smt, bool expression, bool hadref, bool requiresRef)
		{
			FunctionSig fs = functionList[smt[0].token];
			if (hadref && !fs.allowref) AddError("Object reference not valid on this function");
			if (expression)
			{
				EmitByte(0x58);
				//if(fs.ret==VarType.None) AddError("Functions with no return type cannot be used in expressions");
			}
			//if(requiresRef&&fs.ret!=VarType.Ref) AddError("Function does not return a reference");
			Emit(fs.opcode);
			if (fs.skipArgs)
			{
				if (expression) AddError("SkipArgs is not valid on functions used in expressions");
				//for(int j=1;j<smt.Length;j++) smt[j-1]=smt[j];
				//Array.Resize<Token>(ref smt, smt.Length-1);
				smt = new Token[0];
				Emit(0);
				return;
			}
			if (smt.Length == 1)
			{
				if (fs.requiredArgs > 0) AddError("Not enough arguments to function");
				if (fs.args.Length > 0) Emit(2);
				Emit(0);
				smt = new Token[0];
				return;
			}
			if (fs.args.Length == 0)
			{
				Emit(0);
				for (int j = 1; j < smt.Length; j++) smt[j - 1] = smt[j];
				Array.Resize<Token>(ref smt, smt.Length - 1);
				return;
			}
			long pos = bw.BaseStream.Length;
			ushort argcount = 0;
			Emit(0);
			Emit(0);
			int i = 0;
			bool lastwasref = false;
			while (true)
			{
				i++;
				if (i == smt.Length)
				{
					if (argcount < fs.requiredArgs) AddError("Not enough arguments to function. Expected " + fs.requiredArgs);
					smt = new Token[0];
					break;
				}
				if (smt[i].type == TokenType.Symbol)
				{
					if (smt[i].IsSymbol(".") && lastwasref)
					{
						if (i < smt.Length - 1 && farVars.ContainsKey(smt[i - 1].token))
						{
							i++;
							EmitByte(0x73);
							Dictionary<string, ushort> vars = farVars[smt[i - 2].token];
							if (!vars.ContainsKey(smt[i].token)) AddError("Reference '" + smt[i - 2].utoken + "' has no variable called '" + smt[i].utoken + "'");
							else Emit(vars[smt[i].token]);
							continue;
						}
					}
					else if (smt[i].IsSymbol("-") && (!expression || (argcount < fs.requiredArgs)))
					{
						if (i < smt.Length - 1 && (smt[i + 1].type == TokenType.Integer || smt[i + 1].type == TokenType.Float))
						{
							smt[i + 1] = new Token(smt[i + 1].type, "-" + smt[i + 1].token);
							continue;
						}
					}
					if (expression)
					{
						if (argcount < fs.requiredArgs) AddError("Not enough arguments to function. Expected " + fs.requiredArgs);
						for (int j = i; j < smt.Length; j++) smt[j - i] = smt[j];
						Array.Resize<Token>(ref smt, smt.Length - i);
						break;
					}
					else AddError("Unexpected symbol '" + smt[i].token + "' in function arguments");
				}
				if (argcount == fs.args.Length) AddError("Too many arguments given to function. Expected " + fs.args.Length);
				argcount++;
				lastwasref = false;
				switch (fs.args[argcount - 1])
				{
					case VarType.Axis:
						switch (smt[i].token)
						{
							case "x":
								EmitByte((byte)'X');
								continue;
							case "y":
								EmitByte((byte)'Y');
								continue;
							case "z":
								EmitByte((byte)'Z');
								continue;
							default:
								AddError("Expected 'x', 'y' or 'z'");
								continue;
						}
					case VarType.Enum:
						if (smt[i].type == TokenType.Integer)
						{
							Emit(ushort.Parse(smt[i].token));
						}
						else
						{
							Dictionary<string, ushort> Enum = enumList[fs.reftypes[argcount - 1]];
							if (!Enum.ContainsKey(smt[i].token))
							{
								AddError("'" + smt[i].token + "' is not a valid entry of the enum '" + fs.reftypes[argcount - 1] + "'");
							}
							else
							{
								Emit(Enum[smt[i].token]);
							}
						}
						continue;
					case VarType.Short:
						if (smt[i].type != TokenType.Integer)
						{
							AddError("Expected integer argument");
						}
						else
						{
							Emit(ushort.Parse(smt[i].token));
						}
						continue;
					case VarType.String:
						Emit((ushort)smt[i].token.Length);
						bw.Write(System.Text.Encoding.Default.GetBytes(smt[i].token));
						continue;
				}
				switch (smt[i].type)
				{
					case TokenType.edid:
						if (i == smt.Length - 1 || !smt[i + 1].IsSymbol("."))
						{
							if (fs.args[argcount - 1] != VarType.Ref) AddError("Invalid argument " + i + " to function. Expected " + fs.args[argcount - 1].ToString());
							if (fs.reftypes[argcount - 1] != null && fs.reftypes[argcount - 1] != edidList[smt[i].token].Value)
							{
								AddError("Invalid record type at argument " + i + " of function. Expected " + fs.reftypes[argcount - 1]);
							}
						}
						EmitRefLabel(smt[i], RefType.Expression);
						lastwasref = true;
						break;
					case TokenType.Local:
						LocalVar vt = locals[smt[i].token];
						switch (vt.type)
						{
							case VarType.Int:
								if (fs.args[argcount - 1] != VarType.Float && fs.args[argcount - 1] != VarType.Int) AddError("Invalid argument " + i + " to function. Expected " + fs.args[argcount - 1].ToString());
								EmitByte(0x73);
								Emit((ushort)locals[smt[i].token].index);
								break;
							case VarType.Float:
								if (fs.args[argcount - 1] != VarType.Float && fs.args[argcount - 1] != VarType.Int) AddError("Invalid argument " + i + " to function. Expected " + fs.args[argcount - 1].ToString());
								EmitByte(0x66);
								Emit((ushort)locals[smt[i].token].index);
								break;
							case VarType.Ref:
								if (fs.args[argcount - 1] != VarType.Ref) AddError("Invalid argument " + i + " to function. Expected " + fs.args[argcount - 1].ToString());
								EmitRefLabel(smt[i], RefType.Expression);
								break;
						}
						break;
					case TokenType.Global:
						if (fs.args[argcount - 1] != VarType.Float && fs.args[argcount - 1] != VarType.Int) AddError("Invalid argument " + i + " to function. Expected " + fs.args[argcount - 1].ToString());
						EmitRefLabel(smt[i], RefType.Expression);
						break;
					case TokenType.Integer:
						if (fs.args[argcount - 1] == VarType.Float) goto case TokenType.Float;
						if (fs.args[argcount - 1] != VarType.Int) AddError("Invalid argument " + i + " to function. Expected " + fs.args[argcount - 1].ToString());
						EmitByte(0x6e);
						bw.Write(int.Parse(smt[i].token));
						break;
					case TokenType.Float:
						if (fs.args[argcount - 1] != VarType.Float && fs.args[argcount - 1] != VarType.Int) AddError("Invalid argument " + i + " to function. Expected " + fs.args[argcount - 1].ToString());
						EmitByte(0x7a);
						bw.Write(double.Parse(smt[i].token));
						break;
					default:
						AddError("Expected <global>|<local>|<constant>");
						return;
				}
			}
			for (int j = 0; j < fs.paddingbytes; j++) EmitByte(0);
			bw.BaseStream.Position = pos;
			Emit((ushort)(bw.BaseStream.Length - (pos + 2)));
			Emit(argcount);
			bw.BaseStream.Position = bw.BaseStream.Length;
		}
		private static void EmitShowMessage(Token[] smt)
		{
			//ShowMessage DoorOpenedScienceMsg passSkill
			//59 10 0E 00 01 00 72 02 00 01 00 73 01 00 00 00 00 00
			Emit(0x1059);
			if (smt.Length == 1)
			{
				AddError("Not enough arguments to ShowMessage");
				return;
			}
			long pos = bw.BaseStream.Length;
			Emit(0);
			Emit(1);

			switch (smt[1].type)
			{
				case TokenType.edid:
					if (edidList[smt[1].token].Value != "MESG") goto default;
					else EmitRefLabel(smt[1], RefType.Expression);
					break;
				case TokenType.Local:
					LocalVar vt = locals[smt[1].token];
					if (vt.type != VarType.Ref) goto default;
					EmitRefLabel(smt[1], RefType.Expression);
					break;
				default:
					AddError("First argument to ShowMessage must be an MESG record");
					return;
			}

			if (smt.Length == 2)
			{
				Emit(0);
			}
			else
			{
				bool lastwasref = false;
				Emit((ushort)(smt.Length - 2));
				for (int i = 2; i < smt.Length; i++)
				{
					if (smt[i].type == TokenType.Symbol)
					{
						if (smt[i].IsSymbol(".") && lastwasref)
						{
							if (i < smt.Length - 1 && farVars.ContainsKey(smt[i - 1].token))
							{
								i++;
								EmitByte(0x73);
								Dictionary<string, ushort> vars = farVars[smt[i - 2].token];
								if (!vars.ContainsKey(smt[i].token)) AddError("Reference '" + smt[i - 2].utoken + "' has no variable called '" + smt[i].utoken + "'");
								else Emit(vars[smt[i].token]);
								continue;
							}
						}
						else if (smt[i].IsSymbol("-"))
						{
							if (i < smt.Length - 1 && (smt[i + 1].type == TokenType.Integer || smt[i + 1].type == TokenType.Float))
							{
								smt[i + 1] = new Token(smt[i + 1].type, "-" + smt[i + 1].token);
								continue;
							}
						}
						AddError("Unexpected symbol '" + smt[i].token + "' in ShowMessage arguments");
					}
					lastwasref = false;
					switch (smt[i].type)
					{
						case TokenType.edid:
							EmitRefLabel(smt[i], RefType.Expression);
							lastwasref = true;
							break;
						case TokenType.Local:
							LocalVar vt = locals[smt[i].token];
							switch (vt.type)
							{
								case VarType.Int:
									EmitByte(0x73);
									Emit((ushort)locals[smt[i].token].index);
									break;
								case VarType.Float:
									EmitByte(0x66);
									Emit((ushort)locals[smt[i].token].index);
									break;
								case VarType.Ref:
									EmitRefLabel(smt[i], RefType.Expression);
									break;
							}
							break;
						case TokenType.Global:
							EmitRefLabel(smt[i], RefType.Expression);
							break;
						case TokenType.Integer:
							EmitByte(0x6e);
							bw.Write(int.Parse(smt[i].token));
							break;
						case TokenType.Float:
							EmitByte(0x7a);
							bw.Write(double.Parse(smt[i].token));
							break;
						default:
							AddError("Expected <global>|<local>|<constant>");
							return;
					}
				}
			}
			Emit(0);
			Emit(0);
			bw.BaseStream.Position = pos;
			Emit((ushort)(bw.BaseStream.Length - (pos + 2)));
			bw.BaseStream.Position = bw.BaseStream.Length;
		}
		private static void HandleStatement(Token[] smt)
		{
			if (smt[0].type == TokenType.Function)
			{
				EmitFunctionCall(ref smt, false, false, false);
			}
			else if (smt[0].IsKeyword(Keywords.ShowMessage))
			{
				EmitShowMessage(smt); ;
			}
			else if (smt[0].IsKeyword(Keywords.Set))
			{
				if (smt.Length < 4 || !(smt[2].IsKeyword(Keywords.To) || smt[2].IsSymbol(".")))
				{
					AddError("Expected 'set <var> to <expression>'");
					return;
				}
				Emit(0x15);
				long pos = bw.BaseStream.Length;
				Emit(0);
				if (smt[1].type == TokenType.Local)
				{
					LocalVar lv = locals[smt[1].token];
					if (lv.type == VarType.Int) EmitByte(0x73);
					else EmitByte(0x66);
					Emit((ushort)lv.index);
					EmitExpression(TrimStatement(smt, 3), (lv.type == VarType.Ref) ? ExpressionType.Ref : ExpressionType.Numeric);
				}
				else if (smt[1].type == TokenType.Global)
				{
					EmitRefLabel(smt[1], RefType.Expression);
					EmitExpression(TrimStatement(smt, 3), ExpressionType.Numeric);
				}
				else if (smt[1].type == TokenType.edid && farVars.ContainsKey(smt[1].token) && smt[2].IsSymbol("."))
				{
					if (smt.Length < 6 || !smt[4].IsKeyword(Keywords.To))
					{
						AddError("Expected 'set <var> to <expression>'");
						return;
					}
					EmitRefLabel(smt[1], RefType.Expression);
					EmitByte(0x73);
					if (!farVars[smt[1].token].ContainsKey(smt[3].token))
					{
						AddError("Local variable '" + smt[3].token + " does not exist in quest " + smt[1].token);
					}
					else
					{
						Emit(farVars[smt[1].token][smt[3].token]);
					}
					EmitExpression(TrimStatement(smt, 5), ExpressionType.If);
				}
				else
				{
					AddError("Expected set <local>|<global> to <expression>");
				}
				bw.BaseStream.Position = pos;
				Emit((ushort)(bw.BaseStream.Length - (pos + 2)));
				bw.BaseStream.Position = bw.BaseStream.Length;
			}
			else if (smt[0].type == TokenType.edid)
			{
				if (smt.Length < 3 || !smt[1].IsSymbol(".") || smt[2].type != TokenType.Function)
				{
					AddError("Expected ref.function");
					return;
				}
				EmitRefLabel(smt[0], RefType.Standard);
				smt = TrimStatement(smt, 2);
				EmitFunctionCall(ref smt, false, true, false);
			}
			else if (smt[0].type == TokenType.Local)
			{
				LocalVar lv = locals[smt[0].token];
				if (lv.type != VarType.Ref)
				{
					AddError("Expected 'Set', <function> or <ref>.<function>");
					return;
				}
				if (smt.Length < 3 || !smt[1].IsSymbol(".") || smt[2].type != TokenType.Function)
				{
					AddError("Expected ref.function");
					return;
				}
				EmitRefLabel(smt[0], RefType.Standard);
				smt = TrimStatement(smt, 2);
				EmitFunctionCall(ref smt, false, true, false);
			}
			else
			{
				AddError("Expected 'Set', <function> or <ref>.<function>");
			}
		}
		private static void HandleBlock()
		{
			Token[] smt = ts.PopNextStatement();
			long pos = bw.BaseStream.Length + 6;
			if (smt.Length < 2 || !smt[0].IsKeyword(Keywords.Begin)) AddError("Expected 'begin <args>'");
			else EmitBegin(smt);
			long start = bw.BaseStream.Length;
			smt = ts.PopNextStatement();
			Stack<long> flowControl = new Stack<long>();
			List<ushort> opcodecount = new List<ushort>();
			while (smt.Length > 0 && !smt[0].IsKeyword(Keywords.End))
			{
				if (smt[0].IsFlowControl())
				{
					switch (smt[0].keyword)
					{
						case Keywords.If:
							{
								for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
								Emit(0x16);
								long pos2 = bw.BaseStream.Length;
								Emit(0);
								flowControl.Push(bw.BaseStream.Length);
								Emit(0);
								EmitExpression(TrimStatement(smt, 1), ExpressionType.If);
								bw.BaseStream.Position = pos2;
								Emit((ushort)(bw.BaseStream.Length - (pos2 + 2)));
								bw.BaseStream.Position = bw.BaseStream.Length;
								opcodecount.Add(0);
								break;
							}
						case Keywords.ElseIf:
							if (flowControl.Count == 0)
							{
								AddError("elseif without matching if");
							}
							else
							{
								bw.BaseStream.Position = flowControl.Pop();
								Emit(opcodecount[opcodecount.Count - 1]);
								opcodecount.RemoveAt(opcodecount.Count - 1);
								bw.BaseStream.Position = bw.BaseStream.Length;
							}
							for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
							{
								Emit(0x18);
								long pos2 = bw.BaseStream.Length;
								Emit(0);
								flowControl.Push(bw.BaseStream.Length);
								Emit(0);
								EmitExpression(TrimStatement(smt, 1), ExpressionType.If);
								bw.BaseStream.Position = pos2;
								Emit((ushort)(bw.BaseStream.Length - (pos2 + 2)));
								bw.BaseStream.Position = bw.BaseStream.Length;
								opcodecount.Add(0);
							}
							break;
						case Keywords.Else:
							if (flowControl.Count == 0)
							{
								AddError("else without matching if");
							}
							else
							{
								bw.BaseStream.Position = flowControl.Pop();
								Emit(opcodecount[opcodecount.Count - 1]);
								opcodecount.RemoveAt(opcodecount.Count - 1);
								bw.BaseStream.Position = bw.BaseStream.Length;
							}
							for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
							Emit(0x17);
							Emit(2);
							flowControl.Push(bw.BaseStream.Length);
							Emit(0);
							opcodecount.Add(0);
							break;
						case Keywords.EndIf:
							Emit(0x19);
							Emit(0);
							if (flowControl.Count == 0)
							{
								AddError("endif without matching if");
							}
							else
							{
								bw.BaseStream.Position = flowControl.Pop();
								Emit(opcodecount[opcodecount.Count - 1]);
								opcodecount.RemoveAt(opcodecount.Count - 1);
								bw.BaseStream.Position = bw.BaseStream.Length;
							}
							for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
							break;
						case Keywords.Return:
							Emit(0x1e);
							Emit(0);
							for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
							break;
					}
				}
				else
				{
					HandleStatement(smt);
					for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
				}
				smt = ts.PopNextStatement();
			}
			if (smt.Length == 0) AddError("Unexpected end of file. Expected 'end'");
			else if (smt.Length > 1) AddError("Unexpected text after 'end'.");
			if (flowControl.Count > 0) AddError("Missing 'endif'");
			EmitLong(0x11);
			bw.BaseStream.Position = pos;
			EmitLong((uint)(bw.BaseStream.Length - start));
			bw.BaseStream.Position = bw.BaseStream.Length;
		}
		private static void HandleResultsBlock()
		{
			Token[] smt = ts.PopNextStatement();
			Stack<long> flowControl = new Stack<long>();
			List<ushort> opcodecount = new List<ushort>();
			while (smt.Length > 0)
			{
				if (smt[0].IsKeyword(Keywords.End))
				{
					AddError("Keyword 'end' not valid in a results script");
					return;
				}
				if (smt[0].IsFlowControl())
				{
					switch (smt[0].keyword)
					{
						case Keywords.If:
							{
								for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
								Emit(0x16);
								long pos2 = bw.BaseStream.Length;
								Emit(0);
								flowControl.Push(bw.BaseStream.Length);
								Emit(0);
								EmitExpression(TrimStatement(smt, 1), ExpressionType.If);
								bw.BaseStream.Position = pos2;
								Emit((ushort)(bw.BaseStream.Length - (pos2 + 2)));
								bw.BaseStream.Position = bw.BaseStream.Length;
								opcodecount.Add(0);
								break;
							}
						case Keywords.ElseIf:
							if (flowControl.Count == 0)
							{
								AddError("elseif without matching if");
							}
							else
							{
								bw.BaseStream.Position = flowControl.Pop();
								Emit(opcodecount[opcodecount.Count - 1]);
								opcodecount.RemoveAt(opcodecount.Count - 1);
								bw.BaseStream.Position = bw.BaseStream.Length;
							}
							for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
							{
								Emit(0x18);
								long pos2 = bw.BaseStream.Length;
								Emit(0);
								flowControl.Push(bw.BaseStream.Length);
								Emit(0);
								EmitExpression(TrimStatement(smt, 1), ExpressionType.If);
								bw.BaseStream.Position = pos2;
								Emit((ushort)(bw.BaseStream.Length - (pos2 + 2)));
								bw.BaseStream.Position = bw.BaseStream.Length;
								opcodecount.Add(0);
							}
							break;
						case Keywords.Else:
							if (flowControl.Count == 0)
							{
								AddError("else without matching if");
							}
							else
							{
								bw.BaseStream.Position = flowControl.Pop();
								Emit(opcodecount[opcodecount.Count - 1]);
								opcodecount.RemoveAt(opcodecount.Count - 1);
								bw.BaseStream.Position = bw.BaseStream.Length;
							}
							for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
							Emit(0x17);
							Emit(2);
							flowControl.Push(bw.BaseStream.Length);
							Emit(0);
							opcodecount.Add(0);
							break;
						case Keywords.EndIf:
							Emit(0x19);
							Emit(0);
							if (flowControl.Count == 0)
							{
								AddError("endif without matching if");
							}
							else
							{
								bw.BaseStream.Position = flowControl.Pop();
								Emit(opcodecount[opcodecount.Count - 1]);
								opcodecount.RemoveAt(opcodecount.Count - 1);
								bw.BaseStream.Position = bw.BaseStream.Length;
							}
							for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
							break;
						case Keywords.Return:
							Emit(0x1e);
							Emit(0);
							for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
							break;
					}
				}
				else
				{
					HandleStatement(smt);
					for (int i = 0; i < opcodecount.Count; i++) opcodecount[i] += 1;
				}
				smt = ts.PopNextStatement();
			}
			if (flowControl.Count > 0) AddError("Missing 'endif'");
		}

		/// <summary>
		/// Compiles the script in the specified record.
		/// </summary>
		/// <param name="sr">The record containing the script to compile.</param>
		/// <param name="r2">The record to which the script was compiled.</param>
		/// <param name="msg">The compiler return message.</param>
		/// <returns><c>true</c> if the script was compiled;
		/// <c>false</c> otherwise.</returns>
		public static bool CompileResultScript(SubRecord sr, out Record r2, out string msg)
		{
			msg = null;
			r2 = null;
			r = new Record();
			string script = sr.GetStrData();
			locals.Clear();
			localList.Clear();
			edidRefs.Clear();
			refcount = 0;
			errors.Clear();

			ts = new TokenStream(script, errors);
			if (errors.Count > 0) return OutputErrors(out msg);
			schr = new SubRecord();
			schr.Name = "SCHR";
			r.AddRecord(schr);
			scda = new SubRecord();
			scda.Name = "SCDA";
			r.AddRecord(scda);
			sr = (SubRecord)sr.Clone();
			r.AddRecord(sr);

			bw = new BinaryWriter(new MemoryStream());

			while (ts.PeekNextStatement().Length > 0)
			{
				try
				{
					HandleResultsBlock();
				}
				catch (Exception ex)
				{
					return ReturnError(ex.Message, out msg);
				}
			}


			if (errors.Count > 0)
			{
				return OutputErrors(out msg);
			}

			byte[] header = new byte[20];
			TypeConverter.si2h(refcount, header, 4);
			TypeConverter.i2h((uint)bw.BaseStream.Length, header, 8);
			TypeConverter.si2h(localList.Count, header, 12);
			TypeConverter.si2h(0x10000, header, 16);
			schr.SetData(header);
			byte[] compileddata = ((MemoryStream)bw.BaseStream).GetBuffer();
			if (compileddata.Length != bw.BaseStream.Length) Array.Resize<byte>(ref compileddata, (int)bw.BaseStream.Length);
			scda.SetData(compileddata);
			bw.Close();
			r2 = r;
			return true;
		}

		/// <summary>
		/// Compiles the script in the given record.
		/// </summary>
		/// <param name="r2">The record containing the script to compile.</param>
		/// <param name="msg">The compiler return message.</param>
		/// <returns><c>true</c> if the script was compiled;
		/// <c>false</c> otherwise.</returns>
		public static bool Compile(Record r2, out string msg)
		{
			msg = null;
			r = new Record();
			string script = null;
			int scptype = 0;
			foreach (SubRecord sr2 in r2.SubRecords)
			{
				if (sr2.Name == "SCTX") script = sr2.GetStrData();
				if (sr2.Name == "SCHR")
				{
					byte[] tmp = sr2.GetReadonlyData();
					scptype = TypeConverter.h2si(tmp[16], tmp[17], tmp[18], tmp[19]);
				}
			}
			if (script == null)
			{
				msg = "Script had no SCTX record to compile";
				return false;
			}
			locals.Clear();
			localList.Clear();
			edidRefs.Clear();
			refcount = 0;
			errors.Clear();

			ts = new TokenStream(script, errors);
			if (errors.Count > 0) return OutputErrors(out msg);
			Token[] smt = ts.PopNextStatement();
			if (smt.Length != 2 || !smt[0].IsKeyword(Keywords.ScriptName) || smt[1].token == null) return ReturnError("Expected 'ScriptName <edid>'", out msg);
			SubRecord sr = new SubRecord();
			sr.Name = "EDID";
			sr.SetStrData(smt[1].utoken, true);
			r.AddRecord(sr);
			r.descriptiveName = " (" + smt[1].token + ")";
			schr = new SubRecord();
			schr.Name = "SCHR";
			r.AddRecord(schr);
			scda = new SubRecord();
			scda.Name = "SCDA";
			r.AddRecord(scda);
			sr = new SubRecord();
			sr.Name = "SCTX";
			sr.SetStrData(script, false);
			r.AddRecord(sr);

			bw = new BinaryWriter(new MemoryStream());
			Emit(0x001d);
			Emit(0x0000);
			try
			{
				HandleVariables();
			}
			catch (Exception ex)
			{
				return ReturnError(ex.Message, out msg);
			}
			for (int i = 0; i < localList.Count; i++)
			{
				if (localList[i].type == VarType.Ref)
				{
					sr = new SubRecord();
					sr.Name = "SCRV";
					sr.SetData(TypeConverter.si2h(i + 1));
					r.AddRecord(sr);
					refcount++;
					localList[i].refid = refcount;
				}
			}
			while (ts.PeekNextStatement().Length > 0)
			{
				try
				{
					HandleBlock();
				}
				catch (Exception ex)
				{
					return ReturnError(ex.Message, out msg);
				}
			}
			if (errors.Count > 0)
			{
				return OutputErrors(out msg);
			}

			byte[] header = new byte[20];
			TypeConverter.si2h(refcount, header, 4);
			TypeConverter.i2h((uint)bw.BaseStream.Length, header, 8);
			TypeConverter.si2h(localList.Count, header, 12);
			TypeConverter.si2h(scptype, header, 16);
			schr.SetData(header);
			byte[] compileddata = ((MemoryStream)bw.BaseStream).GetBuffer();
			if (compileddata.Length != bw.BaseStream.Length) Array.Resize<byte>(ref compileddata, (int)bw.BaseStream.Length);
			scda.SetData(compileddata);
			r2.SubRecords.Clear();
			r2.SubRecords.AddRange(r.SubRecords);
			bw.Close();
			return true;
		}
	}
}
