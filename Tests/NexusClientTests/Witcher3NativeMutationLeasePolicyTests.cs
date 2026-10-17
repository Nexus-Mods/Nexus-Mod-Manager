using System;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public class Witcher3NativeMutationLeasePolicyTests
    {
        [Test]
        public void RecoveredAndValidatedCrossProcessRoot_IsRequired()
        {
            Assert.DoesNotThrow(() => Witcher3NativeMutationLeasePolicy.RequireRootAuthority(true, true, true, false));
        }

        [TestCase(false, true, true, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, true, true, true)]
        [TestCase(false, false, false, false)]
        [TestCase(false, true, false, true)]
        public void ProcessOnlyUnvalidatedInheritedOrRecoveryLease_IsNotWriteAuthority(
            bool isRoot, bool crossProcess, bool validated, bool recovery)
        {
            Assert.Throws<InvalidOperationException>(() =>
                Witcher3NativeMutationLeasePolicy.RequireRootAuthority(isRoot, crossProcess, validated, recovery));
        }
    }
}
