using DevExpress.XtraEditors;
using Nexus.UI.Controls;

namespace Nexus.Client.UI
{
	/// <summary>
	/// Provides a DevExpress form base with NMM's shared <see cref="FontProvider"/> support.
	/// </summary>
	public class ManagedFontXtraForm : XtraForm
	{
		/// <summary>
		/// The <see cref="FontProvider"/> used by the form.
		/// </summary>
		protected readonly FontProvider m_fpdFontProvider;

		/// <summary>
		/// Initializes a new instance of the <see cref="ManagedFontXtraForm"/> class.
		/// </summary>
		public ManagedFontXtraForm()
		{
			m_fpdFontProvider = new FontProvider();
		}
	}
}
