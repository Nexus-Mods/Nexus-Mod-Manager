using System.Xml.Linq;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.Unparsers;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript.Unparsers
{
	/// <summary>
	/// Unparses the Fallout 3 variety of the version 4.0 XML scripts.
	/// </summary>
	public class Fallout3Unparser40 : Unparser40
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xscScript">The script to unparse.</param>
		public Fallout3Unparser40(Nexus.Client.ModManagement.Scripting.XmlScript.XmlScript p_xscScript)
			: base(p_xscScript)
		{
		}

		#endregion

		/// <summary>
		/// Unparses the given <see cref="ICondition"/>.
		/// </summary>
		/// <remarks>
		/// This checks the see if the given <see cref="ICondition"/> is a <see cref="FoseCondition"/>. If it
		/// is, it is unparsed, otherwise unparsing is delegated to the base class.
		/// </remarks>
		/// <param name="p_cndCondition">The <see cref="ICondition"/> for which to generate XML.</param>
		/// <returns>The XML representation of the given <see cref="ICondition"/>.</returns>
		protected override XElement UnparseCondition(ICondition p_cndCondition)
		{
			return FO3Unparser20Helper.UnparseCondition(p_cndCondition) ?? base.UnparseCondition(p_cndCondition);
		}
	}
}
