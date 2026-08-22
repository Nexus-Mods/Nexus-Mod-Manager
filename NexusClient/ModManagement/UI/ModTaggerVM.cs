namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Nexus.Client.Games;
	using Nexus.Client.ModAuthoring.UI.Controls;
	using Nexus.Client.ModRepositories;
	using Nexus.Client.Mods;
	using Nexus.Client.Settings;
	using Nexus.Client.Util;

	/// <summary>
	/// Identifies the validation failure produced while saving Get Mod Info metadata.
	/// Kept separate from displayed text so localization cannot alter validation routing.
	/// </summary>
	public enum ModTaggerSaveError
	{
		None,
		ModNameRequired,
		InvalidWebsite,
		InvalidModId,
		InvalidFileId,
		InvalidValues
	}

	/// <summary>
	/// Encapsulates the data and operations used by the Get Mod Info dialog.
	/// </summary>
	public class ModTaggerVM
	{
		private readonly IModInfo m_mifCurrentTagOption;
		private readonly List<IModInfo> m_lstTagCandidates;
		private string m_strLoadedCandidateModId;
		private string m_strLoadedCandidateFileId;

		/// <summary>
		/// Gets the theme to use for the UI.
		/// </summary>
		public Theme CurrentTheme { get; private set; }

		/// <summary>
		/// Gets the possible Nexus metadata matches for the current archive.
		/// </summary>
		public IList<IModInfo> TagCandidates
		{
			get { return m_lstTagCandidates; }
		}

		/// <summary>
		/// Gets the view model containing the metadata currently shown in the editor.
		/// </summary>
		public ModInfoEditorVM ModInfoEditorVM { get; private set; }

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		public ISettings Settings { get; private set; }

		/// <summary>
		/// Gets the Nexus game domain used to build fallback mod links.
		/// </summary>
		public string GameDomainName
		{
			get { return ModTagger.GameDomainName; }
		}

		/// <summary>
		/// Gets the editable metadata values.
		/// </summary>
		public ModInfoVM EditedModInfo
		{
			get { return ModInfoEditorVM.EditedModInfoVM; }
		}

		/// <summary>
		/// Gets the tagger used to apply repository metadata.
		/// </summary>
		protected AutoTagger ModTagger { get; private set; }

		/// <summary>
		/// Gets the mod being tagged.
		/// </summary>
		protected IMod Mod { get; private set; }

		/// <summary>
		/// Initializes a Get Mod Info view model and resolves its candidate list once.
		/// </summary>
		/// <param name="p_atgTagger">The tagger used to retrieve and apply metadata.</param>
		/// <param name="p_modMod">The mod being edited.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		/// <param name="p_thmTheme">The current application theme.</param>
		public ModTaggerVM(AutoTagger p_atgTagger, IMod p_modMod, ISettings p_setSettings, Theme p_thmTheme)
		{
			if (p_atgTagger == null)
				throw new ArgumentNullException("p_atgTagger");
			if (p_modMod == null)
				throw new ArgumentNullException("p_modMod");

			ModTagger = p_atgTagger;
			Mod = p_modMod;
			Settings = p_setSettings;
			CurrentTheme = p_thmTheme;
			m_mifCurrentTagOption = new ModInfo(Mod);
			ModInfoEditorVM = new ModInfoEditorVM(m_mifCurrentTagOption, p_setSettings);
			ModInfoEditorVM.EditedModInfoVM.LoadInfoValues(p_modMod);
			m_lstTagCandidates = ModTagger.GetTagInfoCandidates(Mod).Where(info => info != null).ToList();
		}

		/// <summary>
		/// Loads a repository candidate into the editor without changing the target mod.
		/// </summary>
		/// <param name="p_mifInfo">The candidate metadata to display.</param>
		public void LoadTagOption(IModInfo p_mifInfo)
		{
			if (p_mifInfo == null)
				return;

			m_strLoadedCandidateModId = NormalizeText(p_mifInfo.Id);
			m_strLoadedCandidateFileId = NormalizeText(p_mifInfo.DownloadId);
			ModInfoEditorVM.EditedModInfoVM.LoadInfoValues(p_mifInfo);
		}

		/// <summary>
		/// Restores the editor to the metadata currently stored on the local archive.
		/// </summary>
		public void LoadCurrentModInfo()
		{
			m_strLoadedCandidateModId = null;
			m_strLoadedCandidateFileId = null;
			ModInfoEditorVM.EditedModInfoVM.LoadInfoValues(Mod);
		}

		/// <summary>
		/// Validates, normalizes, and applies the edited metadata to the local archive.
		/// </summary>
		/// <param name="modName">The displayed mod name.</param>
		/// <param name="version">The installed file version.</param>
		/// <param name="author">The mod author.</param>
		/// <param name="website">The website or Nexus Mods link.</param>
		/// <param name="modId">The Nexus mod identifier.</param>
		/// <param name="fileId">The Nexus file identifier.</param>
		/// <param name="description">The mod description.</param>
		/// <param name="screenshot">The optional screenshot.</param>
		/// <param name="error">The validation error, when the values cannot be saved.</param>
		/// <returns><c>true</c> when the values were saved.</returns>
		public bool TrySaveTags(string modName, string version, string author, string website, string modId, string fileId,
			string description, ExtendedImage screenshot, out ModTaggerSaveError error)
		{
			error = ModTaggerSaveError.None;
			modName = NormalizeText(modName);
			version = NormalizeText(version);
			author = NormalizeText(author);
			website = NormalizeText(website);
			modId = NormalizeText(modId);
			fileId = NormalizeText(fileId);
			description = description ?? String.Empty;

			if (String.IsNullOrEmpty(modName))
			{
				error = ModTaggerSaveError.ModNameRequired;
				return false;
			}

			Uri websiteUri = null;
			NexusModLink parsedLink = null;
			if (!String.IsNullOrEmpty(website))
			{
				NexusModLinkParser.TryParse(website, out parsedLink);
				if (parsedLink != null && String.Equals(parsedLink.SourceUri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase))
				{
					websiteUri = NexusModLinkParser.CreateModUri(parsedLink.GameDomain, parsedLink.ModId, parsedLink.FileId);
				}
				else if (!NexusModLinkParser.TryNormalizeWebsite(website, out websiteUri))
				{
					error = ModTaggerSaveError.InvalidWebsite;
					return false;
				}

				if (parsedLink == null)
					NexusModLinkParser.TryParse(websiteUri.ToString(), out parsedLink);
			}

			string originalModId = modId;
			if (parsedLink != null)
			{
				modId = parsedLink.ModId;
				if (!String.IsNullOrEmpty(parsedLink.FileId))
					fileId = parsedLink.FileId;
				else if (!String.IsNullOrEmpty(originalModId) && !String.Equals(originalModId, modId, StringComparison.OrdinalIgnoreCase))
					fileId = null;
			}

			if (!String.IsNullOrEmpty(modId) && !NexusModLinkParser.IsValidId(modId))
			{
				error = ModTaggerSaveError.InvalidModId;
				return false;
			}

			if (!String.IsNullOrEmpty(fileId) && !NexusModLinkParser.IsValidId(fileId))
			{
				error = ModTaggerSaveError.InvalidFileId;
				return false;
			}

			if (websiteUri == null && NexusModLinkParser.IsValidId(modId))
				websiteUri = NexusModLinkParser.CreateModUri(GameDomainName, modId, fileId);

			ModInfoVM edited = ModInfoEditorVM.EditedModInfoVM;
			edited.ModName = modName;
			edited.HumanReadableVersion = version;
			edited.Author = author;
			edited.Website = websiteUri == null ? null : websiteUri.ToString();
			edited.ModId = modId;
			edited.DownloadId = fileId;
			edited.Description = description;
			edited.Screenshot = screenshot;

			if (!edited.Validate())
			{
				error = ModTaggerSaveError.InvalidValues;
				return false;
			}

			edited.Commit();
			ModInfo modInfo = (ModInfo)m_mifCurrentTagOption;
			bool matchesLoadedCandidate = SameIdentity(modId, fileId, m_strLoadedCandidateModId, m_strLoadedCandidateFileId);
			bool matchesCurrentMod = SameIdentity(modId, fileId, NormalizeText(Mod.Id), NormalizeText(Mod.DownloadId));
			if (!matchesLoadedCandidate && !matchesCurrentMod)
				modInfo.LastKnownVersion = null;

			modInfo.CustomCategoryId = Mod.CustomCategoryId;
			modInfo.InstallDate = Mod.InstallDate;
			modInfo.UpdateWarningEnabled = Mod.UpdateWarningEnabled;
			modInfo.UpdateChecksEnabled = Mod.UpdateChecksEnabled;
			ModTagger.Tag(Mod, m_mifCurrentTagOption, true);
			return true;
		}

		/// <summary>
		/// Determines whether two mod/file identifier pairs describe the same Nexus file.
		/// </summary>
		/// <param name="modIdA">The first mod identifier.</param>
		/// <param name="fileIdA">The first file identifier.</param>
		/// <param name="modIdB">The second mod identifier.</param>
		/// <param name="fileIdB">The second file identifier.</param>
		/// <returns><c>true</c> when both identifier pairs are equal.</returns>
		private static bool SameIdentity(string modIdA, string fileIdA, string modIdB, string fileIdB)
		{
			return String.Equals(NormalizeText(modIdA), NormalizeText(modIdB), StringComparison.OrdinalIgnoreCase) &&
				String.Equals(NormalizeText(fileIdA), NormalizeText(fileIdB), StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Trims optional metadata text and converts blank values to <c>null</c>.
		/// </summary>
		/// <param name="value">The value to normalize.</param>
		/// <returns>The normalized value.</returns>
		private static string NormalizeText(string value)
		{
			return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}
	}
}
