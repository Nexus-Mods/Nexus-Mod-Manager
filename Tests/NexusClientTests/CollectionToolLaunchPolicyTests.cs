using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[Category("CollectionsC12Compatibility")]
	public class CollectionToolLaunchPolicyTests
	{
		[Test]
		public void CreateStartInfo_ResolvesGameRelativeExecutableWorkingDirectoryArgumentsAndEnvironment()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-collection-tool-root");
			var tool = new CollectionLaunchTool("Tool", "Tools\\Runner.exe", new[] { "--name", "A B", "quote\"value" },
				"Tools", new Dictionary<string, string> { { "NMM_COLLECTION_TEST", "yes" } });

			ProcessStartInfo startInfo = CollectionToolLaunchPolicy.CreateStartInfo(tool, root);

			Assert.AreEqual(Path.Combine(root, "Tools", "Runner.exe"), startInfo.FileName);
			Assert.AreEqual(Path.Combine(root, "Tools"), startInfo.WorkingDirectory);
			Assert.IsFalse(startInfo.UseShellExecute);
			Assert.AreEqual("--name \"A B\" \"quote\\\"value\"", startInfo.Arguments);
			Assert.AreEqual("yes", startInfo.EnvironmentVariables["NMM_COLLECTION_TEST"]);
		}

		/// <summary>Explicit shell launches allow Windows to handle an executable's normal elevation requirements.</summary>
		[Test]
		public void CreateStartInfo_PreservesShellLaunchForGameRelativePatcher()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-collection-tool-root");
			var tool = new CollectionLaunchTool("4GB RAM Patcher Tool", "4gb patch.exe", new string[0], null, null, true);
			ProcessStartInfo startInfo = CollectionToolLaunchPolicy.CreateStartInfo(tool, root);
			Assert.IsTrue(startInfo.UseShellExecute);
			Assert.AreEqual(Path.Combine(root, "4gb patch.exe"), startInfo.FileName);
			Assert.AreEqual(root, startInfo.WorkingDirectory);
		}

		[Test]
		public void ResolvePath_RejectsTraversalEvenWhenCalledWithUntrustedRelativeInput()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-collection-tool-root");
			Assert.Throws<InvalidDataException>(() => CollectionToolLaunchPolicy.ResolvePath(root, "..\\escape.exe"));
		}
	}
}
