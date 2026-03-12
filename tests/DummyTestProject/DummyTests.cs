namespace DummyTestProject.Arithmetic
{
    [TestClass]
    public class BasicMathTests
    {
        [TestMethod]
        public void Addition_ReturnsCorrectResult()
        {
            Assert.AreEqual(4, 2 + 2);
        }

        [TestMethod]
        public void Subtraction_ReturnsCorrectResult()
        {
            Assert.AreEqual(3, 7 - 4);
        }

        [TestMethod]
        public void Multiplication_ReturnsCorrectResult()
        {
            Assert.AreEqual(42, 6 * 7);
        }

        [TestMethod]
        [DataRow(1, 1, 2)]
        [DataRow(0, 0, 0)]
        [DataRow(-1, 1, 0)]
        [DataRow(100, 200, 300)]
        public void Addition_Parameterized(int a, int b, int expected)
        {
            Assert.AreEqual(expected, a + b);
        }

        [TestMethod]
        [DataRow(10, 2, 5)]
        [DataRow(9, 3, 3)]
        [DataRow(100, 10, 10)]
        public void Division_Parameterized(int a, int b, int expected)
        {
            Assert.AreEqual(expected, a / b);
        }
    }

    [TestClass]
    public class FailableTests
    {
        private static bool ShouldFail =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FAIL_TESTS"));

        [TestMethod]
        public void ConditionalFailure_One()
        {
            if (ShouldFail)
                Assert.Fail("FAIL_TESTS is set — deliberate failure #1");

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void ConditionalFailure_Two()
        {
            if (ShouldFail)
                Assert.Fail("FAIL_TESTS is set — deliberate failure #2");

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void ConditionalFailure_Three()
        {
            if (ShouldFail)
                Assert.Fail("FAIL_TESTS is set — deliberate failure #3");

            Assert.IsTrue(true);
        }
    }
}

namespace DummyTestProject.StringOps
{
    [TestClass]
    public class StringTests
    {
        [TestMethod]
        public void String_Contains_Substring()
        {
            Assert.IsTrue("hello world".Contains("world"));
        }

        [TestMethod]
        public void String_ToUpper_Works()
        {
            Assert.AreEqual("HELLO", "hello".ToUpper());
        }

        [TestMethod]
        public void String_Trim_RemovesWhitespace()
        {
            Assert.AreEqual("test", "  test  ".Trim());
        }

        [TestMethod]
        [DataRow("abc", "cba")]
        [DataRow("hello", "olleh")]
        [DataRow("", "")]
        public void String_Reverse(string input, string expected)
        {
            var reversed = new string(input.Reverse().ToArray());
            Assert.AreEqual(expected, reversed);
        }

        [TestMethod]
        public void String_Split_CorrectCount()
        {
            var parts = "a,b,c,d".Split(',');
            Assert.AreEqual(4, parts.Length);
        }
    }
}

namespace DummyTestProject.Collections
{
    [TestClass]
    public class CollectionTests
    {
        [TestMethod]
        public void List_Add_IncreasesCount()
        {
            var list = new List<int> { 1, 2, 3 };
            list.Add(4);
            Assert.AreEqual(4, list.Count);
        }

        [TestMethod]
        public void Dictionary_ContainsKey()
        {
            var dict = new Dictionary<string, int> { ["key"] = 42 };
            Assert.IsTrue(dict.ContainsKey("key"));
            Assert.AreEqual(42, dict["key"]);
        }

        [TestMethod]
        public void HashSet_NoDuplicates()
        {
            var set = new HashSet<int> { 1, 2, 3, 1, 2 };
            Assert.AreEqual(3, set.Count);
        }

        [TestMethod]
        public void Queue_DequeuesInOrder()
        {
            var queue = new Queue<string>();
            queue.Enqueue("first");
            queue.Enqueue("second");
            Assert.AreEqual("first", queue.Dequeue());
            Assert.AreEqual("second", queue.Dequeue());
        }

        [TestMethod]
        public void Stack_PopsInReverseOrder()
        {
            var stack = new Stack<int>();
            stack.Push(1);
            stack.Push(2);
            Assert.AreEqual(2, stack.Pop());
            Assert.AreEqual(1, stack.Pop());
        }
    }
}

namespace DummyTestProject.Performance
{
    [TestClass]
    public class SlowTests
    {
        [TestMethod]
        public void SlowTest_One()
        {
            Thread.Sleep(2000);
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void SlowTest_Two()
        {
            Thread.Sleep(2000);
            Assert.IsTrue(true);
        }
    }
}
