
using Nexus.Client.Settings;
using System;
namespace Nexus.Client
{
	/// <summary>
	/// Provides information about the current programme environment.
	/// </summary>
	public interface IEnvironmentInfo
	{
		#region Properties

		/// <summary>
		/// Gets the path to the mod manager's folder in the user's personal data folder.
		/// </summary>
		/// <value>The path to the mod manager's folder in the user's personal data folder.</value>
		string ApplicationPersonalDataFolderPath { get; }

		/// <summary>
		/// Gets the path to the user's personal data folder.
		/// </summary>
		/// <value>The path to the user's personal data folder.</value>
		string PersonalDataFolderPath { get; }

		/// <summary>
		/// Gets whether the programme is running under the Mono framework.
		/// </summary>
		/// <value>Whether the programme is running under the Mono framework.</value>
		bool IsMonoMode { get; }

		/// <summary>
		/// Gets the temporary path used by the application.
		/// </summary>
		/// <value>The temporary path used by the application.</value>
		string TemporaryPath { get; }

		/// <summary>
		/// Gets the path to the directory where programme data is stored.
		/// </summary>
		/// <value>The path to the directory where programme data is stored.</value>
		string ProgrammeInfoDirectory { get; }

		/// <summary>
		/// Gets whether the current process is 64bit.
		/// </summary>
		/// <value>Whether the current process is 64bit.</value>
		bool Is64BitProcess { get; }

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		ISettings Settings { get; }

		/// <summary>
		/// Gets the version of the running application.
		/// </summary>
		/// <value>The version of the running application.</value>
		Version ApplicationVersion { get; }

		#endregion
	}
}
