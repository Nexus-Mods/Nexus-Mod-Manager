using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.TipsManagement
{
	/// <summary>
	/// Describes the properties and methods of a tip manager.
	/// </summary>
	/// <remarks>
	/// A tip manager.
	/// </remarks>
	public interface ITips
	{
		#region Properties

		/// <summary>
		/// Gets or sets the Id of the tip.
		/// </summary>
		/// <remarks>The id of the tip</remarks>
		Int32 TipId { get; set; }

		/// <summary>
		/// Gets or sets the text of the tip.
		/// </summary>
		/// <value>The text of the tip.</value>
		string TipText { get; set; }

        /// <summary>
        /// Gets or sets the section of the UI.
        /// </summary>
        /// <value>The section of the UI.</value>
        string TipSection { get; set; }

		/// <summary>
		/// Gets or sets the object of the UI.
		/// </summary>
		/// <value>The object of the UI.</value>
		string TipObject { get; set; }

        /// <summary>
        /// Gets or sets the NMM version.
        /// </summary>
        /// <value>The object of the UI.</value>
        string TipVersion { get; set; }

		#endregion
	}
}
