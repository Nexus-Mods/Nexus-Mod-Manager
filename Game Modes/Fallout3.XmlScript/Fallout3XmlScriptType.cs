using System.Xml.Linq;
using Nexus.Client.Games.Fallout3.Scripting.XmlScript.Parsers;
using Nexus.Client.Games.Fallout3.Scripting.XmlScript.Unparsers;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.Parsers;
using Nexus.Client.ModManagement.Scripting.XmlScript.Unparsers;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls;
using System;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript
{
	/// <summary>
	/// Describes the Fallout 3 variant of the XML script type.
	/// </summary>
	public class Fallout3XmlScriptType : XmlScriptType
	{
		#region Properties

		/// <summary>
		/// Gets the path to the game-specific xml script schema files.
		/// </summary>
		/// <value>The path to the game-specific xml script schema files.</value>
		protected override string GameSpecificXMLScriptSchemaPath
		{
			get
			{
				return "Nexus/Client/Games/Fallout3/Scripting/XmlScript/data";
			}
		}

		#endregion

		#region Serialization

		/// <summary>
		/// Gets the parser to use to parse the given XML Script.
		/// </summary>
		/// <remarks>
		/// The given XML Script is only required to extract the script version, which is used to
		/// determine which parser should be used.
		/// </remarks>
		/// <param name="p_xelScript">The XML Script to parse.</param>
		/// <returns>The parser to use to parse the given XML Script.</returns>
		protected override IParser GetParser(XElement p_xelScript)
		{
			string strScriptVersion = GetXmlScriptVersion(p_xelScript).ToString();
			switch (strScriptVersion)
			{
				case "1.0":
					return new Fallout3Parser10(p_xelScript, this);
				case "2.0":
					return new Fallout3Parser20(p_xelScript, this);
				case "3.0":
					return new Fallout3Parser30(p_xelScript, this);
				case "4.0":
					return new Fallout3Parser40(p_xelScript, this);
				case "5.0":
					return new Fallout3Parser50(p_xelScript, this);
			}
			throw new ParserException("Unrecognized XML Script version (" + strScriptVersion + "). Perhaps a newer version of the mod manager is required.");
		}

		/// <summary>
		/// Gets the unparser to use to create an XML representation of the given <see cref="XmlScript"/>.
		/// </summary>
		/// <param name="p_xscScript">The <see cref="XmlScript"/> to unparse.</param>
		/// <returns>The unparser to use to unparse the given XML Script.</returns>
		protected override IUnparser GetUnparser(Nexus.Client.ModManagement.Scripting.XmlScript.XmlScript p_xscScript)
		{
			switch (p_xscScript.Version.ToString())
			{
				case "1.0":
					return new Fallout3Unparser10(p_xscScript);
				case "2.0":
					return new Fallout3Unparser20(p_xscScript);
				case "3.0":
					return new Fallout3Unparser30(p_xscScript);
				case "4.0":
					return new Fallout3Unparser40(p_xscScript);
				case "5.0":
					return new Fallout3Unparser50(p_xscScript);

			}
			throw new ParserException("Unrecognized XML Script version (" + p_xscScript.Version + "). Perhaps a newer version of the mod manager is required.");
		}

		#endregion

		/// <summary>
		/// The factory method that returns the appropriate <see cref="IXmlScriptNodeAdapter"/> for
		/// the given xml script version.
		/// </summary>
		/// <param name="p_verXmlScriptVersion">The xml script version for which to create an
		/// <see cref="IXmlScriptNodeAdapter"/>.</param>
		/// <returns>The appropriate <see cref="IXmlScriptNodeAdapter"/> for the given xml script version.</returns>
		/// <exception cref="Exception">Thrown if no <see cref="IXmlScriptNodeAdapter"/> is
		/// found for the given xml script version.</exception>
		public override IXmlScriptNodeAdapter GetXmlScriptNodeAdapter(Version p_verXmlScriptVersion)
		{
			switch (p_verXmlScriptVersion.ToString())
			{
				case "1.0":
					return new XmlScript10NodeAdapter(this);
				case "2.0":
					return new XmlScript20NodeAdapter(this);
				case "3.0":
					return new XmlScript30NodeAdapter(this);
				case "4.0":
				case "5.0":
					return new XmlScript40NodeAdapter(this);
			}
			throw new Exception("Unrecognized Xml Script version (" + p_verXmlScriptVersion + "). Perhaps a newer version of the client is required.");
		}

		/// <summary>
		/// Gets a CPL Parser factory.
		/// </summary>
		/// <returns>A CPL Parser factory.</returns>
		public override ICplParserFactory GetCplParserFactory()
		{
			return new FO3CplParserFactory();
		}

		/// <summary>
		/// Creates a <see cref="ConditionStateManager"/> to use when running an XML script.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The game mode currently bieng managed.</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <returns>A <see cref="ConditionStateManager"/> to use when running an XML script.</returns>
		public override ConditionStateManager CreateConditionStateManager(IMod p_modMod, IGameMode p_gmdGameMode, IPluginManager p_pmgPluginManager, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			return new Fallout3ConditionStateManager(p_modMod, p_gmdGameMode, p_pmgPluginManager, p_eifEnvironmentInfo);
		}
	}
}
