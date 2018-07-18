using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL.Controls
{
	/// <summary>
	/// The highlighting strategy for the FO3 variant of the Conditional Pattern Language.
	/// </summary>
	/// <remarks>
	/// This is used to highilight CPL in the editor.
	/// </remarks>
	public class FO3CplHighlightingStrategy : CplHighlightingStrategy
	{
		#region Constructors

		/// <summary>
		/// A simple construstor that sets the <see cref="ICplParserFactory"/> to use.
		/// </summary>
		/// <param name="p_cpfLexerFactory">The factory to use to create the CPL lexer used to highlight the code.</param>
		public FO3CplHighlightingStrategy(ICplParserFactory p_cpfLexerFactory)
			: base(p_cpfLexerFactory)
		{
		}

		#endregion

		/// <summary>
		/// Determines the token class of the given token type.
		/// </summary>
		/// <param name="p_intTokenType">The token type to classify.</param>
		protected override TokenClass GetTokenClass(int p_intTokenType)
		{
			switch (p_intTokenType)
			{
				case FO3CplLexer.FOSE_VERSION:
					return TokenClass.Keyword;
				default:
					return base.GetTokenClass(p_intTokenType);
			}
		}
	}
}
