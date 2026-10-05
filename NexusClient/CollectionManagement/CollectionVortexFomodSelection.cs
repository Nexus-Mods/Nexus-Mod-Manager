using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable Vortex FOMOD option identity captured from collection.json.
	/// </summary>
	public sealed class CollectionVortexFomodChoice : IEquatable<CollectionVortexFomodChoice>
	{
		public CollectionVortexFomodChoice(int index, string name)
		{
			if (index < 0)
				throw new ArgumentOutOfRangeException(nameof(index));
			if (String.IsNullOrWhiteSpace(name))
				throw new ArgumentException("A Vortex FOMOD choice name is required.", nameof(name));
			Index = index;
			Name = name;
		}

		public int Index { get; }
		public string Name { get; }

		public bool Equals(CollectionVortexFomodChoice other)
		{
			return !ReferenceEquals(other, null) && Index == other.Index && StringComparer.Ordinal.Equals(Name, other.Name);
		}

		public override bool Equals(object obj) { return Equals(obj as CollectionVortexFomodChoice); }
		public override int GetHashCode() { return (Index * 397) ^ StringComparer.Ordinal.GetHashCode(Name); }
	}

	/// <summary>Immutable Vortex FOMOD group selection captured from collection.json.</summary>
	public sealed class CollectionVortexFomodGroupSelection : IEquatable<CollectionVortexFomodGroupSelection>
	{
		private readonly ReadOnlyCollection<CollectionVortexFomodChoice> _choices;

		public CollectionVortexFomodGroupSelection(string name, IEnumerable<CollectionVortexFomodChoice> choices)
		{
			if (String.IsNullOrWhiteSpace(name))
				throw new ArgumentException("A Vortex FOMOD group name is required.", nameof(name));
			if (choices == null)
				throw new ArgumentNullException(nameof(choices));
			List<CollectionVortexFomodChoice> copied = choices.ToList();
			if (copied.Any(x => x == null) || copied.Select(x => x.Index).Distinct().Count() != copied.Count)
				throw new ArgumentException("Vortex FOMOD group selections require unique non-null option indices.", nameof(choices));
			Name = name;
			_choices = new ReadOnlyCollection<CollectionVortexFomodChoice>(copied);
		}

		public string Name { get; }
		public IReadOnlyList<CollectionVortexFomodChoice> Choices { get { return _choices; } }

		public bool Equals(CollectionVortexFomodGroupSelection other)
		{
			return !ReferenceEquals(other, null) && StringComparer.Ordinal.Equals(Name, other.Name) && _choices.SequenceEqual(other._choices);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexFomodGroupSelection); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = StringComparer.Ordinal.GetHashCode(Name);
				foreach (CollectionVortexFomodChoice choice in _choices) hash = (hash * 397) ^ choice.GetHashCode();
				return hash;
			}
		}
	}

	/// <summary>Immutable Vortex FOMOD install-step selection captured from collection.json.</summary>
	public sealed class CollectionVortexFomodStepSelection : IEquatable<CollectionVortexFomodStepSelection>
	{
		private readonly ReadOnlyCollection<CollectionVortexFomodGroupSelection> _groups;

		public CollectionVortexFomodStepSelection(string name, IEnumerable<CollectionVortexFomodGroupSelection> groups)
		{
			if (String.IsNullOrWhiteSpace(name))
				throw new ArgumentException("A Vortex FOMOD step name is required.", nameof(name));
			if (groups == null)
				throw new ArgumentNullException(nameof(groups));
			List<CollectionVortexFomodGroupSelection> copied = groups.ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("Vortex FOMOD step groups cannot contain null values.", nameof(groups));
			Name = name;
			_groups = new ReadOnlyCollection<CollectionVortexFomodGroupSelection>(copied);
		}

		public string Name { get; }
		public IReadOnlyList<CollectionVortexFomodGroupSelection> Groups { get { return _groups; } }

		public bool Equals(CollectionVortexFomodStepSelection other)
		{
			return !ReferenceEquals(other, null) && StringComparer.Ordinal.Equals(Name, other.Name) && _groups.SequenceEqual(other._groups);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexFomodStepSelection); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = StringComparer.Ordinal.GetHashCode(Name);
				foreach (CollectionVortexFomodGroupSelection group in _groups) hash = (hash * 397) ^ group.GetHashCode();
				return hash;
			}
		}
	}

	/// <summary>
	/// Exact ordered Vortex FOMOD selection wire data retained for later validation against the actual native installer definition.
	/// </summary>
	public sealed class CollectionVortexFomodSelection : IEquatable<CollectionVortexFomodSelection>
	{
		private readonly ReadOnlyCollection<CollectionVortexFomodStepSelection> _steps;

		public CollectionVortexFomodSelection(IEnumerable<CollectionVortexFomodStepSelection> steps)
		{
			if (steps == null)
				throw new ArgumentNullException(nameof(steps));
			List<CollectionVortexFomodStepSelection> copied = steps.ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("Vortex FOMOD selections cannot contain null install steps.", nameof(steps));
			_steps = new ReadOnlyCollection<CollectionVortexFomodStepSelection>(copied);
		}

		public IReadOnlyList<CollectionVortexFomodStepSelection> Steps { get { return _steps; } }

		public bool Equals(CollectionVortexFomodSelection other)
		{
			return !ReferenceEquals(other, null) && _steps.SequenceEqual(other._steps);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexFomodSelection); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				foreach (CollectionVortexFomodStepSelection step in _steps) hash = (hash * 397) ^ step.GetHashCode();
				return hash;
			}
		}
	}
}
