using System;
using System.Collections;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public class OptionGroupVM
	{
		protected OptionGroup OptionGroup { get; private set; }
		public string Name { get; set; }
		public OptionGroupType Type { get; set; }
		public SortOrder OptionSortOrder { get; set; }
		public ThreadSafeObservableList<Option> Options { get; private set; }

		public OptionGroupVM(OptionGroup p_opgGroup)
		{
			OptionGroup = p_opgGroup;
			Reset();
		}

		public OptionGroup Commit()
		{
			OptionGroup.Name = Name;
			OptionGroup.Type = Type;
			OptionGroup.OptionSortOrder = OptionSortOrder;
			if (OptionSortOrder == SortOrder.Explicit)
			{
				OptionGroup.Options.Clear();
				for (Int32 i = 0; i < Options.Count; i++)
				{
					Option optOption = Options[i];
					OptionGroup.Options.Add(optOption);
				}
			}
			return OptionGroup;
		}

		public OptionGroup Commit(string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Name)))
				OptionGroup.Name = Name;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Type)))
				OptionGroup.Type = Type;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.OptionSortOrder)))
				OptionGroup.OptionSortOrder = OptionSortOrder;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Options)))
				if (OptionSortOrder == SortOrder.Explicit)
				{
					OptionGroup.Options.Clear();
					for (Int32 i = 0; i < Options.Count; i++)
					{
						Option optOption = Options[i];
						OptionGroup.Options.Add(optOption);
					}
				}
			return OptionGroup;
		}

		public void Reset()
		{
			Name = OptionGroup.Name;
			Type = OptionGroup.Type;
			OptionSortOrder = OptionGroup.OptionSortOrder;
			Options = new ThreadSafeObservableList<Option>(OptionGroup.Options);
		}

		public void Reset(string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Name)))
				Name = OptionGroup.Name;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Type)))
				Type = OptionGroup.Type;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.OptionSortOrder)))
				OptionSortOrder = OptionGroup.OptionSortOrder;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Options)))
				Options = new ThreadSafeObservableList<Option>(OptionGroup.Options);
		}
	}

	[Flags]
	public enum OptionGroupProperties
	{
		Name = 1,
		Type = 2,
		OptionSortOrder = 4
	}

	public class OptionGroupEditorVM : IViewModel
	{
		public event EventHandler OptionGroupValidated = delegate { };

		public IEnumerable OptionGroupTypes { get; private set; }
		public IEnumerable SortOrders { get; private set; }

		public bool NameVisible { get; private set; }
		public bool TypeVisible { get; private set; }
		public bool OptionSortOrderVisible { get; private set; }

		public OptionGroup OptionGroup
		{
			set
			{
				OptionGroupVM = new OptionGroupVM(value);
			}
		}

		public OptionGroupVM OptionGroupVM { get; private set; }
		public ErrorContainer Errors { get; private set; }

		public OptionGroupEditorVM(OptionGroup p_opgGroup, OptionGroupProperties p_ogpEditableProperties)
		{
			OptionGroup = p_opgGroup;
			NameVisible = (p_ogpEditableProperties & OptionGroupProperties.Name) > 0;
			TypeVisible = (p_ogpEditableProperties & OptionGroupProperties.Type) > 0;
			OptionSortOrderVisible = (p_ogpEditableProperties & OptionGroupProperties.OptionSortOrder) > 0;

			OptionGroupTypes = Enum.GetValues(typeof(OptionGroupType));
			SortOrders = Enum.GetValues(typeof(SortOrder));
			Errors = new ErrorContainer();
		}

		protected void OnOptionGroupValidated()
		{
			OptionGroupValidated(this, new EventArgs());
		}

		public void SaveOptionGroup(string p_strPropertyName)
		{
			bool booValid = true;
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<OptionGroup>(x => x.Name)))
				booValid = ValidateGroupName();
			if (booValid)
				OptionGroupVM.Commit(p_strPropertyName);
		}

		/// <summary>
		/// Ensures that the group name is valid.
		/// </summary>
		/// <remarks>
		/// A group name is valid if it is not empty.
		/// </remarks>
		/// <returns><c>true</c> if the group name is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateGroupName()
		{
			bool booIsValid = true;
			Errors.Clear<OptionGroup>(x => x.Name);
			if (String.IsNullOrEmpty(OptionGroupVM.Name))
			{
				Errors.SetError<OptionGroup>(x => x.Name, "Name is required.");
				booIsValid = false;
			}
			OnOptionGroupValidated();
			return booIsValid;
		}

		#region IViewModel Members

		public bool Validate()
		{
			return ValidateGroupName();
		}

		#endregion
	}
}
