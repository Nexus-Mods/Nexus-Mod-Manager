//turn off warning about missing comments
#pragma warning disable 1591
//turn off warning about not needing CLSCompliant attribute
#pragma warning disable 3021
using Antlr.Runtime;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL
{
	/// <summary>
	/// Creates CPL parsers for the Fallout 3 variant of CPL.
	/// </summary>
	public class FO3CplParserFactory : ICplParserFactory
	{
		#region ICplParserFactory Members

		/// <summary>
		/// Creates a CPL parser for the given code, using the given error tracker.
		/// </summary>
		/// <param name="p_strCode">The code be parsed.</param>
		/// <param name="p_ertErrorTracker">The error tracker to use to log
		/// parsing errors.</param>
		/// <returns>A CPL parser for the given code.</returns>
		public CPLParserBase CreateParser(string p_strCode, ErrorTracker p_ertErrorTracker)
		{
			CPLLexerBase lexLexer = CreateLexer(p_strCode, p_ertErrorTracker);
			CommonTokenStream ctsTokens = new CommonTokenStream(lexLexer);
			FO3CplParser prsParser = new FO3CplParser(ctsTokens, "");
			prsParser.ErrorTracker = p_ertErrorTracker;
			prsParser.gCPLParser.ErrorTracker = p_ertErrorTracker;
			return prsParser;
		}

		/// <summary>
		/// Creates a CPL lexer for the given code, using the given error tracker.
		/// </summary>
		/// <param name="p_strCode">The code be lexed.</param>
		/// <param name="p_ertErrorTracker">The error tracker to use to log
		/// lexing errors.</param>
		/// <returns>A CPL lexer for the given code.</returns>
		public CPLLexerBase CreateLexer(string p_strCode, ErrorTracker p_ertErrorTracker)
		{
			FO3CplLexer lexLexer = new FO3CplLexer(new ANTLRStringStream(p_strCode));
			lexLexer.ErrorTracker = p_ertErrorTracker;
			lexLexer.gCPLLexer.ErrorTracker = p_ertErrorTracker;
			return lexLexer;
		}

		#endregion
	}
}
