using System;
using System.ComponentModel;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A converter for values passed the the <see cref="VerticalTabControl.SelectedTabPage"/>
	/// property.
	/// </summary>
	/// <remarks>
	/// This converter ensures that only <see cref="VerticalTabPage"/>s that are in the
	/// <see cref="VerticalTabControl"/> can be set as the selected tab.
	/// </remarks>
	public class SelectedVerticalTabPageConverter : ReferenceConverter
	{
		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public SelectedVerticalTabPageConverter()
			: base(typeof(VerticalTabPage))
		{
		}

		#endregion

		/// <summary>
		/// Determins if the specified value is allowed.
		/// </summary>
		/// <param name="context">The context of the value.</param>
		/// <param name="value">The value to which to set the property</param>
		/// <returns><c>true</c> if the given value is a <see cref="VerticalTabPage"/>
		/// in the <see cref="VerticalTabControl"/>; <c>false</c> otherwise.</returns>
		protected override bool IsValueAllowed(ITypeDescriptorContext context, object value)
		{
			if (context != null)
			{
				VerticalTabControl vtcTabControl = (VerticalTabControl)context.Instance;
				return vtcTabControl.TabPages.Contains((VerticalTabPage)value);
			}
			return false;

		}
	}
}
