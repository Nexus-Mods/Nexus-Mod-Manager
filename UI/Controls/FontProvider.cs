using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Design;
using System.Globalization;
using System.Reflection;

namespace Nexus.UI.Controls
{
	/// <summary>A component that can be used to assign a set of fonts to any object with a "Font" property.</summary>
	[ToolboxItem(true)]
	[ProvideProperty("FontSet", typeof(object))]
	[ProvideProperty("FontSize", typeof(object))]
	[ProvideProperty("FontStyle", typeof(object))]
	public partial class FontProvider : Component, IExtenderProvider
	{
		/// <summary>Gets or sets the delegate used to quest the font.</summary>
		protected static IFontSetResolver Resolver { get; set; }

		/// <summary>
		/// Sets the resolver to use to get the font associated with a font set.
		/// </summary>
		/// <param name="p_fsrResolver">The resolver to use to get the font associated with a font set.</param>
		public static void SetFontSetResolver(IFontSetResolver p_fsrResolver)
		{
			Resolver = p_fsrResolver;
		}

		#region Properties

		/// <summary>
		/// Gets the font set information associated with each component.
		/// </summary>
		/// <value>The font set information associated with each component.</value>
		protected Dictionary<object, FontSetInformation> ComponentToFontInformation
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the type descriptor provider associated with each component.
		/// </summary>
		/// <value>The type descriptor provider associated with each component.</value>
		protected Dictionary<object, FontProviderTypeDescriptorProvider> ComponentToTypeDescriptorProvider
		{
			get;
			private set;
		}

		#endregion

		#region Constructors

		/// <summary>Creates a new instance of the <see cref="FontProvider"/> class.</summary>
		public FontProvider()
		{
			InitializeComponent();

			//Finish initialisation.
			ComponentToFontInformation = new Dictionary<object, FontSetInformation>();
			ComponentToTypeDescriptorProvider = new Dictionary<object, FontProviderTypeDescriptorProvider>();
		}

		#endregion

		/// <summary>Specifies whether this object can provide its extender properties to the specified object.</summary>            
		public bool CanExtend(object extendee)
		{
			//Check for any property which has a type of font.
			foreach (PropertyInfo property in extendee.GetType().GetProperties())
			{
				if (property.CanRead && property.CanWrite && property.PropertyType == typeof(Font))
					return true;
			}

			return false;
		}

		#region Extended Property Getters/Setters

		/// <summary>Gets the font set for the component passed.</summary>
		/// <param name="component">The component to get the font set for.</param>
		[DefaultValue(null)]
		[Category("Appearance")]
		public string GetFontSet(object component)
		{
			return GetFontInformation(component).Set;
		}

		/// <summary>Sets the font set for the component passed.</summary>
		/// <param name="component">The component to set the font set for.</param>
		/// <param name="fontSet">The name of the font set used.</param>
		public void SetFontSet(object component, string fontSet)
		{
			FontSetInformation information = GetFontInformation(component);
			SetFontInformation(component, new FontSetInformation() { Set = fontSet, Size = information.Size, Style = information.Style });
		}

		/// <summary>Gets the font size for the component passed.</summary>
		/// <param name="component">The component to get the font set for.</param>
		[DefaultValue(8.25f)]
		[Category("Appearance")]
		public float GetFontSize(object component)
		{
			return GetFontInformation(component).Size;
		}

		/// <summary>Sets the font size for the component passed.</summary>
		/// <param name="component">The component to set the font set for.</param>
		/// <param name="fontSize">The size of the font.</param>
		public void SetFontSize(object component, float fontSize)
		{
			FontSetInformation information = GetFontInformation(component);
			SetFontInformation(component, new FontSetInformation() { Set = information.Set, Size = fontSize, Style = information.Style });
		}

		/// <summary>Gets the font style for the component passed.</summary>
		/// <param name="component">The component to get the font set for.</param>
		[DefaultValue(FontStyle.Regular)]
		[Category("Appearance")]
		[Editor(typeof(FlagEnumUITypeEditor), typeof(UITypeEditor))]
		public FontStyle GetFontStyle(object component)
		{
			return GetFontInformation(component).Style;
		}

		/// <summary>Sets the font set for the component passed.</summary>
		/// <param name="component">The component to set the font set for.</param>
		/// <param name="fontStyle">The name of the font set used.</param>
		public void SetFontStyle(object component, FontStyle fontStyle)
		{
			FontSetInformation information = GetFontInformation(component);
			SetFontInformation(component, new FontSetInformation() { Set = information.Set, Size = information.Size, Style = fontStyle });
		}

		#endregion

		#region Protected Methods

		/// <summary>Gets the font set for the component passed.</summary>
		/// <param name="component">The component to get the font set for.</param>
		[DefaultValue(null), DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
		protected FontSetInformation GetFontInformation(object component)
		{
			if (ComponentToFontInformation.ContainsKey(component))
				return ComponentToFontInformation[component];
			else
				return new FontSetInformation();
		}

		/// <summary>Sets the font set for the component passed.</summary>
		/// <param name="component">The component to set the font set for.</param>
		/// <param name="information">The name of the font set used.</param>
		protected void SetFontInformation(object component, FontSetInformation information)
		{
			if (FontSetInformation.Empty.Equals(information))
			{
				if (ComponentToFontInformation.ContainsKey(component))
				{
					//Reset the font.
					foreach (PropertyInfo property in component.GetType().GetProperties())
					{
						if (property.CanRead && property.CanWrite && property.PropertyType == typeof(Font))
							property.SetValue(component, null, null);
					}

					if (DesignMode)
					{
						//Remove the font set.
						ComponentToFontInformation.Remove(component);

						//Remove the type descriptor provider.
						TypeDescriptor.RemoveProvider(ComponentToTypeDescriptorProvider[component], component);
						ComponentToTypeDescriptorProvider.Remove(component);
					}
				}
			}
			else
			{
				ComponentToFontInformation[component] = information;

				Font fntFont = null;
				if (DesignMode || (Resolver == null))
				{
					if (!ComponentToTypeDescriptorProvider.ContainsKey(component))
					{
						ComponentToTypeDescriptorProvider[component] = new FontProviderTypeDescriptorProvider(component, TypeDescriptor.GetProvider(component));
						TypeDescriptor.AddProvider(ComponentToTypeDescriptorProvider[component], component);
					}
					fntFont = new Font("Microsoft Sans Serif", information.Size, information.Style);
				}
				else
				{
					if (Resolver == null)
						throw new Exception("The font set Resolver hasn't been assigned.");
					fntFont = Resolver.RequestFont(information);
				}
				foreach (PropertyInfo property in component.GetType().GetProperties())
				{
					if (property.CanRead && property.CanWrite && property.PropertyType == typeof(Font))
						property.SetValue(component, fntFont, null);
				}
			}
		}

		#endregion

		/// <summary>
		/// Type descriptor used to hide any font properties when using a font information.
		/// </summary>
		public class FontProviderTypeDescriptor : ICustomTypeDescriptor
		{
			#region Properties

			/// <summary>
			/// Gets the object whose type is being described.
			/// </summary>
			/// <value>The object whose type is being described.</value>
			protected object Owner { get; private set; }

			#endregion

			#region Constructors

			/// <summary>Creates a new instance of the <see cref="FontProviderTypeDescriptor"/> class.</summary>
			/// <param name="p_objOwner">The object whose type is being described.</param>
			public FontProviderTypeDescriptor(object p_objOwner)
			{
				if (p_objOwner == null)
					throw new ArgumentNullException("p_objOwner");
				Owner = p_objOwner;
			}

			#endregion

			#region Public Methods

			/// <summary>
			/// Returns a collection of custom attributes for this instance of a component.
			/// </summary>
			public AttributeCollection GetAttributes()
			{
				return TypeDescriptor.GetAttributes(Owner.GetType());
			}

			/// <summary>
			/// Returns the class name of this instance of a component.
			/// </summary>
			public string GetClassName()
			{
				return TypeDescriptor.GetClassName(Owner.GetType());
			}

			/// <summary>
			/// Returns the name of this instance of a component.
			/// </summary>
			public string GetComponentName()
			{
				return Owner is IComponent ? (Owner as IComponent).Site.Name : string.Empty;
			}

			/// <summary>
			/// Returns a type converter for this instance of a component.
			/// </summary>
			public TypeConverter GetConverter()
			{
				return TypeDescriptor.GetConverter(Owner.GetType());
			}

			/// <summary>
			/// Returns the default event for this instance of a component.
			/// </summary>
			public EventDescriptor GetDefaultEvent()
			{
				return TypeDescriptor.GetDefaultEvent(Owner.GetType());
			}

			/// <summary>
			/// Returns the default property for this instance of a component.
			/// </summary>
			public PropertyDescriptor GetDefaultProperty()
			{
				return TypeDescriptor.GetDefaultProperty(Owner.GetType());
			}

			/// <summary>
			/// Returns an editor of the specified type for this instance of a component.
			/// </summary>
			public object GetEditor(Type editorBaseType)
			{
				return TypeDescriptor.GetEditor(Owner.GetType(), editorBaseType);
			}

			/// <summary>
			/// Returns the events for this instance of a component.
			/// </summary>
			public EventDescriptorCollection GetEvents()
			{
				return TypeDescriptor.GetEvents(Owner.GetType());
			}

			/// <summary>
			/// Returns the events for this instance of a component using the specified attribute array as a filter.
			/// </summary>
			public EventDescriptorCollection GetEvents(Attribute[] attributeList)
			{
				return TypeDescriptor.GetEvents(Owner.GetType(), attributeList);
			}

			/// <summary>
			/// Returns the properties for this instance of a component.
			/// </summary>
			public PropertyDescriptorCollection GetProperties()
			{
				return GetProperties(new Attribute[0]);
			}

			/// <summary>
			/// Returns the properties for this instance of a component using the attribute array as a filter.
			/// </summary>
			public PropertyDescriptorCollection GetProperties(Attribute[] attributeList)
			{
				PropertyDescriptorCollection pdcProperties = TypeDescriptor.GetProperties(Owner.GetType(), attributeList);
				PropertyDescriptorCollection pdcFilteredProperties = new PropertyDescriptorCollection(null, false);

				foreach (PropertyDescriptor pdrProperty in pdcProperties)
					if (pdrProperty.PropertyType != typeof(Font))
						pdcFilteredProperties.Add(pdrProperty);

				return pdcFilteredProperties;
			}

			/// <summary>Returns an object that contains the property described by the specified property descriptor.</summary>
			public object GetPropertyOwner(PropertyDescriptor propertyDescriptor)
			{
				return Owner;
			}

			#endregion
		}

		/// <summary>
		/// Type descriptor provider used to return the FontSetProviderTypeDescriptor type descriptor.
		/// </summary>
		protected class FontProviderTypeDescriptorProvider : TypeDescriptionProvider
		{
			#region Properties

			/// <summary>
			/// Gets the object for which this provider is to provide a type descriptor.
			/// </summary>
			/// <value>The object for which this provider is to provide a type descriptor.</value>
			protected object Owner { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// Creates a new instance of the <see cref="FontProviderTypeDescriptorProvider"/> class.
			/// </summary>
			/// <param name="owner">The object for which this provider is to provide a type descriptor.</param>
			/// <param name="parent">The existing type descriptor provider for the given component.</param>
			public FontProviderTypeDescriptorProvider(object owner, TypeDescriptionProvider parent)
				: base(parent)
			{
				if (owner == null)
					throw new ArgumentNullException("owner");

				Owner = owner;
			}

			#endregion

			/// <summary>Gets a custom type descriptor for the given type and object.</summary>
			/// <param name="p_tpeObjectType">The type of object for which to return a descriptor.</param>
			/// <param name="p_objInstance">The object for which to return a descriptor.</param>
			public override ICustomTypeDescriptor GetTypeDescriptor(Type p_tpeObjectType, object p_objInstance)
			{
				if (p_objInstance != null && p_objInstance is object && p_objInstance.Equals(Owner))
					return new FontProviderTypeDescriptor(Owner);
				else
					return base.GetTypeDescriptor(p_tpeObjectType, p_objInstance);
			}
		}
	}
}