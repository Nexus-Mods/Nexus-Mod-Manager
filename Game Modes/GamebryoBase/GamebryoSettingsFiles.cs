
namespace Nexus.Client.Games.Gamebryo
{
	/// <summary>
	/// The paths of the settings files used by Gamebryo based games.
	/// </summary>
	public class GamebryoSettingsFiles : FileSet
	{
		/// <summary>
		/// Gets or sets the path of the ini file.
		/// </summary>
		/// <value>The path of the ini file.</value>
		public string IniPath
		{
			get
			{
				return this["GamebryoIniPath"];
			}
			set
			{
				this["GamebryoIniPath"] = value;
			}
		}

		/// <summary>
		/// Gets or sets the path of the Renderer file.
		/// </summary>
		/// <value>The path of the Renderer file.</value>
		public string RendererFilePath
		{
			get
			{
				return this["RendererFilePath"];
			}
			set
			{
				this["RendererFilePath"] = value;
			}
		}

		/// <summary>
		/// Gets or sets the path of the plugins.txt file.
		/// </summary>
		/// <value>The path of the plugins.txt file.</value>
		public string PluginsFilePath
		{
			get
			{
				return this["PluginsFilePath"];
			}
			set
			{
				this["PluginsFilePath"] = value;
			}
		}
	}
}
