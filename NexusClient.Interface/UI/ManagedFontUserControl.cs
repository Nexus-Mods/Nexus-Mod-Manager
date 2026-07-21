using System.Windows.Forms;
using Nexus.UI.Controls;

namespace Nexus.Client.UI
{
	/// <summary>
	/// A base user control that has a <see cref="FontProvider"/> already declared.
	/// </summary>
	/// <remarks>
	/// This is to make it easier to use the font provider.
	/// </remarks>
	public class ManagedFontUserControl : UserControl
	{
		/// <summary>
		/// The <see cref="FontProvider"/> for the control.
		/// </summary>
		protected FontProvider m_fpdFontProvider = null;

		#region Contructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ManagedFontUserControl()
		{
			m_fpdFontProvider = new FontProvider();
		}

		#endregion
	}
}
