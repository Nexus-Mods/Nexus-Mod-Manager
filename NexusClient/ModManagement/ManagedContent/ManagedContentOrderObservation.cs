using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Separates file configuration from native installation, disk presence and ownership.</summary>
	public enum ManagedContentOrderState
	{
		NotApplicable = 0,
		FileUnavailable = 1,
		Ambiguous = 2,
		Unconfigured = 3,
		Configured = 4
	}

	/// <summary>One read-only component/settings association; an INI-only row never acquires native owners.</summary>
	public sealed class ManagedContentComponentViewRow
	{
		/// <summary>Joins detached native/disk evidence to unambiguous configured values without inferring defaults.</summary>
		internal ManagedContentComponentViewRow(string path, string kind, ManagedContentNativeComponent component,
			bool? observedOnDisk, bool eligible, IniOrderFileObservation order, IniOrderSection section)
		{
			RootRelativeFolderPath = path;
			ComponentKind = kind;
			Component = component;
			ObservedOnDisk = observedOnDisk;
			if (!eligible) OrderState = ManagedContentOrderState.NotApplicable;
			else if (order == null || !order.IsLoaded || order.Document == null) OrderState = ManagedContentOrderState.FileUnavailable;
			else if (!order.Document.IsUnambiguous) OrderState = ManagedContentOrderState.Ambiguous;
			else if (section == null) OrderState = ManagedContentOrderState.Unconfigured;
			else
			{
				OrderState = ManagedContentOrderState.Configured;
				Priority = section.Priority;
				Enabled = section.Enabled;
			}
		}

		public string RootRelativeFolderPath { get; }
		public string ComponentKind { get; }
		/// <summary>Null for a settings-only entry; never interpreted as an NMM registration or external owner.</summary>
		public ManagedContentNativeComponent Component { get; }
		public bool? ObservedOnDisk { get; }
		public ManagedContentOrderState OrderState { get; }
		public int? Priority { get; }
		public bool? Enabled { get; }
	}

	/// <summary>Pure association of game settings and the existing native-derived component index.</summary>
	public static class ManagedContentOrderProjector
	{
		/// <summary>Retains deployed, external and settings-only rows without treating section identity as installation authority.</summary>
		public static IReadOnlyList<ManagedContentComponentViewRow> Project(ManagedContentNativeComponentIndex index,
			IniOrderDocumentRule rule, IniOrderFileObservation order)
		{
			if (index == null) throw new ArgumentNullException(nameof(index));
			var sections = order?.Document != null && order.Document.IsComplete
				? order.Document.ComponentSections.GroupBy(section => section.Name, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, IniOrderSection>(StringComparer.OrdinalIgnoreCase);
			var rows = new Dictionary<string, ManagedContentComponentViewRow>(StringComparer.OrdinalIgnoreCase);
			foreach (ManagedContentNativeComponent component in index.Components)
			{
				bool eligible = rule != null && component.ComponentKind == rule.ComponentKind &&
					String.Equals(component.RootRelativeFolderPath, rule.TargetFolder + "\\" + component.FolderName, StringComparison.OrdinalIgnoreCase) &&
					rule.Format.IsComponentName(component.FolderName);
				IniOrderSection section;
				sections.TryGetValue(component.FolderName, out section);
				rows.Add(component.RootRelativeFolderPath, new ManagedContentComponentViewRow(component.RootRelativeFolderPath,
					component.ComponentKind, component, component.ObservedOnDisk, eligible, order, section));
			}
			if (rule != null)
				foreach (IniOrderSection section in sections.Values.Where(section => rule.Format.IsComponentName(section.Name)))
				{
					string path = rule.TargetFolder + "\\" + section.Name;
					if (!rows.ContainsKey(path)) rows.Add(path, new ManagedContentComponentViewRow(path, rule.ComponentKind,
						null, index.HasCompleteDiskInventory ? (bool?)false : null, true, order, section));
				}
			return new ReadOnlyCollection<ManagedContentComponentViewRow>(rows.Values.OrderBy(row => row.RootRelativeFolderPath,
				StringComparer.OrdinalIgnoreCase).ThenBy(row => row.RootRelativeFolderPath, StringComparer.Ordinal).ToList());
		}
	}
}
