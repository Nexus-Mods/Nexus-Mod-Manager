using System;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// This marks a class as a view model.
	/// </summary>
	public interface IViewModel
	{
		/// <summary>
		/// Validates the view model.
		/// </summary>
		/// <returns><c>true</c> if the model is valid;
		/// <c>false</c> otherwise.</returns>
		bool Validate();
	}
}
