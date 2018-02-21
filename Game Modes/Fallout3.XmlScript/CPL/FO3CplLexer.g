lexer grammar FO3CplLexer;

options {
	language=CSharp3;
	superClass=AntlrLexerBase;
}

import CPLImportLexer;

@namespace {Nexus.Client.ModManagement.Scripting.XmlScript.CPL}

@lexer::header {
	//turn off warning about missing comments
	#pragma warning disable 1591
	//turn off warning about not needing CLSCompliant attribute
	#pragma warning disable 3021
	using Nexus.Client.Util.Antlr;
}

@lexer::members {
	public const System.Int32 HIDDEN = TokenChannels.Hidden;
	
	/// <summary>
	/// Sets the error tracker to use to track errors.
	/// </summary>
	/// <param name="p_ertErrorTracker">The error tracker to use to log
	/// parsing errors.</param>
	public void SetErrorTracker(ErrorTracker p_ertErrorTracker)
	{
		ErrorTracker = p_ertErrorTracker;
		gCPLImportLexer.ErrorTracker = p_ertErrorTracker;
	}
}

/*------------------------------------------------------------------
 * LEXER RULES
 *------------------------------------------------------------------*/

FOSE_VERSION	: F O S E V E R S I O N;
