using Nexus.Client.Games;
using Nexus.Client.Mods;
using System.Collections.Generic;
using Nexus.Client.ModAuthoring.UI.Controls;
using Nexus.Client.ModRepositories;
using Nexus.Client.Settings;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display a mod tag editor.
	/// </summary>
	public class ModTaggerVM
	{
		private IModInfo m_mifCurrentTagOption = null;

		#region Properties

		/// <summary>
		/// Gets the theme to use for the UI.
		/// </summary>
		/// <value>The theme to use for the UI.</value>
		public Theme CurrentTheme { get; private set; }

		/// <summary>
		/// Gets the list of possible matches to use to tag the mod.
		/// </summary>
		/// <value>The list of possible matches to use to tag the mod.</value>
		public IEnumerable<IModInfo> TagCandidates
		{
			get
			{
				return ModTagger.GetTagInfoCandidates(Mod);
			}
		}

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for diaplying the mod info editor.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying the mod info editor.</value>
		public ModInfoEditorVM ModInfoEditorVM { get; private set; }

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		/// <summary>
		/// Gets the tagger to use to tag mods with metadata.
		/// </summary>
		/// <value>The tagger to use to tag mods with metadata.</value>
		protected AutoTagger ModTagger { get; private set; }

		/// <summary>
		/// Gets the mod being tagged.
		/// </summary>
		/// <value>The mod being tagged.</value>
		protected IMod Mod { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_atgTagger">The tagger to use to tag mods with metadata.</param>
		/// <param name="p_modMod">The mod to be tagged.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		/// <param name="p_thmTheme">The current theme to use for the views.</param>
		public ModTaggerVM(AutoTagger p_atgTagger, IMod p_modMod, ISettings p_setSettings, Theme p_thmTheme)
		{
			ModTagger = p_atgTagger;
			Mod = p_modMod;
			Settings = p_setSettings;
			CurrentTheme = p_thmTheme;
			m_mifCurrentTagOption = new ModInfo(Mod);
			ModInfoEditorVM = new ModInfoEditorVM(m_mifCurrentTagOption, p_setSettings);
			ModInfoEditorVM.EditedModInfoVM.LoadInfoValues(p_modMod);
		}

		#endregion

		/// <summary>
		/// Loads the values of the given tag option into the tag editor.
		/// </summary>
		/// <param name="p_mifInfo">The tag option whose values are to be displayed in the tag editor.</param>
		public void LoadTagOption(IModInfo p_mifInfo)
		{
			ModInfoEditorVM.EditedModInfoVM.LoadInfoValues(p_mifInfo);
		}

		/// <summary>
		/// Tags the mod with the current tag values.
		/// </summary>
		public void SaveTags()
		{
			ModInfoEditorVM.EditedModInfoVM.Commit();
			ModInfo modMod = (ModInfo)m_mifCurrentTagOption;
			modMod.CustomCategoryId = Mod.CustomCategoryId;
			modMod.InstallDate = Mod.InstallDate;
			modMod.UpdateWarningEnabled = Mod.UpdateWarningEnabled;
			modMod.UpdateChecksEnabled = Mod.UpdateChecksEnabled;
			ModTagger.Tag(Mod, m_mifCurrentTagOption, true);
		}
	}
}
