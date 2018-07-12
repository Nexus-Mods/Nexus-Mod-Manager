namespace Nexus.Client.Settings
{
    /// <summary>
    /// Describes a file type association with the client.
    /// </summary>
    public class FileAssociationSetting
    {
        #region Properties

        /// <summary>
        /// Gets or sets the extention of the file type association.
        /// </summary>
        /// <value>The extention of the file type association.</value>
        public string Extension { get; set; }

        /// <summary>
        /// Gets or sets the description of the association.
        /// </summary>
        /// <value>The description of the association.</value>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets whether the file is associated with the client.
        /// </summary>
        /// <value>Whether the file is associated with the client.</value>
        public bool IsAssociated { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="extension">The extention of the file type association.</param>
        /// <param name="description">The description of the association.</param>
        /// <param name="isAssociated">Whether the file is associated with the client.</param>
        public FileAssociationSetting(string extension, string description, bool isAssociated)
        {
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            Extension = extension;
            Description = description;
            IsAssociated = isAssociated;
        }

        #endregion
    }
}
