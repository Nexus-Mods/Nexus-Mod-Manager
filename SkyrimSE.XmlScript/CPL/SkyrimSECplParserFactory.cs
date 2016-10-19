using Antlr.Runtime;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using Nexus.Client.Util.Antlr;

namespace Nexus.Client.Games.SkyrimSE.Scripting.XmlScript.CPL
{
	/// <summary>
	/// Creates CPL parsers for the SkyrimSE variant of CPL.
	/// </summary>
	public class SkyrimSECplParserFactory : ICplParserFactory
	{
		#region ICplParserFactory Members

		/// <summary>
		/// Creates a CPL parser for the given code, using the given error tracker.
		/// </summary>
		/// <param name="p_strCode">The code be parsed.</param>
		/// <param name="p_ertErrorTracker">The error tracker to use to log
		/// parsing errors.</param>
		/// <returns>A CPL parser for the given code.</returns>
		public AntlrParserBase CreateParser(string p_strCode, ErrorTracker p_ertErrorTracker)
		{
			AntlrLexerBase lexLexer = CreateLexer(p_strCode, p_ertErrorTracker);
			CommonTokenStream ctsTokens = new CommonTokenStream(lexLexer);
			SkyrimSECplParser prsParser = new SkyrimSECplParser(ctsTokens, "");
			prsParser.SetErrorTracker(p_ertErrorTracker);
			return prsParser;
		}

		/// <summary>
		/// Creates a CPL lexer for the given code, using the given error tracker.
		/// </summary>
		/// <param name="p_strCode">The code be lexed.</param>
		/// <param name="p_ertErrorTracker">The error tracker to use to log
		/// lexing errors.</param>
		/// <returns>A CPL lexer for the given code.</returns>
		public AntlrLexerBase CreateLexer(string p_strCode, ErrorTracker p_ertErrorTracker)
		{
			SkyrimSECplLexer lexLexer = new SkyrimSECplLexer(new ANTLRStringStream(p_strCode));
			lexLexer.SetErrorTracker(p_ertErrorTracker);
			return lexLexer;
		}

		#endregion
	}
}
