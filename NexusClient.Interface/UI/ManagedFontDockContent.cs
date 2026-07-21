using Nexus.UI.Controls;
using WeifenLuo.WinFormsUI.Docking;

namespace Nexus.Client.UI
{
	/// <summary>
	/// A base dock content that has a <see cref="FontProvider"/> already declared.
	/// </summary>
	/// <remarks>
	/// This is to make it easier to use the font provider.
	/// </remarks>
	public class ManagedFontDockContent : DockContent
	{
		/// <summary>
		/// The <see cref="FontProvider"/> for the form.
		/// </summary>
		protected FontProvider m_fpdFontProvider = null;

		#region Contructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ManagedFontDockContent()
		{
			m_fpdFontProvider = new FontProvider();
		}

		#endregion
	}
}
