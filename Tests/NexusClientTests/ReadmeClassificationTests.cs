using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class ReadmeClassificationTests
	{
		[TestCase("readme.txt", true)]
		[TestCase("notes.txt", true)]
		[TestCase("script.txt", false)]
		[TestCase("config.txt", false)]
		[TestCase("my-config.txt", false)]
		public void IsValidReadme_DistinguishesDocumentationFromKnownTxtFiles(string path, bool expected)
		{
			Assert.That(Readme.IsValidReadme(path), Is.EqualTo(expected));
		}
	}
}
