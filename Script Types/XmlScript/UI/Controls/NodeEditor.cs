using Nexus.UI.Controls;
using System.Windows.Forms;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	/// <summary>
	/// The base class for editors of parts of an <see cref="XmlScript"/>.
	/// </summary>
	public class NodeEditor : UserControl
	{
		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public virtual IViewModel GetViewModel()
		{
			return null;
		}
	}
}
