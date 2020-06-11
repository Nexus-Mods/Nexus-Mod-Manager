namespace Nexus.Client.ModRepositories
{
    using Pathoschild.FluentNexus.Models;

    public class ModFileInfo :  IModFileInfo
    {
        /// <inheritdoc />
        public string Id { get; }
        
        /// <inheritdoc />
        public string Filename { get; }
        
        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public string HumanReadableVersion { get; }

        /// <summary>
        /// Creates a <see cref="ModFileInfo"/> from a <see cref="ModFile"/>.
        /// </summary>
        /// <param name="modFile">ModFile to get information from.</param>
        public ModFileInfo(ModFile modFile)
        {
            Id = modFile?.FileID.ToString();
            Filename = modFile?.FileName;
            Name = modFile?.Name;
            HumanReadableVersion = modFile?.ModVersion;
        }

        public ModFileInfo(string id, string filename, string name, string modVersion)
        {
            Id = id;
            Filename = filename;
            Name = name;
            HumanReadableVersion = modVersion;
        }
    }
}
