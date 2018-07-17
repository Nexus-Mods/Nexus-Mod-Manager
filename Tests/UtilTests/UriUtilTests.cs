namespace UtilTests
{
    using System;
    using Nexus.Client.Util;
    using NUnit.Framework;

    [TestFixture]
    public class UriUtilTests
    {
        [Test]
        public void ValidUriTest()
        {
            Assert.IsTrue(UriUtil.TryBuildUri("http://www.nexusmods.com", out var result));
            Assert.AreEqual(new Uri("http://www.nexusmods.com"), result);

            Assert.IsTrue(UriUtil.TryBuildUri("https://www.nexusmods.com", out result));
            Assert.AreEqual(new Uri("https://www.nexusmods.com"), result);

            Assert.IsTrue(UriUtil.TryBuildUri("www.nexusmods.com", out result));
            Assert.AreEqual(new Uri("http://www.nexusmods.com"), result);

            Assert.IsTrue(UriUtil.TryBuildUri("nexusmods.com", out result));
            Assert.AreEqual(new Uri("http://nexusmods.com"), result);
        }

        [Test]
        public void InvalidUriTest()
        {
            Assert.IsFalse(UriUtil.TryBuildUri("this is not a URL", out var result));
            Assert.IsNull(result);

            Assert.IsFalse(UriUtil.TryBuildUri(string.Empty, out result));
            Assert.IsNull(result);
        }
    }
}
