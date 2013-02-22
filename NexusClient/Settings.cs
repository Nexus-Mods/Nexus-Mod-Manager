using Nexus.Client.Settings;

namespace Nexus.Client.Properties
{
	/// <summary>
	/// This class adds the <see cref="ISettings"/> to the project's <see cref="Properties.Settings"/>
	/// class.
	/// </summary>
	/// <remarks>
	/// This file should not contain any memebers or properties.
	/// </remarks>
	internal sealed partial class Settings : ISettings
	{
		/// <summary>
		/// Gets the full name of the mod manager.
		/// </summary>
		/// <value>The full name of the mod manager.</value>
		public string ModManagerName
		{
			get
			{
				return ProgrammeMetadata.ModManagerName;
			}
		}
	}
}
