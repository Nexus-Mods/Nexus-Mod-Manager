namespace Nexus.Client.ModRepositories
{
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

        public ModFileInfo(string id, string filename, string name, string modVersion)
        {
            Id = id;
            Filename = filename;
            Name = name;
            HumanReadableVersion = modVersion;
        }
    }
}
