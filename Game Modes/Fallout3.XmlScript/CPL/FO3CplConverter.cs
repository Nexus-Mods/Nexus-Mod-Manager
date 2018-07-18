using System;
using Antlr.Runtime.Tree;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL
{
	/// <summary>
	/// A converter that translates CPL to and from an <see cref="ICondition"/>,
	/// </summary>
	public class FO3CplConverter : CPLConverter
	{
		/// <summary>
		/// A simple constructor that initializes the <see cref="ICplParserFactory"/> to use.
		/// </summary>
		/// <param name="p_pftParserFactory">The <see cref="ICplParserFactory"/> to use to create the
		/// parser to use to build the AST.</param>
		public FO3CplConverter(ICplParserFactory p_pftParserFactory)
			: base(p_pftParserFactory)
		{
		}

		/// <summary>
		/// Builds an <see cref="ICondition"/> representing the given CPL.
		/// </summary>
		/// <param name="p_astCPL">The CPL abstract syntax tree for which to create an <see cref="ICondition"/>.</param>
		/// <returns>An <see cref="ICondition"/> representing the given CPL.</returns>
		protected override ICondition BuildCompositeCondition(ITree p_astCPL)
		{
			switch (p_astCPL.Type)
			{
				case CPLParser.ATLEAST:
					string strVersion = p_astCPL.GetChild(1).Text;
					if (!strVersion.Contains("."))
						strVersion += ".0";
					Version verVersion = new Version(strVersion);
					switch (p_astCPL.GetChild(0).Type)
					{
						case FO3CplParser.FOSE_VERSION:
							return new FoseCondition(verVersion);
					}
					break;
			}
			return base.BuildCompositeCondition(p_astCPL);
		}

		/// <summary>
		/// Generates the CPL representing to given <see cref="ICondition"/>.
		/// </summary>
		/// <param name="p_cndCondition">The <see cref="ICondition"/> to translate into CPL.</param>
		/// <returns>The CPL representing to given <see cref="ICondition"/>.</returns>
		protected override string GenerateCpl(ICondition p_cndCondition)
		{
			if (p_cndCondition is FoseCondition)
			{
				FoseCondition fseCondition = (FoseCondition)p_cndCondition;
				return String.Format("foseVersion >= {0}", fseCondition.MinimumVersion.ToString());
			}
			return base.GenerateCpl(p_cndCondition);
		}
	}
}
