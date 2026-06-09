namespace NexusClientTests
{
    using System.Collections.Generic;
    using System.Diagnostics;

    using NUnit.Framework;

    /// <summary>
    /// Thin test double that mirrors InstallerStack.RemoveAll(ISet&lt;string&gt;) so we can
    /// test the algorithm directly without exposing the private inner class.
    /// </summary>
    internal sealed class TestableInstallerStack
    {
        private readonly List<(string Key, int Value)> _items = new List<(string, int)>();

        public int Count => _items.Count;

        public void Push(string key, int value) => _items.Add((key, value));

        public string GetInstallerKey(int index) => _items[index].Key;

        // Mirrors InstallerStack<T>.RemoveAll — single backward pass, O(1) hash lookup.
        public void RemoveAll(ISet<string> keys)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
                if (keys.Contains(_items[i].Key))
                    _items.RemoveAt(i);
        }
    }

    /// <summary>
    /// Regression tests for issue #1362: O(n²) safeguard cleanup in install log commit paths.
    /// The fix changed the inner mod-key loop to InstallerStack.RemoveAll(ISet&lt;string&gt;),
    /// which does a single O(n) pass with O(1) hash lookups instead of O(n×m) nested iteration.
    /// </summary>
    [TestFixture]
    public class InstallerStackPerformanceTests
    {
        [Test]
        [Timeout(5000)]
        public void RemoveAll_LargeInputs_CompletesUnder500ms()
        {
            // Build a stack with 2000 entries from 200 distinct "mod keys".
            var stack = new TestableInstallerStack();
            for (int i = 0; i < 2000; i++)
                stack.Push("mod" + (i % 200), i);

            // Mark 100 of those 200 mods as removed.
            var removed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 100; i++)
                removed.Add("mod" + i);

            var sw = Stopwatch.StartNew();
            stack.RemoveAll(removed);
            sw.Stop();

            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500),
                "RemoveAll should complete in well under 500 ms for 2000 entries and 100 removed keys.");

            // Verify correctness: no surviving entry should belong to a removed mod.
            for (int i = 0; i < stack.Count; i++)
                Assert.IsFalse(removed.Contains(stack.GetInstallerKey(i)),
                    "Removed mod key survived in stack after RemoveAll.");
        }

        [Test]
        public void RemoveAll_EmptySet_LeavesStackUnchanged()
        {
            var stack = new TestableInstallerStack();
            stack.Push("modA", 1);
            stack.Push("modB", 2);

            stack.RemoveAll(new HashSet<string>());

            Assert.AreEqual(2, stack.Count);
        }

        [Test]
        public void RemoveAll_AllKeysRemoved_EmptiesStack()
        {
            var stack = new TestableInstallerStack();
            stack.Push("modA", 1);
            stack.Push("modB", 2);

            var keys = new HashSet<string> { "modA", "modB" };
            stack.RemoveAll(keys);

            Assert.AreEqual(0, stack.Count);
        }

        [Test]
        public void RemoveAll_NoMatchingKeys_LeavesStackUnchanged()
        {
            var stack = new TestableInstallerStack();
            stack.Push("modA", 1);
            stack.Push("modB", 2);

            stack.RemoveAll(new HashSet<string> { "modX", "modY" });

            Assert.AreEqual(2, stack.Count);
        }
    }
}
