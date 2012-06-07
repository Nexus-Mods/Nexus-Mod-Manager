using System.Windows.Forms;
using Nexus.UI.Controls;

namespace Nexus.Client.UI
{
	/// <summary>
	/// A base form that has a <see cref="FontProvider"/> already declared.
	/// </summary>
	/// <remarks>
	/// This is to make it easier to use the font provider.
	/// </remarks>
	public class ManagedFontForm : Form
	{
		protected FontProvider m_fpdFontProvider = null;

		#region Contructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ManagedFontForm()
		{
			m_fpdFontProvider = new FontProvider();
		}

		#endregion
	}
}
