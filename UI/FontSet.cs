using System;
using System.Drawing;

namespace Nexus.UI
{
    /// <summary>Used to represent a list of fonts.</summary>
    public class FontSet
    {
        #region Properties

        /// <summary>Gets the families.</summary>
        public string[] Families
        {
            get;
            private set;
        }

        /// <summary>Gets the validated font family.</summary>
        protected FontFamily Family
        {
            get;
            private set;
        }

        /// <summary>Gets the styles.</summary>
        public FontStyle[] Styles
        {
            get;
            private set;
        }

        #endregion

        #region Constructors
     
        /// <summary>Creates a new instance of the <see cref="FontSet"/> class.</summary>
        /// <param name="families">The list of font familes this set use. The familes will try and be used in the order there passed, e.g. index 0 to n.</param>
        /// <param name="styles">The list of styles the font must have to be used.</param>
        /// <exception cref="ArgumentNullException">If the <paramref name="families"/> or <paramref name="styles"/> parameters are null.</exception>
        /// <exception cref="ArgumentException">If the <paramref name="families"/> or <paramref name="styles"/> parameters are zero length, or the <paramref name="families"/> parameters contains null, or empty string entires.</exception>
        public FontSet(string[] families, params FontStyle[] styles)
        {
            if (families == null)
                throw new ArgumentNullException("families");

            if (families.Length == 0)
                throw new ArgumentException("Invalid argument passed, the families parameter can't be zero length.");

            if (Array.Exists(families, delegate(string family) { return string.IsNullOrEmpty(family); }))
                throw new ArgumentException("Invalid argument passed, the families parameter contains a null or empty value.");

            if (styles == null)
                throw new ArgumentNullException("styles");

            if (styles.Length == 0)
                throw new ArgumentException("Invalid argument passed, the styles parameter can't be zero length.");

            this.Families = families;
            this.Styles = styles;
        }
      
        #endregion
        
        #region Public Methods

        /// <summary>Used to create the font with the style and size requested.</summary>
        /// <param name="style">The style of font to create.</param>
        /// <param name="size">The size of font to create.</param>
        /// <returns>The font created.</returns>
        public Font CreateFont(FontStyle style, float size)
        {
            if (Array.Exists<FontStyle>(this.Styles, delegate(FontStyle match) { return match == style; }))
                return new Font(this.GetValidatedFontFamily(), size, style);
            else
                throw new ArgumentException(string.Format("Invalid font style passed, the font set wasn't declared with the style: \"{0}\".", style.ToString()));
        }
       
        #endregion

        #region Protected Methods

        /// <summary>Gets the validate font family.</summary>
        /// <returns>The font family found.</returns>
        protected FontFamily GetValidatedFontFamily()
        {
            lock (this)
            {
                if (this.Family == null)
                {
                    Font font = null;
                    for (int i1 = 0, i2; i1 < this.Families.Length; i1++)
                    {
                        for (i2 = 0; i2 < this.Styles.Length; i2++)
                        {
                            font = null;

                            try
                            {
                                if((font = FontManager.GetFont(this.Families[i1], 8.25f, this.Styles[i2])) == null)
                                    font = new Font(this.Families[i1], 8.25f, this.Styles[i2]);
                                
                                //Check to make sure a fallback font wasn't used.
                                if (font != null && (font.FontFamily.Name != this.Families[i1] || font.Style != this.Styles[i2]))
                                {
                                    font = null;
                                    break;
                                }
                            }
                            catch
                            {
                                //Ignore exception and try and get the next font.
                                break;
                            }
                        }

                        //If font is set must be valid for all style's.
                        if (font != null)
                        {
                            this.Family = font.FontFamily;
                            font.Dispose();

                            break;
                        }
                    }

                    if (this.Family == null)
                        throw new Exception("An error occured when validating the font family, no valid font family was found.");
                }

                return this.Family;
            }
        }
      
        #endregion
    }
}