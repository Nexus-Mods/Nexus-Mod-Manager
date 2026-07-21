using Nexus.UI.Controls;
using System.Collections.Generic;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Describes the properties and methods of an editor for a specific script type.
	/// </summary>
	public interface IScriptEditor
	{
		/// <summary>
		/// Gets the name of the script type being edited.
		/// </summary>
		/// <value>The name of the script type being edited.</value>
		string ScriptTypeName { get; }

		/// <summary>
		/// Sets the files in the mod whose script is being edited.
		/// </summary>
		/// <value>The files in the mod whose script is being edited.</value>
		IList<VirtualFileSystemItem> ModFiles { set; }

		/// <summary>
		/// Gets or sets the script being edited.
		/// </summary>
		/// <value>The script being edited.</value>
		IScript Script { get; set; }
	}
}
