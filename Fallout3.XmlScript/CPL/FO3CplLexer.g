lexer grammar FO3CplLexer;

options {
	language=CSharp2;
	superClass=CPLLexerBase;
}

import CPLLexer;

@namespace {Nexus.Client.ModManagement.Scripting.XmlScript.CPL}

@lexer::header {
//turn off warning about missing comments
#pragma warning disable 1591
//turn off warning about not needing CLSCompliant attribute
#pragma warning disable 3021
}

@lexer::members {
	public const System.Int32 HIDDEN = TokenChannels.Hidden;
}

/*------------------------------------------------------------------
 * LEXER RULES
 *------------------------------------------------------------------*/

FOSE_VERSION	: F O S E V E R S I O N;
