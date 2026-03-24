using System.Runtime.CompilerServices;

[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]

namespace DummyTestProject.DisplayNames
{
    /// <summary>
    /// Tests with explicit [DisplayName] attributes where the display name differs from the FQN.
    /// Used to verify the custom ##ptr logger correctly reports FQNs for retry matching.
    /// </summary>
    [TestClass]
    public class DisplayNameTests
    {
        [TestMethod("Adding two positive numbers returns correct sum")]
        public void Addition_PositiveNumbers_ReturnsSum()
        {
            Assert.AreEqual(5, 2 + 3);
        }

        [TestMethod("Subtracting gives the right answer")]
        public void Subtraction_BasicOperation_Works()
        {
            Assert.AreEqual(3, 7 - 4);
        }

        [TestMethod("Multiplying two numbers together")]
        public void Multiplication_TwoNumbers_Correct()
        {
            Assert.AreEqual(42, 6 * 7);
        }

        private static bool ShouldFail =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FAIL_DISPLAY_NAME_TESTS"));

        [TestMethod("This display name test should fail when triggered")]
        public void DisplayName_ConditionalFailure()
        {
            if (ShouldFail)
                Assert.Fail("FAIL_DISPLAY_NAME_TESTS is set — deliberate failure");

            Assert.IsTrue(true);
        }
    }
}

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
    public class TransientFailureTests
    {
        [TestMethod]
        public void TransientFailure_FailsOnceThenPasses()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FAIL_ONCE")))
            {
                Assert.IsTrue(true);
                return;
            }

            var markerPath = Path.Combine(Path.GetTempPath(), "parallel_test_runner_fail_once.marker");
            if (!File.Exists(markerPath))
            {
                File.WriteAllText(markerPath, "failed");
                Assert.Fail("FAIL_ONCE: first run — deliberate transient failure");
            }
            else
            {
                File.Delete(markerPath);
                Assert.IsTrue(true);
            }
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

namespace DummyTestProject.LongNamedTests
{
    [TestClass]
    public class VeryLongTestNameClass_ForFilterStringStressTesting
    {
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_001() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_002() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_003() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_004() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_005() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_006() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_007() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_008() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_009() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_010() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_011() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_012() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_013() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_014() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_015() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_016() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_017() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_018() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_019() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_020() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_021() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_022() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_023() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_024() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_025() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_026() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_027() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_028() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_029() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_030() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_031() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_032() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_033() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_034() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_035() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_036() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_037() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_038() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_039() => Assert.IsTrue(true);
        [TestMethod] public void Verification_Of_Customer_Account_Registration_With_Full_Address_And_PostCode_Validation_Scenario_040() => Assert.IsTrue(true);
    }
}

namespace DummyTestProject.Concurrency
{
    /// <summary>
    /// These tests detect if they are running in parallel. They sleep and check a shared counter.
    /// With [assembly: Parallelize(Workers = 4)], these would FAIL if run via plain dotnet test.
    /// The ParallelTestRunner tool forces Workers=1, so they should PASS through the tool.
    /// </summary>
    [TestClass]
    public class SequentialExecutionTests
    {
        private static int _concurrentCount;

        [TestMethod]
        public void SequentialCheck_A()
        {
            var count = Interlocked.Increment(ref _concurrentCount);
            try
            {
                Assert.AreEqual(1, count, $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
                Thread.Sleep(100);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentCount);
            }
        }

        [TestMethod]
        public void SequentialCheck_B()
        {
            var count = Interlocked.Increment(ref _concurrentCount);
            try
            {
                Assert.AreEqual(1, count, $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
                Thread.Sleep(100);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentCount);
            }
        }

        [TestMethod]
        public void SequentialCheck_C()
        {
            var count = Interlocked.Increment(ref _concurrentCount);
            try
            {
                Assert.AreEqual(1, count, $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
                Thread.Sleep(100);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentCount);
            }
        }

        [TestMethod]
        public void SequentialCheck_D()
        {
            var count = Interlocked.Increment(ref _concurrentCount);
            try
            {
                Assert.AreEqual(1, count, $"Expected 1 concurrent test but found {count} — tests are running in parallel!");
                Thread.Sleep(100);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentCount);
            }
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
            Thread.Sleep(200);
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void SlowTest_Two()
        {
            Thread.Sleep(200);
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void HangingTest_ConditionalBlock()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HANG_TEST")))
            {
                // Simulate a hanging test — blocks until idle timeout kills it
                Thread.Sleep(TimeSpan.FromSeconds(30));
            }

            Assert.IsTrue(true);
        }
    }
}
