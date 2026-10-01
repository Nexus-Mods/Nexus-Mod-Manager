using System;
using System.Collections.Generic;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Exposes exact FOMOD-selection translation implemented by a native scripted-installer type.
	/// </summary>
	/// <remarks>
	/// The interface keeps Collection/native orchestration independent from the concrete XML-script assembly. Implementations
	/// translate only against the actual installer definition already loaded from the verified mod archive.
	/// </remarks>
	public interface IModInstallationFomodRecipeAdapter
	{
		/// <summary>
		/// Gets the adapter identifier required by <see cref="ModInstallationRecipeValidation"/>.
		/// </summary>
		string AdapterId { get; }

		/// <summary>
		/// Gets the adapter contract version required by <see cref="ModInstallationRecipeValidation"/>.
		/// </summary>
		int AdapterVersion { get; }

		/// <summary>
		/// Gets the capability identifier consumed by this adapter.
		/// </summary>
		string CapabilityId { get; }

		/// <summary>
		/// Gets the supported capability contract version.
		/// </summary>
		int CapabilityVersion { get; }


		/// <summary>
		/// Translates exact FOMOD selections against the actual installer definition into native typed installation operations.
		/// </summary>
		/// <param name="recipeInput">The validated recipe envelope that will carry the translated native plan.</param>
		/// <param name="mod">The verified mod archive whose actual install script must be translated.</param>
		/// <param name="gameMode">The native game mode used to evaluate installer dependencies.</param>
		/// <param name="environmentInfo">The current application environment used by installer dependency checks.</param>
		/// <param name="pluginManager">The current plugin manager, or <c>null</c> for pluginless games.</param>
		/// <param name="recipe">The exact step/group/option selections to validate against the actual installer definition.</param>
		/// <returns>A new immutable recipe input carrying the translated native operation plan.</returns>
		ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe);
	}

	/// <summary>
	/// Optional exact-planning extension used by orchestration which must bind a validated path envelope before translation.
	/// Implementations remain full <see cref="IModInstallationFomodRecipeAdapter"/> translators; adapters which do not expose
	/// this planning contract remain valid native adapters but are not eligible for non-interactive Collection replay.
	/// </summary>
	public interface IModInstallationFomodRecipePlanningAdapter : IModInstallationFomodRecipeAdapter
	{
		/// <summary>Gets the exact parsed FOMOD/XML script version from the verified mod archive.</summary>
		Version GetScriptVersion(IMod mod);

		/// <summary>
		/// Resolves the exact archive-source/destination path set produced by one FOMOD selection before C5.3 validation.
		/// </summary>
		IReadOnlyList<ModInstallationRecipePath> GetValidationPaths(IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe);

		/// <summary>
		/// Resolves exact validation paths against an explicitly projected effective environment rather than the live plugin baseline.
		/// </summary>
		IReadOnlyList<ModInstallationRecipePath> GetValidationPaths(IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe,
			IModInstallationConditionEnvironment conditionEnvironment);

		/// <summary>
		/// Translates exact selections against an explicitly projected effective environment.
		/// </summary>
		ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe,
			IModInstallationConditionEnvironment conditionEnvironment);
	}
}
