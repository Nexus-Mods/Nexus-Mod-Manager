using System;
using System.Collections;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public enum OptionTypeResolverType
	{
		Static,
		Conditional
	}

	public class OptionTypeResolverEditorVM : IViewModel
	{
		public ConditionalTypeEditorVM ConditionalTypeEditorVM { get; private set; }
		public IEnumerable OptionTypes { get; private set; }

		public StaticOptionTypeResolver StaticOptionTypeResolver { get; private set; }
		protected ConditionalOptionTypeResolver ConditionalOptionTypeResolver { get; private set; }
		protected Option Option { get; set; }

		public OptionTypeResolverType OptionTypeResolverType
		{
			get
			{
				if (Option.OptionTypeResolver is StaticOptionTypeResolver)
					return OptionTypeResolverType.Static;
				return OptionTypeResolverType.Conditional;
			}
			set
			{
				switch (value)
				{
					case OptionTypeResolverType.Static:
						if (!(Option.OptionTypeResolver is StaticOptionTypeResolver))
							Option.OptionTypeResolver = StaticOptionTypeResolver;
						break;
					case OptionTypeResolverType.Conditional:
						if (!(Option.OptionTypeResolver is ConditionalOptionTypeResolver))
							Option.OptionTypeResolver = ConditionalOptionTypeResolver;
						break;
					default:
						throw new Exception("Invalid value for " + ObjectHelper.GetPropertyName(() => OptionTypeResolverType));
				}
			}
		}

		public OptionTypeResolverEditorVM(ConditionalTypeEditorVM p_tvmConditionResolverVM, Option p_optOption)
		{
			ConditionalTypeEditorVM = p_tvmConditionResolverVM;
			Option = p_optOption;

			OptionTypes = Enum.GetValues(typeof(OptionType));
			if (Option.OptionTypeResolver is StaticOptionTypeResolver)
			{
				StaticOptionTypeResolver = (StaticOptionTypeResolver)Option.OptionTypeResolver;
				ConditionalOptionTypeResolver = new ConditionalOptionTypeResolver(OptionType.NotUsable);
			}
			if (Option.OptionTypeResolver is ConditionalOptionTypeResolver)
			{
				StaticOptionTypeResolver = new StaticOptionTypeResolver(OptionType.NotUsable);
				ConditionalOptionTypeResolver = (ConditionalOptionTypeResolver)Option.OptionTypeResolver;
			}
			p_tvmConditionResolverVM.TypeResolver = ConditionalOptionTypeResolver;
		}

		#region IViewModel Members

		public bool Validate()
		{
			return true;
		}

		#endregion
	}
}
