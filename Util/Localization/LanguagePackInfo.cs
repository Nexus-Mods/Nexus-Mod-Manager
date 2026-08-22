namespace Nexus.Client.Util.Localization
{
	/// <summary>
	/// Describes an available NMM UI language without retaining its translation catalog in memory.
	/// </summary>
	public sealed class LanguagePackInfo
	{
		internal LanguagePackInfo(int formatVersion, string id, string name, string culture, string author, string filePath, bool isBuiltIn)
		{
			FormatVersion = formatVersion;
			Id = id;
			Name = name;
			Culture = culture;
			Author = author;
			FilePath = filePath;
			IsBuiltIn = isBuiltIn;
		}

		public int FormatVersion { get; private set; }
		public string Id { get; private set; }
		public string Name { get; private set; }
		public string Culture { get; private set; }
		public string Author { get; private set; }
		public string FilePath { get; private set; }
		public bool IsBuiltIn { get; private set; }
	}
}
