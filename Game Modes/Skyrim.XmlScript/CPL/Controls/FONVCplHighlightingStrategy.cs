using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls;

namespace Nexus.Client.Games.Skyrim.Scripting.XmlScript.CPL.Controls
{
	/// <summary>
	/// The highlighting strategy for the Skyrim variant of the Conditional Pattern Language.
	/// </summary>
	/// <remarks>
	/// This is used to highilight CPL in the editor.
	/// </remarks>
	public class SKYRIMCplHighlightingStrategy : CplHighlightingStrategy
	{
		#region Constructors

		/// <summary>
		/// A simple construstor that sets the <see cref="ICplParserFactory"/> to use.
		/// </summary>
		/// <param name="p_cpfLexerFactory">The factory to use to create the CPL lexer used to highlight the code.</param>
		public SKYRIMCplHighlightingStrategy(ICplParserFactory p_cpfLexerFactory)
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
				case SKYRIMCplLexer.NVSE_VERSION:
					return TokenClass.Keyword;
				default:
					return base.GetTokenClass(p_intTokenType);
			}
		}
	}
}
