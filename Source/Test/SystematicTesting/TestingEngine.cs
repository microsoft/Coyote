// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Actors.Coverage;
using Microsoft.Coyote.Coverage;
using Microsoft.Coyote.Logging;
using Microsoft.Coyote.Rewriting;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Telemetry;
using Microsoft.Coyote.Visualization;

namespace Microsoft.Coyote.SystematicTesting
{
    /// <summary>
    /// Testing engine that can run a controlled concurrency test using
    /// a specified configuration.
    /// </summary>
#if !DEBUG
    [DebuggerStepThrough]
#endif
    public sealed class TestingEngine : IDisposable
    {
        /// <summary>
        /// The client used to optionally send anonymized telemetry data.
        /// </summary>
        private static TelemetryClient TelemetryClient;

        /// <summary>
        /// The test configuration.
        /// </summary>
        private readonly Configuration Configuration;

        /// <summary>
        /// The method to test.
        /// </summary>
        private readonly TestMethodInfo TestMethodInfo;

        /// <summary>
        /// Set of callbacks to invoke at the start of each iteration.
        /// </summary>
        private readonly ISet<Action<uint>> StartIterationCallbacks;

        /// <summary>
        /// Set of callbacks to invoke at the end of each iteration.
        /// </summary>
        private readonly ISet<Action<uint>> EndIterationCallbacks;

        /// <summary>
        /// The scheduler used by the runtime during testing.
        /// </summary>
        internal OperationScheduler Scheduler { get; private set; }

        /// <summary>
        /// The profiler.
        /// </summary>
        internal Profiler Profiler { get; private set; }

        /// <summary>
        /// The testing task cancellation token source.
        /// </summary>
        private readonly CancellationTokenSource CancellationTokenSource;

        /// <summary>
        /// Data structure containing information
        /// gathered during testing.
        /// </summary>
        public TestReport TestReport { get; set; }

        /// <summary>
        /// Responsible for writing to the installed <see cref="ILogger"/>.
        /// </summary>
        private readonly LogWriter LogWriter;

        /// <summary>
        /// The DGML coverage graph of the execution path explored in the last iteration.
        /// </summary>
        private CoverageGraph LastCoverageGraph;

        /// <summary>
        /// Contains a single iteration of XML log output in the case where the IsXmlLogEnabled
        /// configuration is specified.
        /// </summary>
        private StringBuilder XmlLog;

        /// <summary>
        /// The readable trace, if any.
        /// </summary>
        public string ReadableTrace { get; private set; }

        /// <summary>
        /// The reproducible trace, if any.
        /// </summary>
        public string ReproducibleTrace { get; private set; }

        /// <summary>
        /// A guard for printing info.
        /// </summary>
        private int PrintGuard;

        /// <summary>
        /// Serializes access to the engine.
        /// </summary>
        private readonly object EngineLock;

        /// <summary>
        /// Creates a new systematic testing engine.
        /// </summary>
        public static TestingEngine Create(Configuration configuration, Action test) =>
            new TestingEngine(configuration, test, new LogWriter(configuration, true));

        /// <summary>
        /// Creates a new systematic testing engine.
        /// </summary>
        public static TestingEngine Create(Configuration configuration, Action<ICoyoteRuntime> test) =>
            new TestingEngine(configuration, test, new LogWriter(configuration, true));

        /// <summary>
        /// Creates a new systematic testing engine.
        /// </summary>
        public static TestingEngine Create(Configuration configuration, Action<IActorRuntime> test) =>
            new TestingEngine(configuration, test, new LogWriter(configuration, true));

        /// <summary>
        /// Creates a new systematic testing engine.
        /// </summary>
        public static TestingEngine Create(Configuration configuration, Func<Task> test) =>
            new TestingEngine(configuration, test, new LogWriter(configuration, true));

        /// <summary>
        /// Creates a new systematic testing engine.
        /// </summary>
        public static TestingEngine Create(Configuration configuration, Func<ICoyoteRuntime, Task> test) =>
            new TestingEngine(configuration, test, new LogWriter(configuration, true));

        /// <summary>
        /// Creates a new systematic testing engine.
        /// </summary>
        public static TestingEngine Create(Configuration configuration, Func<IActorRuntime, Task> test) =>
            new TestingEngine(configuration, test, new LogWriter(configuration, true));

        /// <summary>
        /// Initializes a new instance of the <see cref="TestingEngine"/> class.
        /// </summary>
        internal TestingEngine(Configuration configuration, LogWriter logWriter)
            : this(configuration, TestMethodInfo.Create(configuration, logWriter), TraceReport.FromJson(configuration), logWriter)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestingEngine"/> class.
        /// </summary>
        internal TestingEngine(Configuration configuration, Delegate test, LogWriter logWriter)
            : this(configuration, TestMethodInfo.Create(test, logWriter), TraceReport.FromJson(configuration), logWriter)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestingEngine"/> class.
        /// </summary>
        /// <remarks>
        /// If a non-empty prefix trace is provided, then the testing engine will attempt
        /// to replay it before performing any new exploration.
        /// </remarks>
        private TestingEngine(Configuration configuration, TestMethodInfo testMethodInfo, ExecutionTrace prefixTrace, LogWriter logWriter)
        {
            this.Configuration = configuration;
            this.TestMethodInfo = testMethodInfo;
            this.LogWriter = logWriter;
            this.Profiler = new Profiler();
            this.StartIterationCallbacks = new HashSet<Action<uint>>();
            this.EndIterationCallbacks = new HashSet<Action<uint>>();
            this.TestReport = new TestReport(configuration);
            this.ReadableTrace = string.Empty;
            this.ReproducibleTrace = string.Empty;
            this.CancellationTokenSource = new CancellationTokenSource();
            this.PrintGuard = 1;
            this.EngineLock = new object();

            AppDomain.CurrentDomain.UnhandledException += this.OnUnhandledException;

            // Do some sanity checking.
            if (configuration.IsSystematicFuzzingEnabled && prefixTrace.Length > 0)
            {
                throw new InvalidOperationException("Replaying an execution trace is not supported in systematic fuzzing.");
            }

            // Parse the trace if one is provided, and update any configuration values.
            this.Scheduler = OperationScheduler.Setup(configuration, prefixTrace);

            // Create a client for gathering and sending optional telemetry data.
            TelemetryClient = TelemetryClient.GetOrCreate(this.Configuration, this.LogWriter);
        }

        /// <summary>
        /// Runs the testing engine.
        /// </summary>
        public void Run()
        {
            try
            {
                if (this.Configuration.IsTelemetryEnabled)
                {
                    this.LogWriter.LogImportant("..... Anonymized telemetry is enabled, see {0}.", Documentation.LearnAboutTelemetryUrl);
                }

                if (!this.IsTestRewritten())
                {
                    // TODO: eventually will throw an exception; we allow this for now.
                    this.LogWriter.LogError("... Assembly is not rewritten for testing, see {0}.", Documentation.LearnAboutRewritingUrl);
                }

                Task task = this.CreateTestingTask(this.TestMethodInfo);
                if (this.Configuration.TestingTimeout > 0)
                {
                    this.CancellationTokenSource.CancelAfter(
                        this.Configuration.TestingTimeout * 1000);
                }

                this.Profiler.StartMeasuringExecutionTime();
                if (!this.CancellationTokenSource.IsCancellationRequested)
                {
                    task.Start();
                    task.Wait(this.CancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                if (this.CancellationTokenSource.IsCancellationRequested)
                {
                    this.LogWriter.LogWarning("... Test timed out.");
                }
            }
            catch (Exception ex)
            {
                if (ex is AggregateException aex)
                {
                    ex = aex.Flatten().InnerException;
                }

                if (ex is FileNotFoundException)
                {
                    this.LogWriter.LogError(ex.Message);
                }
                else
                {
                    this.LogWriter.LogError("... Test failed due to an internal '{0}' exception.", ex.GetType().FullName);
                    this.TestReport.InternalErrors.Add(ex.ToString());
                }

                ExceptionDispatchInfo.Capture(ex).Throw();
            }
            finally
            {
                this.Profiler.StopMeasuringExecutionTime();
            }

            if (this.Configuration.IsTelemetryEnabled)
            {
                // 10 seconds should be enough, any more than that and too bad.
                this.TrackTelemetry().Wait(10000, this.CancellationTokenSource.Token);
            }
        }

        /// <summary>
        /// Creates a new testing task for the specified test method.
        /// </summary>
        private Task CreateTestingTask(TestMethodInfo methodInfo)
        {
            return new Task(() =>
            {
                this.LogWriter.LogImportant("... Setting up the{0} test:", string.IsNullOrEmpty(methodInfo.Name) ? string.Empty : $" '{methodInfo.Name}'");
                this.LogWriter.LogImportant("..... Using the {0} exploration strategy.", this.Scheduler.GetDescription());
                if (this.Configuration.AttachDebugger)
                {
                    this.LogWriter.LogImportant("..... Launching and attaching the debugger.");
                    Debugger.Launch();
                    if (!Debugger.IsAttached)
                    {
                        this.LogWriter.LogError("..... Failed to launch or attach the debugger.");
                    }
                }

                try
                {
                    // Invokes the user-specified initialization method.
                    methodInfo.InitializeAllIterations();

                    this.LogWriter.LogImportant(this.Scheduler.IsReplaying ? "... Running test." : "... Running test iterations:");
                    uint iteration = 0;
                    while (iteration < this.Configuration.TestingIterations || this.Configuration.TestingTimeout > 0)
                    {
                        if (this.CancellationTokenSource.IsCancellationRequested)
                        {
                            break;
                        }

                        // Runs the next iteration.
                        bool runNext = this.RunNextIteration(methodInfo, iteration);
                        if ((!this.Configuration.RunTestIterationsToCompletion && this.TestReport.NumOfFoundBugs > 0) ||
                            this.Scheduler.IsReplaying || !runNext)
                        {
                            break;
                        }

                        // Increments the seed in the random number generator, to capture the seed used
                        // by the exploration strategy in the next iteration.
                        this.Scheduler.ValueGenerator.Seed++;
                        iteration++;
                    }

                    // Invokes the user-specified test disposal method.
                    methodInfo.DisposeAllIterations();
                }
                catch (Exception ex)
                {
                    Exception innerException = ex;
                    while (innerException is TargetInvocationException)
                    {
                        innerException = innerException.InnerException;
                    }

                    if (innerException is AggregateException)
                    {
                        innerException = innerException.InnerException;
                    }

                    if (!(innerException is TaskCanceledException))
                    {
                        ExceptionDispatchInfo.Capture(innerException).Throw();
                    }
                }
            }, this.CancellationTokenSource.Token);
        }

        /// <summary>
        /// Runs the next testing iteration for the specified test method.
        /// </summary>
        private bool RunNextIteration(TestMethodInfo methodInfo, uint iteration)
        {
            // Log writer used to observe all test iteration output and write it in memory.
            using MemoryLogWriter iterationLogWriter = new MemoryLogWriter(this.Configuration);
            if (!this.LogWriter.IsRuntimeLogger())
            {
                // Override the default logger associated with this test iteration.
                iterationLogWriter.SetLogger(this.LogWriter.Logger);
            }

            if (!this.Scheduler.InitializeNextIteration(iteration, iterationLogWriter))
            {
                // The next iteration cannot run, so stop exploring.
                return false;
            }

            if (!this.Scheduler.IsReplaying && this.ShouldPrintIteration(iteration + 1))
            {
                this.LogWriter.LogImportant("..... Iteration #{0}", iteration + 1);
            }

            // Runtime used to serialize and test the program in this iteration.
            CoyoteRuntime runtime = null;

            try
            {
                // Invoke any registered callbacks at the start of this iteration.
                this.InvokeStartIterationCallBacks(iteration);

                // TODO: optimize so that the actor runtime extension is only added if the test supports actors.
                // Creates a new instance of the controlled runtime and adds the actor runtime extension.
                var logManager = RuntimeFactory.CreateLogManager(iterationLogWriter);
                var actorRuntimeExtension = Actors.RuntimeFactory.Create(this.Configuration, logManager, this.Scheduler.SchedulingPolicy);
                runtime = CoyoteRuntime.Create(this.Configuration, this.Scheduler, iterationLogWriter, logManager, actorRuntimeExtension);
                actorRuntimeExtension.WithRuntime(runtime);

                // Set the test method iteration logger, if one can be set.
                methodInfo.SetTestLogWriter(iterationLogWriter);

                this.InitializeCustomActorLogging(actorRuntimeExtension);

                // Run the test and waits for it to terminate.
                Task task = runtime.RunTestAsync(methodInfo.Method, methodInfo.Name);
                task.Wait();

                // Turn off runtime logging for the current iteration.
                iterationLogWriter.Close();

                // Invoke the user-specified iteration disposal method.
                methodInfo.DisposeCurrentIteration();

                // Invoke any registered callbacks at the end of this iteration.
                this.InvokeEndIterationCallBacks(iteration);

                runtime.LogManager.LogCompletion();

                this.GatherTestingStatistics(runtime);

                if (!this.Scheduler.IsReplaying && this.TestReport.NumOfFoundBugs > 0)
                {
                    this.ReadableTrace = string.Empty;
                    this.ReadableTrace += iterationLogWriter.GetObservedMessages();
                    this.ReadableTrace += this.TestReport.GetText(this.Configuration, "[coyote::report]");

                    if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
                    {
                        this.ReproducibleTrace = TraceReport.GetJson(this.Scheduler, this.Configuration);
                    }
                }

                if (this.Configuration.IsSystematicFuzzingFallbackEnabled &&
                    runtime.SchedulingPolicy is SchedulingPolicy.Interleaving &&
                    (runtime.ExecutionStatus is ExecutionStatus.ConcurrencyUncontrolled ||
                    runtime.ExecutionStatus is ExecutionStatus.Deadlocked))
                {
                    // Detected uncontrolled concurrency or deadlock, so switch to systematic fuzzing.
                    this.Scheduler = OperationScheduler.Setup(this.Configuration, SchedulingPolicy.Fuzzing, this.Scheduler.ValueGenerator);
                    this.LogWriter.LogImportant("..... Iteration #{0} enables systematic fuzzing due to uncontrolled concurrency",
                        iteration + 1);
                }
                else if (runtime.ExecutionStatus is ExecutionStatus.BugFound)
                {
                    if (!this.Scheduler.IsReplaying)
                    {
                        this.LogWriter.LogImportant("..... Iteration #{0} found bug #{1}", iteration + 1, this.TestReport.NumOfFoundBugs);
                    }

                    this.LogWriter.LogError(runtime.BugReport);
                }
                else if (this.Scheduler.IsReplaying)
                {
                    this.LogWriter.LogError("Failed to reproduce the bug.");
                }
            }
            finally
            {
                // Clean up runtime resources before the next iteration starts.
                runtime?.Dispose();
            }

            return true;
        }

        /// <summary>
        /// Stops the testing engine.
        /// </summary>
        public void Stop()
        {
            this.CancellationTokenSource.Cancel();
        }

        /// <summary>
        /// Returns a report with the testing results.
        /// </summary>
        public string GetReport()
        {
            if (this.Scheduler.IsReplaying)
            {
                StringBuilder report = new StringBuilder();
                report.AppendFormat("... Reproduced {0} bug{1}{2}.", this.TestReport.NumOfFoundBugs,
                    this.TestReport.NumOfFoundBugs is 1 ? string.Empty : "s",
                    Debugger.IsAttached ? string.Empty : " (use --break to attach the debugger)");
                report.AppendLine();
                return report.ToString();
            }

            return this.TestReport.GetText(this.Configuration, "...");
        }

        /// <summary>
        /// Throws either an <see cref="AssertionFailureException"/>, if a bug was found,
        /// or an unhandled <see cref="Exception"/>, if one was thrown.
        /// </summary>
        public void ThrowIfBugFound()
        {
            if (this.TestReport.NumOfFoundBugs > 0)
            {
                if (this.TestReport.ThrownException != null)
                {
                    ExceptionDispatchInfo.Capture(this.TestReport.ThrownException).Throw();
                }

                throw new AssertionFailureException(this.TestReport.BugReports.FirstOrDefault());
            }
        }

        /// <summary>
        /// Tries to emit the available reports to the specified directory with the given file name,
        /// and returns the paths of all emitted reports.
        /// </summary>
        public bool TryEmitReports(string directory, string fileName, out IEnumerable<string> reportPaths)
        {
            var paths = new List<string>();
            if (!this.Configuration.RunTestIterationsToCompletion)
            {
                // Emit the human readable trace, if it exists.
                if (!string.IsNullOrEmpty(this.ReadableTrace))
                {
                    string readableTracePath = Path.Combine(directory, fileName + ".txt");
                    File.WriteAllText(readableTracePath, this.ReadableTrace);
                    paths.Add(readableTracePath);
                }
            }

            if (!this.Configuration.RunTestIterationsToCompletion)
            {
                if (this.Configuration.IsXmlLogEnabled)
                {
                    string xmlPath = Path.Combine(directory, fileName + ".trace.xml");
                    File.WriteAllText(xmlPath, this.XmlLog.ToString());
                    paths.Add(xmlPath);
                }

                // Emit the reproducible trace, if it exists.
                if (!string.IsNullOrEmpty(this.ReproducibleTrace))
                {
                    string reproTracePath = Path.Combine(directory, fileName + ".trace");
                    File.WriteAllText(reproTracePath, this.ReproducibleTrace);
                    paths.Add(reproTracePath);
                }

                // Emit the trace visualization, if it exists.
                if (this.Configuration.IsTraceAnalysisEnabled &&
                    this.Scheduler.Graph.Length > 0 && this.TestReport.NumOfFoundBugs > 0)
                {
                    string visualTracePath = Path.Combine(directory, fileName + ".trace.dgml");
                    File.WriteAllText(visualTracePath, TraceVisualizer.Visualize(this.Scheduler.Graph, TraceVisualizer.Layout.Trace));
                    paths.Add(visualTracePath);
                }

                if (this.Configuration.IsActorTraceVisualizationEnabled &&
                    this.LastCoverageGraph != null && this.TestReport.NumOfFoundBugs > 0)
                {
                    // TODO: combine with the new ExecutionGraphVisualizer capability.
                    string graphPath = Path.Combine(directory, fileName + ".actors.trace.dgml");
                    this.LastCoverageGraph.SaveDgml(graphPath, true);
                    paths.Add(graphPath);
                }
            }

            // Emit the uncontrolled invocations report, if it exists.
            if (this.TestReport.UncontrolledInvocations.Count > 0)
            {
                string reportPath = Path.Combine(directory, fileName + ".uncontrolled.json");
                File.WriteAllText(reportPath, UncontrolledInvocationsReport.ToJSON(this.TestReport.UncontrolledInvocations));
                paths.Add(reportPath);
            }

            reportPaths = paths;
            return paths.Count > 0;
        }

        /// <summary>
        /// Tries to emit the available coverage reports to the specified directory with the given file name,
        /// and returns the paths of all emitted coverage reports.
        /// </summary>
        public bool TryEmitCoverageReports(string directory, string fileName, out IEnumerable<string> reportPaths)
        {
            var paths = new List<string>();
            var coverageReporter = new ActorCoverageReporter(this.TestReport.CoverageInfo);

            // Emit the coverage visualization, if it exists.
            if (this.Configuration.IsActivityCoverageReported)
            {
                string graphFilePath = Path.Combine(directory, fileName + ".coverage.dgml");
                if (coverageReporter.TryEmitVisualizationGraph(graphFilePath))
                {
                    paths.Add(graphFilePath);
                }

                string coverageFilePath = Path.Combine(directory, fileName + ".coverage.txt");
                if (coverageReporter.TryEmitActivityCoverageReport(coverageFilePath))
                {
                    paths.Add(coverageFilePath);
                }
            }
            else if (this.Configuration.IsTraceAnalysisEnabled && this.Scheduler.Graph.Length > 0)
            {
                string coveragePath = Path.Combine(directory, fileName + ".coverage.dgml");
                File.WriteAllText(coveragePath, TraceVisualizer.Visualize(this.Scheduler.Graph, TraceVisualizer.Layout.Coverage));
                paths.Add(coveragePath);
            }

            if (this.Configuration.IsScheduleCoverageReported)
            {
                string scheduleCoverageFilePath = Path.Combine(directory, fileName + ".coverage.schedule.txt");
                coverageReporter.TryEmitScheduleCoverageReport(scheduleCoverageFilePath);
                paths.Add(scheduleCoverageFilePath);
            }

            if (this.Configuration.IsCoverageInfoSerialized)
            {
                string serFilePath = Path.Combine(directory, fileName + ".coverage.ser");
                this.TestReport.CoverageInfo.Save(serFilePath);
                paths.Add(serFilePath);
            }

            reportPaths = paths;
            return paths.Count > 0;
        }

        /// <summary>
        /// Gathers the exploration strategy statistics from the specified runtime.
        /// </summary>
        private void GatherTestingStatistics(CoyoteRuntime runtime)
        {
            TestReport report = new TestReport(this.Configuration);
            runtime.PopulateTestReport(report);

            var coverageInfo = runtime.BuildCoverageInfo();
            report.CoverageInfo.Merge(coverageInfo);
            this.TestReport.Merge(report);

            // Save the DGML coverage graph of the execution path explored in the last iteration.
            this.LastCoverageGraph = runtime.GetCoverageGraph();
        }

        /// <summary>
        /// Tracks anonymized telemetry data.
        /// </summary>
        private async Task TrackTelemetry()
        {
            bool isReplaying = this.Scheduler.IsReplaying;
            await TelemetryClient.TrackEvent(
                action: Debugger.IsAttached ?
                    (isReplaying ? "debug-replay" : "debug-test") :
                    (isReplaying ? "replay" : "test"),
                result: (this.TestReport.NumOfFoundBugs > 0) ? "failed" : "passed",
                bugsFound: this.TestReport?.NumOfFoundBugs,
                testTime: this.Profiler?.Results());
        }

        /// <summary>
        /// Registers a callback to invoke at the start of each iteration. The callback takes as
        /// a parameter an integer representing the current iteration.
        /// </summary>
        public void RegisterStartIterationCallBack(Action<uint> callback) =>
            this.StartIterationCallbacks.Add(callback);

        /// <summary>
        /// Registers a callback to invoke at the end of each iteration. The callback takes as
        /// a parameter an integer representing the current iteration.
        /// </summary>
        public void RegisterEndIterationCallBack(Action<uint> callback) =>
            this.EndIterationCallbacks.Add(callback);

        /// <summary>
        /// Invokes any registered callbacks at the start of the specified iteration.
        /// </summary>
        public void InvokeStartIterationCallBacks(uint iteration)
        {
            foreach (var callback in this.StartIterationCallbacks)
            {
                callback(iteration);
            }
        }

        /// <summary>
        /// Invokes any registered callbacks at the end of the specified iteration.
        /// </summary>
        public void InvokeEndIterationCallBacks(uint iteration)
        {
            foreach (var callback in this.EndIterationCallbacks)
            {
                callback(iteration);
            }
        }

        /// <summary>
        /// Initializes any custom actor logs.
        /// </summary>
        private void InitializeCustomActorLogging(IActorRuntime runtime)
        {
            if (this.Configuration.IsActorTraceVisualizationEnabled)
            {
                // Registers an activity coverage graph builder.
                runtime.RegisterLog(new ActorRuntimeLogGraphBuilder(false, false));
            }

            if (this.Configuration.IsActivityCoverageReported)
            {
                // Registers an activity coverage graph builder that collapses instances.
                runtime.RegisterLog(new ActorRuntimeLogGraphBuilder(false, true));

                // Need this additional logger to get the event coverage report correct
                runtime.RegisterLog(new ActorRuntimeLogEventCoverage());
            }

            if (this.Configuration.IsXmlLogEnabled)
            {
                this.XmlLog = new StringBuilder();
                runtime.RegisterLog(new ActorRuntimeLogXmlFormatter(XmlWriter.Create(this.XmlLog,
                    new XmlWriterSettings() { Indent = true, IndentChars = "  ", OmitXmlDeclaration = true })));
            }
        }

        /// <summary>
        /// Returns true if the engine should print the current iteration.
        /// </summary>
        private bool ShouldPrintIteration(uint iteration)
        {
            if (iteration > this.PrintGuard * 10)
            {
                var count = iteration.ToString().Length - 1;
                var guard = "1" + (count > 0 ? string.Concat(Enumerable.Repeat("0", count)) : string.Empty);
                this.PrintGuard = int.Parse(guard);
            }

            return iteration % this.PrintGuard is 0;
        }

        /// <summary>
        /// Checks if the test executed by the testing engine has been rewritten with the current version.
        /// </summary>
        /// <returns>True if the test has been rewritten, else false.</returns>
        public bool IsTestRewritten() => RewritingEngine.IsAssemblyRewritten(this.TestMethodInfo.Assembly);

        /// <summary>
        /// Installs the specified <see cref="ILogger"/> to log messages during testing.
        /// </summary>
        public void SetLogger(ILogger logger) => this.LogWriter.SetLogger(logger);

        /// <summary>
        /// Callback invoked when an unhandled exception occurs during testing.
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            lock (this.EngineLock)
            {
                Exception exception = args.ExceptionObject as Exception;
                if (exception is AggregateException aggregateException)
                {
                    exception = aggregateException.Flatten().InnerException;
                }

                this.LogWriter.LogError("[coyote::error] Unhandled exception: {0}", exception);
            }
        }

        /// <summary>
        /// Releases any held resources.
        /// </summary>
        public void Dispose()
        {
            lock (this.EngineLock)
            {
                AppDomain.CurrentDomain.UnhandledException -= this.OnUnhandledException;
                this.TestMethodInfo.Dispose();
                this.LogWriter.Dispose();
            }
        }
    }
}
