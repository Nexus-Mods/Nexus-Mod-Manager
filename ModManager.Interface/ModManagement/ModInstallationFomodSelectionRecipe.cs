using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Identifies one exact option selected from a FOMOD group by its parsed position and expected display name.
	/// </summary>
	public sealed class ModInstallationFomodOptionSelection
	{
		/// <summary>
		/// Initializes one exact option identity.
		/// </summary>
		/// <param name="optionIndex">The zero-based option index in the actual parsed installer group.</param>
		/// <param name="optionName">The exact option name expected at that index.</param>
		public ModInstallationFomodOptionSelection(int optionIndex, string optionName)
		{
			if (optionIndex < 0)
				throw new ArgumentOutOfRangeException(nameof(optionIndex));
			if (optionName == null)
				throw new ArgumentNullException(nameof(optionName));

			OptionIndex = optionIndex;
			OptionName = optionName;
		}

		/// <summary>
		/// Gets the zero-based option index in the parsed installer group.
		/// </summary>
		public int OptionIndex { get; }

		/// <summary>
		/// Gets the exact option name expected at <see cref="OptionIndex"/>.
		/// </summary>
		public string OptionName { get; }
	}

	/// <summary>
	/// Stores the exact selected options for one FOMOD option group.
	/// </summary>
	public sealed class ModInstallationFomodGroupSelection
	{
		private readonly ReadOnlyCollection<ModInstallationFomodOptionSelection> m_rocSelectedOptions;

		/// <summary>
		/// Initializes one exact group selection.
		/// </summary>
		/// <param name="groupIndex">The zero-based group index in the actual parsed install step.</param>
		/// <param name="groupName">The exact group name expected at that index.</param>
		/// <param name="selectedOptions">The exact options selected in the group; an empty sequence explicitly selects none.</param>
		public ModInstallationFomodGroupSelection(int groupIndex, string groupName,
			IEnumerable<ModInstallationFomodOptionSelection> selectedOptions)
		{
			if (groupIndex < 0)
				throw new ArgumentOutOfRangeException(nameof(groupIndex));
			if (groupName == null)
				throw new ArgumentNullException(nameof(groupName));
			if (selectedOptions == null)
				throw new ArgumentNullException(nameof(selectedOptions));

			var copied = new List<ModInstallationFomodOptionSelection>();
			var indices = new HashSet<int>();
			foreach (ModInstallationFomodOptionSelection selection in selectedOptions)
			{
				if (selection == null)
					throw new ArgumentException("FOMOD selected options cannot contain null values.", nameof(selectedOptions));
				if (!indices.Add(selection.OptionIndex))
					throw new ArgumentException("A FOMOD group selection cannot contain the same option index more than once.", nameof(selectedOptions));
				copied.Add(selection);
			}

			GroupIndex = groupIndex;
			GroupName = groupName;
			m_rocSelectedOptions = new ReadOnlyCollection<ModInstallationFomodOptionSelection>(copied);
		}

		/// <summary>
		/// Gets the zero-based group index in the parsed install step.
		/// </summary>
		public int GroupIndex { get; }

		/// <summary>
		/// Gets the exact group name expected at <see cref="GroupIndex"/>.
		/// </summary>
		public string GroupName { get; }

		/// <summary>
		/// Gets the exact options selected in this group.
		/// </summary>
		public IReadOnlyList<ModInstallationFomodOptionSelection> SelectedOptions
		{
			get { return m_rocSelectedOptions; }
		}
	}

	/// <summary>
	/// Stores the exact group selections for one parsed FOMOD install step.
	/// </summary>
	public sealed class ModInstallationFomodStepSelection
	{
		private readonly ReadOnlyCollection<ModInstallationFomodGroupSelection> m_rocGroups;

		/// <summary>
		/// Initializes one exact install-step selection.
		/// </summary>
		/// <param name="stepIndex">The zero-based step index in the actual parsed installer definition.</param>
		/// <param name="stepName">The exact step name expected at that index; legacy version-1 scripts may use <c>null</c>.</param>
		/// <param name="groups">The explicit selection state for every group in the step.</param>
		public ModInstallationFomodStepSelection(int stepIndex, string stepName,
			IEnumerable<ModInstallationFomodGroupSelection> groups)
		{
			if (stepIndex < 0)
				throw new ArgumentOutOfRangeException(nameof(stepIndex));
			if (groups == null)
				throw new ArgumentNullException(nameof(groups));

			var copied = new List<ModInstallationFomodGroupSelection>();
			var indices = new HashSet<int>();
			foreach (ModInstallationFomodGroupSelection group in groups)
			{
				if (group == null)
					throw new ArgumentException("FOMOD step groups cannot contain null values.", nameof(groups));
				if (!indices.Add(group.GroupIndex))
					throw new ArgumentException("A FOMOD step selection cannot contain the same group index more than once.", nameof(groups));
				copied.Add(group);
			}

			StepIndex = stepIndex;
			StepName = stepName;
			m_rocGroups = new ReadOnlyCollection<ModInstallationFomodGroupSelection>(copied);
		}

		/// <summary>
		/// Gets the zero-based step index in the parsed installer definition.
		/// </summary>
		public int StepIndex { get; }

		/// <summary>
		/// Gets the exact step name expected at <see cref="StepIndex"/>.
		/// </summary>
		public string StepName { get; }

		/// <summary>
		/// Gets the explicit selection state for every group in the step.
		/// </summary>
		public IReadOnlyList<ModInstallationFomodGroupSelection> Groups
		{
			get { return m_rocGroups; }
		}
	}

	/// <summary>
	/// Stores an immutable exact FOMOD selection recipe bound to one parsed XML-script version and definition shape.
	/// </summary>
	/// <remarks>
	/// Parsed positions are paired with expected names because the native FOMOD schema does not require step, group or option
	/// display names to be unique. The adapter rechecks both against the actual installer definition before producing operations.
	/// </remarks>
	public sealed class ModInstallationFomodSelectionRecipe
	{
		private readonly ReadOnlyCollection<ModInstallationFomodStepSelection> m_rocSteps;

		/// <summary>
		/// Initializes one exact FOMOD selection recipe.
		/// </summary>
		/// <param name="scriptVersion">The exact parsed XML-script version expected from the archive.</param>
		/// <param name="steps">The explicit selection state for every parsed install step.</param>
		public ModInstallationFomodSelectionRecipe(Version scriptVersion,
			IEnumerable<ModInstallationFomodStepSelection> steps)
		{
			if (scriptVersion == null)
				throw new ArgumentNullException(nameof(scriptVersion));
			if (steps == null)
				throw new ArgumentNullException(nameof(steps));

			var copied = new List<ModInstallationFomodStepSelection>();
			var indices = new HashSet<int>();
			foreach (ModInstallationFomodStepSelection step in steps)
			{
				if (step == null)
					throw new ArgumentException("FOMOD step selections cannot contain null values.", nameof(steps));
				if (!indices.Add(step.StepIndex))
					throw new ArgumentException("A FOMOD selection recipe cannot contain the same step index more than once.", nameof(steps));
				copied.Add(step);
			}

			ScriptVersion = scriptVersion;
			m_rocSteps = new ReadOnlyCollection<ModInstallationFomodStepSelection>(copied);
		}

		/// <summary>
		/// Gets the exact parsed XML-script version expected from the archive.
		/// </summary>
		public Version ScriptVersion { get; }

		/// <summary>
		/// Gets the explicit selection state for every parsed install step.
		/// </summary>
		public IReadOnlyList<ModInstallationFomodStepSelection> Steps
		{
			get { return m_rocSteps; }
		}
	}
}
