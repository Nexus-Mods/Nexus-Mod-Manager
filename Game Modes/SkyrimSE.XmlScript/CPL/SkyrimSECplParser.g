parser grammar SkyrimSECplParser;

options {
	language=CSharp3;
	output=AST;    
	ASTLabelType=CommonTree;
	superClass=AntlrParserBase;
	tokenVocab=SkyrimSECplLexer;
}

import CPLImportParser;

@namespace {Nexus.Client.ModManagement.Scripting.XmlScript.CPL}

@parser::header {
	//turn off warning about missing comments
	#pragma warning disable 1591
	//turn off warning about not needing CLSCompliant attribute
	#pragma warning disable 3021
	using Nexus.Client.Util.Antlr;
}

@parser::members {
	//this is needed as there is a bug in the code generator
	// the generated constructor tries to set the tree before setting
	// the delegate parser, which causes an exception to be thrown.
	public SkyrimSECplParser(ITokenStream input, string dummy)
		: base(input, new RecognizerSharedState())
	{
		gCPLImportParser = new SkyrimSECplParser_CPLImportParser(input, state, this);
		ITreeAdaptor treeAdaptor = null;
		CreateTreeAdaptor(ref treeAdaptor);
		TreeAdaptor = treeAdaptor ?? new CommonTreeAdaptor();

		OnCreated();
	}
	
	/// <summary>
	/// Sets the error tracker to use to track errors.
	/// </summary>
	/// <param name="p_ertErrorTracker">The error tracker to use to log
	/// parsing errors.</param>
	public void SetErrorTracker(ErrorTracker p_ertErrorTracker)
	{
		ErrorTracker = p_ertErrorTracker;
		gCPLImportParser.ErrorTracker = p_ertErrorTracker;
	}
	
	/// <summary>
	/// Parses the input.
	/// </summary>
	/// <returns>The parsed input.</returns>
	public override ITree Parse()
	{
		return stmt().Tree;
	}
}

/*------------------------------------------------------------------
 * PARSER RULES
 *------------------------------------------------------------------*/

public factor		: file_check
						| flag_check
						| game_ver_check
						| skse_ver_check
						| manager_ver_check
						| LB! expr RB!;
skse_ver_check		: SKSE_VERSION ATLEAST^ VERSION;