using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.TipsManagement
{
	class Tips : ITips
	{
		#region Properties

		/// <summary>
		/// Gets or sets the Id of the tip.
		/// </summary>
		/// <remarks>The id of the tip</remarks>
		public Int32 TipId { get; set; }

		/// <summary>
		/// Gets or sets the text of the tip.
		/// </summary>
		/// <value>The text of the tip.</value>
		public string TipText { get; set; }

        /// <summary>
        /// Gets or sets the section of the UI.
        /// </summary>
        /// <value>The section of the UI.</value>
        public string TipSection { get; set; }
        		
		/// <summary>
		/// Gets or sets the object of the UI.
		/// </summary>
		/// <value>The object of the UI.</value>
		public string TipObject { get; set; }

        /// <summary>
        /// Gets or sets the NMM version.
        /// </summary>
        /// <value>The object of the UI.</value>
        public string TipVersion { get; set; }
		
		#endregion

		#region Constructors

		public Tips(Int32 p_intTipId, string p_strTipText,string p_strTipSection, string p_strTipObject, string p_strTipVersion)
		{
			TipId = p_intTipId;
			TipText = p_strTipText;
			TipObject = p_strTipObject;
            TipSection = p_strTipSection;
            TipVersion = p_strTipVersion;
		}

		#endregion
	}
}
