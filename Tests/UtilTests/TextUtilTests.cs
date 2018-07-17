namespace UtilTests
{
    using Nexus.Client.Util;

    using NUnit.Framework;

    [TestFixture]
    public class TextUtilTests
    {
        private readonly byte[] _thisIsATest = { 0x74, 0x68, 0x69, 0x73, 0x20, 0x69, 0x73, 0x20, 0x61, 0x20, 0x74, 0x65, 0x73, 0x74 };

        [Test]
        public void ByteToStringLinesTest()
        {
            var result = TextUtil.ByteToStringLines(new byte[0]);
            Assert.AreEqual("", result);

            Assert.Ignore("Can't write working test input/results.");
        }

        [Test]
        public void ByteToStringTest()
        {
            var result = TextUtil.ByteToString(new byte[0]);
            Assert.AreEqual("", result);

            const string thisIsATestString = "this is a test";

            result = TextUtil.ByteToString(_thisIsATest);
            Assert.AreEqual(thisIsATestString, result);
        }
    }
}
