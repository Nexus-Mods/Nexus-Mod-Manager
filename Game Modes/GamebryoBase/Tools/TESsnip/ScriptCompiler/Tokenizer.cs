using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.Games.Gamebryo.Tools.TESsnip.ScriptCompiler
{
	enum TokenType { Unknown, Integer, Float, Keyword, Symbol, Local, Global, Function, edid, Null }
	enum Keywords
	{
		If, ElseIf, Else, EndIf, ScriptName, Scn, Short, Int, Float, Ref, Begin, End, Set, To, Return, ShowMessage, NotAKeyword
	}
	struct Token
	{
		public readonly static Token Null = new Token(TokenType.Null, null);
		public readonly static Token NewLine = new Token(TokenType.Symbol, "\n");

		public readonly TokenType type;
		public readonly string token;
		public readonly string utoken;
		public readonly Keywords keyword;

		private static readonly Keywords[] typelist = new Keywords[] { Keywords.Int, Keywords.Float, Keywords.Ref };

		private static readonly Keywords[] flowlist = new Keywords[] { Keywords.If, Keywords.ElseIf, Keywords.Else, Keywords.EndIf, Keywords.Return };

		public Token(TokenType type, string token)
		{
			this.type = type;
			this.utoken = token;
			this.token = token;
			this.keyword = Keywords.NotAKeyword;
		}
		public Token(TokenType type, string ltoken, string token)
		{
			this.type = type;
			this.utoken = token;
			this.token = ltoken;
			this.keyword = Keywords.NotAKeyword;
		}
		public Token(TokenType type, Keywords keyword)
		{
			if (keyword == Keywords.Short) keyword = Keywords.Int;
			else if (keyword == Keywords.Scn) keyword = Keywords.ScriptName;
			this.type = type;
			this.keyword = keyword;
			this.token = keyword.ToString();
			this.utoken = this.token;
		}

		public override string ToString()
		{
			if (type == TokenType.Keyword) return keyword.ToString();
			return token;
		}

		public bool IsFlowControl()
		{
			return type == TokenType.Keyword && Array.IndexOf<Keywords>(flowlist, keyword) != -1;
		}
		public bool IsType()
		{
			return type == TokenType.Keyword && Array.IndexOf<Keywords>(typelist, keyword) != -1;
		}
		public bool IsSymbol(string s)
		{
			return type == TokenType.Symbol && s == token;
		}
		public bool IsKeyword(Keywords k)
		{
			return type == TokenType.Keyword && keyword == k;
		}
		/*public bool IsLiteral() {
			if(type==TokenType.String||type==TokenType.Float||type==TokenType.Integer) return true;
			return false;
		}*/
	}

	class TokenStream
	{
		private static readonly string[] ReservedWords = new string[] { "if", "elseif", "else", "endif", "scriptname", "scn", "short", "int", "float", "ref", "begin", "end", "set", "to", "return", "showmessage" };

		private static readonly List<string> globalVars = new List<string>();
		private static readonly List<string> functions = new List<string>();
		private static readonly List<string> edids = new List<string>();
		private readonly List<string> localVars = new List<string>();

		public void AddLocal(string s) { localVars.Add(s); }
		public static void AddGlobal(string s) { globalVars.Add(s); }
		public static void AddFunction(string s) { functions.Add(s); }
		public static void AddEdid(string s) { edids.Add(s); }

		public static void Reset()
		{
			globalVars.Clear();
			edids.Clear();
		}

		private readonly Queue<char> input;
		private readonly Queue<Token> storedTokens;

		private int line;
		public int Line { get { return line; } }

		private readonly List<string> errors;
		private void AddError(string msg) { errors.Add(line.ToString() + ": " + msg); }

		private void SkipLine()
		{
			while (input.Count > 0 && input.Dequeue() != '\n') ;
			line++;
		}

		private char SafePop()
		{
			if (input.Count == 0) return '\0';
			char c = input.Dequeue();
			while (c == '\r')
			{
				if (input.Count == 0) return '\0';
				c = input.Dequeue();
			}
			if (c == '\t' || c == ',') c = ' ';
			if (c < 32 && c != '\n') AddError("There is an invalid character in the file");
			return c;
		}
		private char SafePeek()
		{
			if (input.Count == 0) return '\0';
			char c = input.Peek();
			while (c == '\r')
			{
				input.Dequeue();
				if (input.Count == 0) return '\0';
				c = input.Peek();
			}
			if (c == '\t' || c == ',') c = ' ';
			if (c < 32 && c != '\n') AddError("There is an invalid character in the file");
			return c;
		}

		private readonly StringBuilder builder = new StringBuilder(32);
		private static Token FromWord(string token)
		{
			int i;
			string ltoken = token.ToLowerInvariant();
			if (char.IsDigit(token[0]) || (token.Length > 1 && (token[0] == '.' || token[0] == '-') && char.IsDigit(token[1])))
			{
				if (token.Contains(".") || ltoken.Contains("e")) return new Token(TokenType.Float, token);
				return new Token(TokenType.Integer, token);
			}
			if ((i = Array.IndexOf<string>(ReservedWords, ltoken)) != -1)
			{
				return new Token(TokenType.Keyword, (Keywords)i);
			}
			return new Token(TokenType.Unknown, ltoken, token);
		}
		private Token PopTokenInternal2()
		{
			char c;
			while (true)
			{
				while (true)
				{
					c = SafePop();
					if (c == '\0') return Token.Null;
					else if (c == '\n')
					{
						line++;
						return Token.NewLine;
					}
					else if (c == ';')
					{
						SkipLine();
						return Token.NewLine;
					}
					else
					{
						if (!char.IsWhiteSpace(c))
						{
							break;
						}
					}
				}
				if (char.IsLetterOrDigit(c) || c == '_' || ((c == '.' || c == '~') && char.IsDigit(SafePeek())))
				{
					builder.Length = 0;
					if (c == '~') builder.Append('-');
					else builder.Append(c);
					bool numeric = char.IsDigit(c);
					while (true)
					{
						c = SafePeek();
						if (char.IsLetterOrDigit(c) || c == '_' || (numeric && c == '.'))
						{
							builder.Append(input.Dequeue());
						}
						else break;
					}
					return FromWord(builder.ToString());
				}
				else switch (c)
					{
						case '"':
							builder.Length = 0;
							while ((c = SafePop()) != '"')
							{
								if (c == '\r' || c == '\n' || c == '\0')
								{
									AddError("Unexpected end of line");
									break;
								}
								if (c == '\\')
								{
									switch (c = SafePop())
									{
										case '\0':
										case '\r':
										case '\n':
											AddError("Unexpected end of line");
											return FromWord(builder.ToString());
										case '\\':
											builder.Append('\\');
											break;
										case 'n':
											builder.Append('\n');
											break;
										case '"':
											builder.Append('"');
											break;
										default:
											AddError("Unrecognised escape sequence");
											builder.Append(c);
											break;
									}
								}
								else builder.Append(c);
							}
							return FromWord(builder.ToString());
						case '+':
							return new Token(TokenType.Symbol, "+");
						case '-':
							return new Token(TokenType.Symbol, "-");
						case '*':
							if (SafePeek() == '*') { input.Dequeue(); return new Token(TokenType.Symbol, "**"); }
							return new Token(TokenType.Symbol, "*");
						case '/':
							if (SafePeek() == '=') { input.Dequeue(); return new Token(TokenType.Symbol, "/="); }
							if (SafePeek() == ')') { input.Dequeue(); return new Token(TokenType.Symbol, "/)"); }
							return new Token(TokenType.Symbol, "/");
						case '!':
							if (SafePeek() == '=') { input.Dequeue(); return new Token(TokenType.Symbol, "!="); }
							AddError("Illegal symbol '!'");
							return new Token(TokenType.Symbol, "!");
						case '=':
							if (SafePeek() == '=') { input.Dequeue(); return new Token(TokenType.Symbol, "=="); }
							AddError("Illegal symbol '='");
							return new Token(TokenType.Symbol, "=");
						case '>':
							if (SafePeek() == '=') { input.Dequeue(); return new Token(TokenType.Symbol, ">="); }
							return new Token(TokenType.Symbol, ">");
						case '<':
							if (SafePeek() == '=') { input.Dequeue(); return new Token(TokenType.Symbol, "<="); }
							return new Token(TokenType.Symbol, "<");
						case '(':
							return new Token(TokenType.Symbol, "(");
						case ')':
							return new Token(TokenType.Symbol, ")");
						//case ',':
						//    return new Token(TokenType.Symbol, ",");
						case '&':
							if (SafePeek() == '&') { input.Dequeue(); return new Token(TokenType.Symbol, "&&"); }
							AddError("Illegal symbol '&'");
							return new Token(TokenType.Symbol, "&");
						case '|':
							if (SafePeek() == '|') { input.Dequeue(); return new Token(TokenType.Symbol, "||"); }
							AddError("Illegal symbol '|'");
							return new Token(TokenType.Symbol, "|");
						case '.':
							return new Token(TokenType.Symbol, ".");
						default:
							AddError("Unexpected character");
							SkipLine();
							break;
					}
			}
		}
		private void PopTokenInternal()
		{
			Token t;
			t = PopTokenInternal2();
			storedTokens.Enqueue(t);
		}

		private Token DequeueToken()
		{
			if (storedTokens.Count == 0) return Token.Null;
			Token t = storedTokens.Dequeue();
			if (t.type == TokenType.Unknown)
			{
				if (localVars.Contains(t.token)) return new Token(TokenType.Local, t.token, t.utoken);
				if (globalVars.Contains(t.token)) return new Token(TokenType.Global, t.token, t.utoken);
				if (functions.Contains(t.token)) return new Token(TokenType.Function, t.token, t.utoken);
				if (edids.Contains(t.token)) return new Token(TokenType.edid, t.token, t.utoken);
			}
			return t;
		}

		private Token[] lastTokens;
		private readonly List<Token> getNextStatementTokens = new List<Token>();
		public Token[] PopNextStatement()
		{
			if (lastTokens != null)
			{
				Token[] tmp = lastTokens;
				lastTokens = null;
				return tmp;
			}
			line++;
			Token t = DequeueToken();
			while (t.IsSymbol("\n"))
			{
				line++;
				t = DequeueToken();
			}
			if (storedTokens.Count == 0) return new Token[0];
			getNextStatementTokens.Clear();
			while (t.type != TokenType.Null && !t.IsSymbol("\n"))
			{
				getNextStatementTokens.Add(t);
				t = DequeueToken();
			}
			return getNextStatementTokens.ToArray();
		}
		public Token[] PeekNextStatement()
		{
			if (lastTokens == null) lastTokens = PopNextStatement();
			return lastTokens;
		}

		public TokenStream(string file, List<string> errors)
		{
			this.errors = errors;
			line = 1;
			input = new Queue<char>(file.ToCharArray());
			input.Enqueue('\n');
			storedTokens = new Queue<Token>();
			while (input.Count > 0) PopTokenInternal();
			line = 0;
		}
	}
}
