using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.7 execution-boundary coverage that does not invoke the native installer.
	/// </summary>
	[TestFixture]
	public class CollectionNativeChildExecutionCoordinatorTests
	{
		[Test]
		public void ValidateExecutableFilePriorities_SingleWriterIsRepresentableByCurrentC5Plan()
		{
			CollectionMemberKey member = CollectionMemberKey.FromProvider("member-a");
			CollectionFileImpact impact = CreateImpact(new[] { member }, member);

			Assert.DoesNotThrow(() => InvokeFilePriorityValidation(new[] { impact }));
		}

		[Test]
		public void ValidateExecutableFilePriorities_MultipleSelectedWritersWithReviewedWinnerAreAllowedForC61511Reconciliation()
		{
			CollectionMemberKey lower = CollectionMemberKey.FromProvider("member-a");
			CollectionMemberKey winner = CollectionMemberKey.FromProvider("member-b");
			CollectionFileImpact impact = CreateImpact(new[] { lower, winner }, winner);

			Assert.DoesNotThrow(() => InvokeFilePriorityValidation(new[] { impact }));
		}

		[Test]
		public void ValidateExecutableFilePriorities_MultipleSelectedWritersWithoutReviewedWinnerStillBlock()
		{
			CollectionMemberKey first = CollectionMemberKey.FromProvider("member-a");
			CollectionMemberKey second = CollectionMemberKey.FromProvider("member-b");
			CollectionFileImpact impact = CreateImpact(new[] { first, second }, null);

			TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
				InvokeFilePriorityValidation(new[] { impact }));

			Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
			StringAssert.Contains("no deterministic C6.4 winner", error.InnerException.Message);
		}

		private static CollectionFileImpact CreateImpact(IEnumerable<CollectionMemberKey> writers, CollectionMemberKey winner)
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			return (CollectionFileImpact)Activator.CreateInstance(typeof(CollectionFileImpact),
				BindingFlags.Instance | BindingFlags.NonPublic, null,
				new object[] { target, writers, winner, null, new Guid[0] }, CultureInfo.InvariantCulture);
		}

		private static void InvokeFilePriorityValidation(IEnumerable<CollectionFileImpact> impacts)
		{
			MethodInfo method = typeof(CollectionNativeChildExecutionCoordinator).GetMethod(
				"ValidateExecutableFilePriorities", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			method.Invoke(null, new object[] { impacts });
		}
	}
}
