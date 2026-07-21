using System.ComponentModel;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A converter for values passed the the <see cref="DropDownTabControl.SelectedTabPage"/>
	/// property.
	/// </summary>
	/// <remarks>
	/// This converter ensures that only <see cref="DropDownTabPage"/>s that are in the
	/// <see cref="DropDownTabControl"/> can be set as the selected tab.
	/// </remarks>
	public class SelectedDropDownTabPageConverter : ReferenceConverter
	{
		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public SelectedDropDownTabPageConverter()
			: base(typeof(DropDownTabPage))
		{
		}

		#endregion

		/// <summary>
		/// Determins if the specified value is allowed.
		/// </summary>
		/// <param name="context">The context of the value.</param>
		/// <param name="value">The value to which to set the property</param>
		/// <returns><c>true</c> if the given value is a <see cref="DropDownTabPage"/>
		/// in the <see cref="DropDownTabControl"/>; <c>false</c> otherwise.</returns>
		protected override bool IsValueAllowed(ITypeDescriptorContext context, object value)
		{
			if (context != null)
			{
				DropDownTabControl ddtTabControl = (DropDownTabControl)context.Instance;
				return ddtTabControl.TabPages.Contains((DropDownTabPage)value);
			}
			return false;

		}
	}
}
