using System;
using System.IO;
using NUnit.Framework;
using SevenZip;

namespace NexusClientTests
{
	/// <summary>
	/// Configures native dependencies required by archive-backed integration tests.
	/// </summary>
	[SetUpFixture]
	public sealed class SevenZipTestEnvironment
	{
		/// <summary>
		/// Configures SevenZipSharp with the native library copied beside the test output.
		/// </summary>
		[OneTimeSetUp]
		public void ConfigureSevenZipLibrary()
		{
			string libraryName = IntPtr.Size == 8 ? "7z-64bit.dll" : "7z-32bit.dll";
			string libraryPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "data", libraryName);

			if (!File.Exists(libraryPath))
				throw new FileNotFoundException("The SevenZip native test dependency was not copied to the test output.", libraryPath);

			SevenZipCompressor.SetLibraryPath(libraryPath);
		}
	}
}
