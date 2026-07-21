using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.ModAuthoring.UI.Controls
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display a mod install script editor.
	/// </summary>
	public class InstallScriptEditorVM : ModScriptEditorVM
	{
		#region Properties

		/// <summary>
		/// Gets or sets the install script being edited.
		/// </summary>
		/// <value>The install script being edited.</value>
		protected override IScript EditedScript
		{
			get
			{
				return EditedMod.InstallScript;
			}
			set
			{
				EditedMod.InstallScript = value;
			}
		}

		#endregion
		
		#region Constructors

		/// <summary>
		/// A siple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_stgScriptTypeRegistry">The <see cref="IScriptTypeRegistry"/> of available <see cref="IScriptType"/>s.</param>
		public InstallScriptEditorVM(IScriptTypeRegistry p_stgScriptTypeRegistry)
			: base(p_stgScriptTypeRegistry)
		{
		}

		#endregion
	}
}
