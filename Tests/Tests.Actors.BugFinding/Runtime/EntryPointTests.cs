﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.SystematicTesting;
using Xunit;
using Xunit.Abstractions;
using CoyoteTasks = Microsoft.Coyote.Tasks;
using SystemTasks = System.Threading.Tasks;

namespace Microsoft.Coyote.Actors.BugFinding.Tests.Runtime
{
    public class EntryPointTests : BaseActorBugFindingTest
    {
        public EntryPointTests(ITestOutputHelper output)
            : base(output)
        {
        }

#pragma warning disable xUnit1013 // Public method should be marked as test
        [Test]
        public static void VoidTest() => Assert.True(true);

        [Test]
        public static void VoidTestWithNoRuntime() => Assert.True(true);

        [Test]
        public static void VoidTestWithRuntime(ICoyoteRuntime runtime) => Assert.NotNull(runtime);

        [Test]
        public static void VoidTestWithActorRuntime(IActorRuntime runtime) => Assert.NotNull(runtime);

        [Test]
        public static SystemTasks.Task SystemTaskTestWithNoRuntime()
        {
            Assert.True(true);
            return SystemTasks.Task.CompletedTask;
        }

        [Test]
        public static async SystemTasks.Task SystemAsyncTaskTestWithNoRuntime()
        {
            Assert.True(true);
            await SystemTasks.Task.CompletedTask;
        }

        [Test]
        public static SystemTasks.Task SystemTaskTestWithRuntime(ICoyoteRuntime runtime)
        {
            Assert.NotNull(runtime);
            return SystemTasks.Task.CompletedTask;
        }

        [Test]
        public static async SystemTasks.Task SystemAsyncTaskTestWithRuntime(ICoyoteRuntime runtime)
        {
            Assert.NotNull(runtime);
            await SystemTasks.Task.CompletedTask;
        }

        [Test]
        public static SystemTasks.Task SystemTaskTestWithActorRuntime(IActorRuntime runtime)
        {
            Assert.NotNull(runtime);
            return SystemTasks.Task.CompletedTask;
        }

        [Test]
        public static async SystemTasks.Task SystemAsyncTaskTestWithActorRuntime(IActorRuntime runtime)
        {
            Assert.NotNull(runtime);
            await SystemTasks.Task.CompletedTask;
        }

        [Test]
        public static CoyoteTasks.Task CoyoteTaskTestWithNoRuntime()
        {
            Assert.True(true);
            return CoyoteTasks.Task.CompletedTask;
        }

        [Test]
        public static async CoyoteTasks.Task CoyoteAsyncTaskTestWithNoRuntime()
        {
            Assert.True(true);
            await CoyoteTasks.Task.CompletedTask;
        }

        [Test]
        public static CoyoteTasks.Task CoyoteTaskTestWithRuntime(ICoyoteRuntime runtime)
        {
            Assert.NotNull(runtime);
            return CoyoteTasks.Task.CompletedTask;
        }

        [Test]
        public static async CoyoteTasks.Task CoyoteAsyncTaskTestWithRuntime(ICoyoteRuntime runtime)
        {
            Assert.NotNull(runtime);
            await CoyoteTasks.Task.CompletedTask;
        }

        [Test]
        public static CoyoteTasks.Task CoyoteTaskTestWithActorRuntime(IActorRuntime runtime)
        {
            Assert.NotNull(runtime);
            return CoyoteTasks.Task.CompletedTask;
        }

        [Test]
        public static async CoyoteTasks.Task CoyoteAsyncTaskTestWithActorRuntime(IActorRuntime runtime)
        {
            Assert.NotNull(runtime);
            await CoyoteTasks.Task.CompletedTask;
        }

        public static class Foo
        {
            [Test]
            public static void VoidTest() => Assert.True(true);
        }

        public static class Bar
        {
            [Test]
            public static void VoidTest() => Assert.True(true);
        }
#pragma warning restore xUnit1013 // Public method should be marked as test

        [Fact(Timeout = 5000)]
        public void TestVoidEntryPoint() => CheckTestMethod(nameof(VoidTestWithNoRuntime));

        [Fact(Timeout = 5000)]
        public void TestVoidEntryPointWithRuntime() => CheckTestMethod(nameof(VoidTestWithRuntime));

        [Fact(Timeout = 5000)]
        public void TestVoidEntryPointWithActorRuntime() => CheckTestMethod(nameof(VoidTestWithActorRuntime));

        [Fact(Timeout = 5000)]
        public void TestSystemTaskEntryPoint() => CheckTestMethod(nameof(SystemTaskTestWithNoRuntime));

        [Fact(Timeout = 5000)]
        public void TestSystemTaskEntryPointWithRuntime() => CheckTestMethod(nameof(SystemTaskTestWithRuntime));

        [Fact(Timeout = 5000)]
        public void TestSystemTaskEntryPointWithActorRuntime() => CheckTestMethod(nameof(SystemTaskTestWithActorRuntime));

        [Fact(Timeout = 5000)]
        public void TestSystemAsyncTaskEntryPoint() => CheckTestMethod(nameof(SystemAsyncTaskTestWithNoRuntime));

        [Fact(Timeout = 5000)]
        public void TestSystemAsyncTaskEntryPointWithRuntime() => CheckTestMethod(nameof(SystemAsyncTaskTestWithRuntime));

        [Fact(Timeout = 5000)]
        public void TestSystemAsyncTaskEntryPointWithActorRuntime() => CheckTestMethod(nameof(SystemAsyncTaskTestWithActorRuntime));

        [Fact(Timeout = 5000)]
        public void TestCoyoteTaskEntryPoint() => CheckTestMethod(nameof(CoyoteTaskTestWithNoRuntime));

        [Fact(Timeout = 5000)]
        public void TestCoyoteTaskEntryPointWithRuntime() => CheckTestMethod(nameof(CoyoteTaskTestWithRuntime));

        [Fact(Timeout = 5000)]
        public void TestCoyoteTaskEntryPointWithActorRuntime() => CheckTestMethod(nameof(CoyoteTaskTestWithActorRuntime));

        [Fact(Timeout = 5000)]
        public void TestCoyoteAsyncTaskEntryPoint() => CheckTestMethod(nameof(CoyoteAsyncTaskTestWithNoRuntime));

        [Fact(Timeout = 5000)]
        public void TestCoyoteAsyncTaskEntryPointWithRuntime() => CheckTestMethod(nameof(CoyoteAsyncTaskTestWithRuntime));

        [Fact(Timeout = 5000)]
        public void TestCoyoteAsyncTaskEntryPointWithActorRuntime() => CheckTestMethod(nameof(CoyoteAsyncTaskTestWithActorRuntime));

        [Fact(Timeout = 5000)]
        public void TestUnspecifiedEntryPoint()
        {
            string name = string.Empty;
            var exception = Assert.Throws<InvalidOperationException>(() => CheckTestMethod(name));

            string possibleNames = GetPossibleTestNames();
            string expected = $"System.InvalidOperationException: Found '18' test methods declared with the " +
                $"'{typeof(TestAttribute).FullName}' attribute. Provide --method (-m) flag to qualify the test " +
                $"method that you want to run. {possibleNames}   at";
            string actual = exception.ToString();

            Assert.StartsWith(expected, actual);
        }

        [Fact(Timeout = 5000)]
        public void TestNotExistingEntryPoint()
        {
            string name = "NotExistingEntryPoint";
            var exception = Assert.Throws<InvalidOperationException>(() => CheckTestMethod(name));

            string possibleNames = GetPossibleTestNames();
            string expected = "System.InvalidOperationException: Cannot detect a Coyote test method name " +
                $"containing {name}. {possibleNames}   at";
            string actual = exception.ToString();

            Assert.StartsWith(expected, actual);
        }

        [Fact(Timeout = 5000)]
        public void TestAmbiguousEntryPoint()
        {
            string name = "VoidTest";
            var exception = Assert.Throws<InvalidOperationException>(() => CheckTestMethod(name));

            string possibleNames = GetPossibleTestNames(name);
            string expected = $"System.InvalidOperationException: The method name '{name}' is ambiguous. " +
                $"Please specify the full test method name. {possibleNames}   at";
            string actual = exception.ToString();

            Assert.StartsWith(expected, actual);
        }

        private static void CheckTestMethod(string name)
        {
            Configuration config = Configuration.Create();
            config.AssemblyToBeAnalyzed = Assembly.GetExecutingAssembly().Location;
            config.TestMethodName = name;
            var testMethodInfo = TestMethodInfo.Create(config);

            Assert.Equal(Assembly.GetExecutingAssembly(), testMethodInfo.Assembly);
            Assert.Equal($"{typeof(EntryPointTests).FullName}.{name}", testMethodInfo.Name);
        }

        private static string GetPossibleTestNames(string ambiguousName = null)
        {
            var testNames = new List<(string qualifier, string name)>()
            {
                (typeof(EntryPointTests).FullName, nameof(VoidTest)),
                (typeof(EntryPointTests).FullName, nameof(VoidTestWithNoRuntime)),
                (typeof(EntryPointTests).FullName, nameof(VoidTestWithRuntime)),
                (typeof(EntryPointTests).FullName, nameof(VoidTestWithActorRuntime)),
                (typeof(EntryPointTests).FullName, nameof(SystemTaskTestWithNoRuntime)),
                (typeof(EntryPointTests).FullName, nameof(SystemAsyncTaskTestWithNoRuntime)),
                (typeof(EntryPointTests).FullName, nameof(SystemTaskTestWithRuntime)),
                (typeof(EntryPointTests).FullName, nameof(SystemAsyncTaskTestWithRuntime)),
                (typeof(EntryPointTests).FullName, nameof(SystemTaskTestWithActorRuntime)),
                (typeof(EntryPointTests).FullName, nameof(SystemAsyncTaskTestWithActorRuntime)),
                (typeof(EntryPointTests).FullName, nameof(CoyoteTaskTestWithNoRuntime)),
                (typeof(EntryPointTests).FullName, nameof(CoyoteAsyncTaskTestWithNoRuntime)),
                (typeof(EntryPointTests).FullName, nameof(CoyoteTaskTestWithRuntime)),
                (typeof(EntryPointTests).FullName, nameof(CoyoteAsyncTaskTestWithRuntime)),
                (typeof(EntryPointTests).FullName, nameof(CoyoteTaskTestWithActorRuntime)),
                (typeof(EntryPointTests).FullName, nameof(CoyoteAsyncTaskTestWithActorRuntime)),
                (typeof(Foo).FullName, nameof(Foo.VoidTest)),
                (typeof(Bar).FullName, nameof(Bar.VoidTest))
            };

            string result = $"Possible methods are:{Environment.NewLine}";
            foreach (var testName in testNames)
            {
                if (ambiguousName is null || testName.name.Equals(ambiguousName) ||
                    $"{testName.qualifier}.{testName.name}".Equals(ambiguousName))
                {
                    result += $"  {testName.qualifier}.{testName.name}{Environment.NewLine}";
                }
            }

            return result;
        }
    }
}
