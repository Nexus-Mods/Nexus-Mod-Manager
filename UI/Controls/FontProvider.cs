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
        #region Properties
      
        /// <summary>Gets the font set keyed by component.</summary>
        protected Dictionary<object, FontProviderInformation> ComponentToFontInformation
        {
            get;
            private set;
        }

        /// <summary>Gets the font set keyed by component.</summary>
        protected Dictionary<object, FontProviderTypeDescriptorProvider> ComponentToTypeDescriptorProvider
        {
            get;
            private set;
        }

        /// <summary>Gets or sets the delegate used to quest the font.</summary>
        public static Func<string, FontStyle, float, Font> RequestFont
        {
            get;
            set;
        }
  
        #endregion

        #region Constructors

        /// <summary>Creates a new instance of the <see cref="FontProvider"/> class.</summary>
        public FontProvider()
        {
            InitializeComponent();

            //Finish initialisation.
            this.ComponentToFontInformation = new Dictionary<object, FontProviderInformation>();
            this.ComponentToTypeDescriptorProvider = new Dictionary<object, FontProviderTypeDescriptorProvider>();
        }

        #endregion

        #region Public Methods
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

        /// <summary>Gets the font set for the component passed.</summary>
        /// <param name="component">The component to get the font set for.</param>
        [DefaultValue(null)]
        public string GetFontSet(object component)
        {
            return this.GetFontInformation(component).Set;
        }

        /// <summary>Gets the font size for the component passed.</summary>
        /// <param name="component">The component to get the font set for.</param>
        [DefaultValue(8.25f)]
        public float GetFontSize(object component)
        {
            return this.GetFontInformation(component).Size;
        }

        /// <summary>Gets the font style for the component passed.</summary>
        /// <param name="component">The component to get the font set for.</param>
        [DefaultValue(FontStyle.Regular)]
        public FontStyle GetFontStyle(object component)
        {
            return this.GetFontInformation(component).Style;
        }

        /// <summary>Sets the font set for the component passed.</summary>
        /// <param name="component">The component to set the font set for.</param>
        /// <param name="fontSet">The name of the font set used.</param>
        public void SetFontSet(object component, string fontSet)
        {
            FontProviderInformation information = this.GetFontInformation(component);
            this.SetFontInformation(component, new FontProviderInformation() { Set = fontSet, Size = information.Size, Style = information.Style });
        }

        /// <summary>Sets the font size for the component passed.</summary>
        /// <param name="component">The component to set the font set for.</param>
        /// <param name="fontSize">The size of the font.</param>
        public void SetFontSize(object component, float fontSize)
        {
            FontProviderInformation information = this.GetFontInformation(component);
            this.SetFontInformation(component, new FontProviderInformation() { Set = information.Set, Size = fontSize, Style = information.Style });
        }

        /// <summary>Sets the font set for the component passed.</summary>
        /// <param name="component">The component to set the font set for.</param>
        /// <param name="fontStyle">The name of the font set used.</param>
        public void SetFontStyle(object component, FontStyle fontStyle)
        {
            FontProviderInformation information = this.GetFontInformation(component);
            this.SetFontInformation(component, new FontProviderInformation() { Set = information.Set, Size = information.Size, Style = fontStyle });
        }

        #endregion

        #region Protected Methods

        /// <summary>Gets the font set for the component passed.</summary>
        /// <param name="component">The component to get the font set for.</param>
        [DefaultValue(null), DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        protected FontProviderInformation GetFontInformation(object component)
        {
            if (this.ComponentToFontInformation.ContainsKey(component))
                return this.ComponentToFontInformation[component];
            else
                return new FontProviderInformation();
        }

        /// <summary>Sets the font set for the component passed.</summary>
        /// <param name="component">The component to set the font set for.</param>
        /// <param name="information">The name of the font set used.</param>
        protected void SetFontInformation(object component, FontProviderInformation information)
        {
            if (information.Equals(FontProviderInformation.Empty))
            {
                if (this.ComponentToFontInformation.ContainsKey(component))
                {
                    //Reset the font.
                    foreach (PropertyInfo property in component.GetType().GetProperties())
                    {
                        if (property.CanRead && property.CanWrite && property.PropertyType == typeof(Font))
                            property.SetValue(component, null, null);
                    }

                    if (this.DesignMode)
                    {
                        //Remove the font set.
                        this.ComponentToFontInformation.Remove(component);

                        //Remove the type descriptor provider.
                        TypeDescriptor.RemoveProvider(this.ComponentToTypeDescriptorProvider[component], component);
                        this.ComponentToTypeDescriptorProvider.Remove(component);
                    }
                }
            }
            else
            {
                this.ComponentToFontInformation[component] = information;

                if (this.DesignMode)
                {
                    if (!this.ComponentToTypeDescriptorProvider.ContainsKey(component))
                    {
                        this.ComponentToTypeDescriptorProvider[component] = new FontProviderTypeDescriptorProvider(component, TypeDescriptor.GetProvider(component));
                        TypeDescriptor.AddProvider(this.ComponentToTypeDescriptorProvider[component], component);
                    }
                }
                else
                {
                    if (FontProvider.RequestFont == null)
                        throw new Exception("The \"RequestFont\" delegate hasn't been assigned.");

                    //Update the font.
                    Font font = FontProvider.RequestFont(information.Set, information.Style, information.Size);
                    foreach (PropertyInfo property in component.GetType().GetProperties())
                    {
                        if (property.CanRead && property.CanWrite && property.PropertyType == typeof(Font))
                            property.SetValue(component, font, null);
                    }
                }
            }
        }

        #endregion

        /// <summary>Used to represent a font.</summary>
        public class FontProviderInformation
        {
            #region Properties

            /// <summary>Gets the empty font provider information.</summary>
            public static FontProviderInformation Empty
            {
                get;
                private set;
            }

            /// <summary>Gets or sets the set.</summary>
            public string Set
            {
                get;
                set;
            }

            /// <summary>Gets or sets the size.</summary>
            public float Size
            {
                get;
                set;
            }

            /// <summary>Gets or sets the style.</summary>
            public FontStyle Style
            {
                get;
                set;
            }

            #endregion

            #region Constructors

            /// <summary>Creates the static instance of the <see cref="FontProviderInformation"/> struct.</summary>
            static FontProviderInformation()
            {
                FontProviderInformation.Empty = new FontProviderInformation();
            }

            /// <summary>Creates a new instances of the <see cref="FontProviderInformation"/> class.</summary>
            public FontProviderInformation()
            {
                this.Size = 8.25f;
                this.Style = FontStyle.Regular;
            }

            #endregion

            #region Public Methods

            /// <summary>Indicates whether this instance and a specified font provider information are equal.</summary>
            /// <param name="information">The information to check the equality of.</param>
            /// <returns>True if logically equal, otherwise false.</returns>
            public bool Equals(FontProviderInformation information)
            {
                return this.Set == information.Set && this.Size == information.Size && this.Style == information.Style;
            }

            /// <summary>Indicates whether this instance and a specified font provider information are equal.</summary>
            /// <param name="obj">Another object to compare to.</param>
            /// <returns>True if obj and this instance are the same type and represent the same value; otherwise, false.</returns>
            public override bool Equals(object obj)
            {
                return obj is FontProviderInformation ? this.Equals((FontProviderInformation)obj) : base.Equals(obj);
            }

            /// <summary>Returns the hash code for this instance.</summary>
            /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
            public override int GetHashCode()
            {
                int hashCode = base.GetHashCode();
                if (this.Set != null)
                    hashCode ^= this.Set.GetHashCode();

                hashCode ^= this.Size.GetHashCode();
                hashCode ^= this.Style.GetHashCode();

                return hashCode;
            }

            #endregion
        }

        /// <summary>Type descriptor used to hide any font property when using a font information.</summary>
        public class FontProviderTypeDescriptor : ICustomTypeDescriptor
        {
            #region Properties

            /// <summary>Gets the owner.</summary>
            protected object Owner
            {
                get;
                private set;
            }

            #endregion

            #region Constructors

            /// <summary>Creates a new instance of the <see cref="FontProviderTypeDescriptor"/> class.</summary>
            public FontProviderTypeDescriptor(object owner)
            {
                if (owner == null)
                    throw new ArgumentNullException("owner");

                this.Owner = owner;
            }

            #endregion

            #region Public Methods

            /// <summary>Returns a collection of custom attributes for this instance of a component.</summary>
            public AttributeCollection GetAttributes()
            {
                return TypeDescriptor.GetAttributes(this.Owner.GetType());
            }

            /// <summary>Returns the class name of this instance of a component.</summary>
            public string GetClassName()
            {
                return TypeDescriptor.GetClassName(this.Owner.GetType());
            }

            /// <summary>Returns the name of this instance of a component.</summary>
            public string GetComponentName()
            {
                return this.Owner is IComponent ? (this.Owner as IComponent).Site.Name : string.Empty;
            }

            /// <summary>Returns a type converter for this instance of a component.</summary>
            public TypeConverter GetConverter()
            {
                return TypeDescriptor.GetConverter(this.Owner.GetType());
            }

            /// <summary>Returns the default event for this instance of a component.</summary>
            public EventDescriptor GetDefaultEvent()
            {
                return TypeDescriptor.GetDefaultEvent(this.Owner.GetType());
            }

            /// <summary>Returns the default property for this instance of a component.</summary>
            public PropertyDescriptor GetDefaultProperty()
            {
                return TypeDescriptor.GetDefaultProperty(this.Owner.GetType());
            }

            /// <summary>Returns an editor of the specified type for this instance of a component.</summary>
            public object GetEditor(Type editorBaseType)
            {
                return TypeDescriptor.GetEditor(this.Owner.GetType(), editorBaseType);
            }

            /// <summary>Returns the events for this instance of a component.</summary>
            public EventDescriptorCollection GetEvents()
            {
                return TypeDescriptor.GetEvents(this.Owner.GetType());
            }

            /// <summary>Returns the events for this instance of a component using the specified attribute array as a filter.</summary>
            public EventDescriptorCollection GetEvents(Attribute[] attributeList)
            {
                return TypeDescriptor.GetEvents(this.Owner.GetType(), attributeList);
            }

            /// <summary>Returns the properties for this instance of a component.</summary>
            public PropertyDescriptorCollection GetProperties()
            {
                return this.GetProperties(new Attribute[0]);
            }

            /// <summary>Returns the properties for this instance of a component using the attribute array as a filter.</summary>
            public PropertyDescriptorCollection GetProperties(Attribute[] attributeList)
            {
                PropertyDescriptorCollection pd_PropertyDescriptorList = TypeDescriptor.GetProperties(this.Owner.GetType(), attributeList);

                PropertyDescriptor[] propertyDescriptorList = new PropertyDescriptor[pd_PropertyDescriptorList.Count];
                pd_PropertyDescriptorList.CopyTo(propertyDescriptorList, 0);
                pd_PropertyDescriptorList = new PropertyDescriptorCollection(propertyDescriptorList, false);

                foreach (PropertyDescriptor propertyDescriptor in pd_PropertyDescriptorList)
                {
                    if (propertyDescriptor.PropertyType == typeof(Font))
                    {
                        pd_PropertyDescriptorList.Remove(propertyDescriptor);
                        break;
                    }
                }

                return pd_PropertyDescriptorList;
            }

            /// <summary>Returns an object that contains the property described by the specified property descriptor.</summary>
            public object GetPropertyOwner(PropertyDescriptor propertyDescriptor)
            {
                return this.Owner;
            }

            #endregion
        }

        /// <summary>Type descriptor provider used to return the FontSetProviderTypeDescriptor type descriptor.</summary>
        public class FontProviderTypeDescriptorProvider : TypeDescriptionProvider
        {
            #region Properties

            /// <summary>Gets the owner of the descriptor provider.</summary>
            protected object Owner
            {
                get;
                private set;
            }

            #endregion

            #region Constructors

            /// <summary>Creates a new instance of the <see cref="FontProviderTypeDescriptorProvider"/> class.</summary>
            public FontProviderTypeDescriptorProvider(object owner, TypeDescriptionProvider parent)
                : base(parent)
            {
                if (owner == null)
                    throw new ArgumentNullException("owner");

                this.Owner = owner;
            }

            #endregion

            #region Public Methods

            /// <summary>Gets a custom type descriptor for the given type and object.</summary>
            public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
            {
                if (instance != null && instance is object && instance.Equals(this.Owner))
                    return new FontProviderTypeDescriptor(this.Owner);
                else
                    return base.GetTypeDescriptor(objectType, instance);
            }

            #endregion
        }
    }
}