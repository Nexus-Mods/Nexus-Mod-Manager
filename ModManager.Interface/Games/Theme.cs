using System;
using System.Collections.Generic;
using System.Drawing;

using Nexus.Client.Util;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Describes visual features of an application.
	/// </summary>
	public class Theme
    {
        #region Properties

        /// <summary>Gets the default theme.</summary>
        public static Theme Default
        {
            get;
            private set;
        }

        /// <summary>Gets the default font set.</summary>
        public static FontSet DefaultFontSet
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the icon to use for this theme.
        /// </summary>
        /// <value>The icon to use for this theme.</value>
        public Icon Icon
        {
            get;
            private set;
        }

        /// <summary>Gets the font sets.</summary>
        public Dictionary<string, FontSet> FontSets
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the theme's primary colour.
        /// </summary>
        /// <value>The theme's primary colour.</value>
        public Color PrimaryColour
        {
            get;
            private set;
        }

        /// <summary>Gets the toolbar font set.</summary>
        public static FontSet ToolbarFontSet
        {
            get;
            private set;
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates the static instance of the <see cref="Theme"/> class.
        /// </summary>
        static Theme()
        {
            //Create the font sets.
            Theme.DefaultFontSet = new FontSet(new string[2] { "Microsoft Sans Serif", "Arial" }, FontStyle.Regular, FontStyle.Bold);
            Theme.ToolbarFontSet = new FontSet(new string[2] { "Segoe UI", "Arial" }, FontStyle.Regular);

            //Create the default theme, this is used when no game mode has been initialised.
            Theme.Default = new Theme(null, Color.Black, Theme.DefaultFontSet, Theme.ToolbarFontSet);
        }

        /// <summary>
        /// A simple constructor that initializes the theme.
        /// </summary>
        /// <param name="p_icnIcon">The icon to use for this theme.</param>
        /// <param name="p_clrPrimary">The theme's primary colour.</param>
        /// <param name="p_DefaultFontSet">The theme's default font set.</param>
        /// <param name="p_ToolbarFontSet">The theme's toolbar font set.</param>
        /// <exception cref="ArgumentNullException">An argument null exception will be thrown if either <paramref name="p_DefaultFontSet"/> or <paramref name="p_ToolbarFontSet"/> are null.</exception>
        public Theme(Icon p_icnIcon, Color p_clrPrimary, FontSet p_DefaultFontSet, FontSet p_ToolbarFontSet)
        {
            if (p_DefaultFontSet == null)
                throw new ArgumentNullException("p_DefaultFontSet");

            if (p_ToolbarFontSet == null)
                throw new ArgumentNullException("p_ToolbarFontSet");

            this.Icon = p_icnIcon;
            this.PrimaryColour = p_clrPrimary;            

            //Create the font sets.
            this.FontSets = new Dictionary<string, FontSet>();
            this.FontSets.Add("Default", p_DefaultFontSet);
            this.FontSets.Add("Toolbar", p_ToolbarFontSet);
        }
       
        #endregion

        #region Public Methods

        /// <summary>Used to create the font with the style and size requested.</summary>
        /// <param name="name">The name of the font set to use.</param>
        /// <param name="style">The style of font to create.</param>
        /// <param name="size">The size of font to create.</param>
        /// <returns>The font created.</returns>
        public Font CreateFont(string name, FontStyle style, float size)
        {
            if (this.FontSets.ContainsKey(name))
                return this.FontSets[name].CreateFont(style, size);
            else
                throw new ArgumentException("The name passed is invalid, name dosn't match a font set.");
        }

        #endregion
    }
}