using System.Collections.Generic;
using System.IO;
using System.ServiceModel;
using System.ServiceModel.Web;
using Nexus.Client.Mods;
using Nexus.Client.ModManagement;

namespace Nexus.Client.ModRepositories.Nexus
{
	/// <summary>
	/// Describes the methods and properties of the Nexus mod repository.
	/// </summary>
	/// <remarks>
	/// The Nexus mod repository is the repository hosted with the Nexus group of websites.
	/// </remarks>
	[ServiceContract]
	public interface INexusModRepositoryApi
	{
		/// <summary>
		/// Logs a user into the repository.
		/// </summary>
		/// <param name="p_strUsername">The username to authenticate.</param>
		/// <param name="p_strPassword">The password to authenticate.</param>
		/// <returns>An authentication token if the credentials are valid; <c>null</c> otherwise.</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Sessions/?Login&username={p_strUsername}&password={p_strPassword}",
			ResponseFormat = WebMessageFormat.Json)]
		string Login(string p_strUsername, string p_strPassword);

		/// <summary>
		/// Validates the current security tokens.
		/// </summary>
		/// <returns>An authentication token if the tokens are valid; <c>null</c> otherwise.</returns>
		[OperationContract]
		[WebInvoke(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Sessions/?Validate",
			ResponseFormat = WebMessageFormat.Json)]
		string ValidateTokens();

		/// <summary>
		/// Logs a user into the repository.
		/// </summary>
		/// <param name="p_strUsername">The username to authenticate.</param>
		/// <param name="p_strPassword">The password to authenticate.</param>
		/// <returns>An authentication token if the credentials are valid; <c>null</c> otherwise.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Sessions/?Login&username={p_strUsername}&password={p_strPassword}",
			ResponseFormat = WebMessageFormat.Json)]
		string LoginPOST(string p_strUsername, string p_strPassword);

		/// <summary>
		/// Validates the current security tokens.
		/// </summary>
		/// <returns>An authentication token if the tokens are valid; <c>null</c> otherwise.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Sessions/?Validate",
			ResponseFormat = WebMessageFormat.Json)]
		string ValidateTokensPOST();

		/// <summary>
		/// Toggles the mod Endorsement state.
		/// </summary>
		/// <param name="p_strModId">The mod ID.</param>
		/// <param name="p_intLocalState">The local Endorsement state.</param>
		/// <returns>The updated online Endorsement state.</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Mods/toggleendorsement/{p_strModId}?lvote={p_intLocalState}&game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		bool ToggleEndorsement(string p_strModId, int p_intLocalState, int p_intGameId);

		/// <summary>
		/// Gets the info about the specified mod from the repository.
		/// </summary>
		/// <param name="p_strModId">The id of the mod for which to retrieved the metadata.</param>
		/// <returns>The info about the specified mod from the repository.</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Mods/{p_strModId}/?game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		NexusModInfo GetModInfo(string p_strModId, int p_intGameId);

		/// <summary>
		/// Gets the info about the specified mod list from the repository.
		/// </summary>
		/// <param name="p_strModList">The mod list for which to retrieved the metadata.</param>
		/// <returns>The info about the specified mod list from the repository.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Mods/GetUpdates?ModList={p_strModList}&game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		NexusModInfo[] GetModListInfo(string p_strModList, int p_intGameId);

		/// <summary>
		/// Gets the info about the specified file list from the repository.
		/// </summary>
		/// <param name="p_strFileList">The file list for which to retrieved the metadata.</param>
		/// <returns>The info about the specified file list from the repository.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Files/GetUpdates?FileList={p_strFileList}&game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		NexusModInfo[] GetFileListInfo(string p_strFileList, int p_intGameId);

		/// <summary>
		/// Gets the files associated with the specified mod from the repository.
		/// </summary>
		/// <param name="p_strModId">The id of the mod for which to retrieved the associated files.</param>
		/// <returns>The files associated with the specified mod from the repository.</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Files/indexfrommod/{p_strModId}?game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		List<NexusModFileInfo> GetModFiles(string p_strModId, int p_intGameId);

		/// <summary>
		/// Gets the file info for the specified download file of the specified mod.
		/// </summary>
		/// <param name="p_strFileId">The id of the download file whose metadata is to be retrieved.</param>
		/// <returns>The file info for the specified download file of the specified mod.</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Files/{p_strFileId}/?game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		NexusModFileInfo GetModFile(string p_strFileId, int p_intGameId);

		/// <summary>
		/// Gets the download URLs of all the parts associated with the specified file.
		/// </summary>
		/// <param name="p_strFileId">The id of the file for which to retrieve the URLs.</param>
		/// <returns>The download URLs of all the parts associated with the specified file.</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Files/download/{p_strFileId}/?game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		List<FileserverInfo> GetModFileDownloadUrls(string p_strFileId, int p_intGameId);

		/// <summary>
		/// Gets the user credentials.
		/// </summary>
		/// <returns>The user credentials (User ID, Name and Status).</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Core/Libs/Flamework/Entities/User?GetCredentials&game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		string[] GetCredentials(int p_intGameId);

		/// <summary>
		/// Finds the mods containing the given search terms.
		/// </summary>
		/// <param name="p_strModNameSearchString">The terms to use to search for mods.</param>
		/// <param name="p_strType">Whether the returned mods' names should include all of
		/// the given search terms, or any of the terms.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Mods/Find/?name={p_strModNameSearchString}&type={p_strType}&game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		List<NexusModInfo> FindMods(string p_strModNameSearchString, string p_strType, int p_intGameId);

		/// <summary>
		/// Finds the mods for the given Author.
		/// </summary>
		/// <param name="p_strModNameSearchString">The terms to use to search for mods.</param>
		/// <param name="p_strType">Whether the returned mods' names should include all of
		/// the given search terms, or any of the terms.</param>
		/// <param name="p_strAuthorSearchString">The Author to use to search for mods.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		[OperationContract]
		[WebGet(
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Mods/Find/?name={p_strModNameSearchString}&author={p_strAuthorSearchString}&type={p_strType}&game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		List<NexusModInfo> FindModsAuthor(string p_strModNameSearchString, string p_strType, string p_strAuthorSearchString, int p_intGameId);

		/// <summary>
		/// Gets the Online Profile Id list.
		/// </summary>
		/// <returns>The Download Request array.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "profiles/GetUserProfiles/?game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		List<int> GetUserProfiles(int p_intGameId);

		/// <summary>
		/// Gets the Profile Data.
		/// </summary>
		/// <returns>The Download Request array.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "profiles/GetProfileData/?game_id={p_intGameId}&profile_id={p_intProfileId}",
			ResponseFormat = WebMessageFormat.Json)]
		IModProfileInfo GetProfileData(int p_intGameId, int p_intProfileId);

		/// <summary>
		/// Gets the Profile Data.
		/// </summary>
		/// <returns>The Download Request array.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "profiles/ChangeStatus/?game_id={p_intGameId}&profile_id={p_intProfileId}&share={p_intShare}",
			ResponseFormat = WebMessageFormat.Json)]
		string ToggleSharing(int p_intGameId, int p_intProfileId, int p_intShare);

		/// <summary>
		/// Rename the Profile.
		/// </summary>
		/// <returns>Rename the Profile.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "profiles/RenameProfile/?game_id={p_intGameId}&profile_id={p_intProfileId}&name={p_strName}",
			ResponseFormat = WebMessageFormat.Json)]
		string RenameProfile(int p_intGameId, int p_intProfileId, string p_strName);
		
		/// <summary>
		/// Remove the Profile.
		/// </summary>
		/// <returns>Remove the Profile.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "profiles/RemoveProfile/?game_id={p_intGameId}&profile_id={p_intProfileId}",
			ResponseFormat = WebMessageFormat.Json)]
		string RemoveProfile(int p_intGameId, int p_intProfileId);

		/// <summary>
		/// Gets the Missing Files.
		/// </summary>
		/// <returns>The Missing files.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "profiles/GetMissingFiles/?game_id={p_intGameId}&profile_id={p_intProfileId}",
			ResponseFormat = WebMessageFormat.Json)]
		List<ProfileMissingModInfo> GetMissingFiles(int p_intGameId, int p_intProfileId);

		/// <summary>
		/// Gets the Missing Files.
		/// </summary>
		/// <returns>The Missing files.</returns>
		[OperationContract]
		[WebInvoke(Method = "POST",
			BodyStyle = WebMessageBodyStyle.Bare,
			UriTemplate = "Mods/GetCategories/?game_id={p_intGameId}",
			ResponseFormat = WebMessageFormat.Json)]
		List<CategoriesInfo> GetCategories(int p_intGameId);
	}
}
