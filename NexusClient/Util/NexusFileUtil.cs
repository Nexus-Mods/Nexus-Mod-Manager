
namespace Nexus.Client.Util
{
	/// <summary>
	/// Utility functions to work with files.
	/// </summary>
	public class NexusFileUtil : FileUtil
	{
		#region Properties

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public NexusFileUtil(IEnvironmentInfo p_eifEnvironmentInfo)
			: base()
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		/// <summary>
		/// Creates a temporary directory.
		/// </summary>
		/// <returns>The path to the newly created temporary directory.</returns>
		public override string CreateTempDirectory()
		{
			return CreateTempDirectory(EnvironmentInfo.TemporaryPath);
		}
	}
}
