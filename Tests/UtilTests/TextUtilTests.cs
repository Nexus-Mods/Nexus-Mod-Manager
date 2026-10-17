namespace UtilTests
{
    using System.Linq;
    using System.Text;

    using Nexus.Client.Util;

    using NUnit.Framework;

    [TestFixture]
    public class TextUtilTests
    {
        private readonly byte[] _thisIsATest = { 0x74, 0x68, 0x69, 0x73, 0x20, 0x69, 0x73, 0x20, 0x61, 0x20, 0x74, 0x65, 0x73, 0x74 };

        /// <summary>Decodes empty input and preserves logical lines across supported line endings.</summary>
        [TestCase("", new string[0])]
        [TestCase("this is a test", new[] { "this is a test" })]
        [TestCase("first\r\nsecond", new[] { "first", "second" })]
        [TestCase("first\nsecond\n", new[] { "first", "second" })]
        [TestCase("first\rsecond", new[] { "first", "second" })]
        [TestCase("first\n\nthird", new[] { "first", "", "third" })]
        [TestCase("\n", new[] { "" })]
        public void ByteToStringLinesTest(string text, string[] expected)
        {
            CollectionAssert.AreEqual(expected, TextUtil.ByteToStringLines(Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>Honors byte-order marks without leaking them into decoded text or lines.</summary>
        [TestCase("utf-8")]
        [TestCase("utf-16")]
        [TestCase("utf-16BE")]
        [TestCase("utf-32")]
        public void ByteToString_DetectsEncodingFromByteOrderMark(string encodingName)
        {
            const string text = "café\r\n世界";
            Encoding encoding = Encoding.GetEncoding(encodingName);
            byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

            Assert.AreEqual(text, TextUtil.ByteToString(bytes));
            CollectionAssert.AreEqual(new[] { "café", "世界" }, TextUtil.ByteToStringLines(bytes));
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
