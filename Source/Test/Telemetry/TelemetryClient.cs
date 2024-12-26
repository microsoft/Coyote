// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.Logging;

namespace Microsoft.Coyote.Telemetry
{
    /// <summary>
    /// Thread-safe client for sending telemetry messages to Azure.
    /// </summary>
    /// <remarks>
    /// See <see href="https://github.com/microsoft/ApplicationInsights-dotnet"/>.
    /// </remarks>
    internal class TelemetryClient
    {
        private const string ApiSecret = "eOkUmW73T9Wv5TUynCLAEA";
        private const string MeasurementId = "G-JS8YSYVDQX";

        /// <summary>
        /// Path to the Coyote home directory where the UUID is stored.
        /// </summary>
        private static string CoyoteHomePath => IsWindowsLike ?
            Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), "Microsoft", "coyote") :
            Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".microsoft", "coyote");

        /// <summary>
        /// File name where the UUID is stored.
        /// </summary>
        private const string IdFileName = "device_id.txt";

        /// <summary>
        /// Used to synchronize access to the telemetry client.
        /// </summary>
        private static readonly object SyncObject = new object();

        /// <summary>
        /// The current instance of the telemetry client.
        /// </summary>
        private static TelemetryClient Current;

        /// <summary>
        /// Responsible for writing to the installed <see cref="ILogger"/>.
        /// </summary>
        private readonly LogWriter LogWriter;

        /// <summary>
        /// True if telemetry is enabled, else false.
        /// </summary>
        private readonly bool IsEnabled;

        /// <summary>
        /// A unique id for this user on this device.
        /// </summary>
        private string DeviceId;

        /// <summary>
        /// The coyote version.
        /// </summary>
        private readonly string Version;

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetryClient"/> class.
        /// </summary>
        private TelemetryClient(LogWriter logWriter, bool isEnabled)
        {
            this.IsEnabled = isEnabled;
            if (isEnabled)
            {
                this.Version = typeof(Runtime.CoyoteRuntime).Assembly.GetName().Version.ToString();
                this.LogWriter = logWriter;
            }
        }

        /// <summary>
        /// Returns the existing telemetry client if one has already been created for this process,
        /// or creates and returns a new one with the specified configuration.
        /// </summary>
        internal static TelemetryClient GetOrCreate(Configuration configuration, LogWriter logWriter)
        {
            lock (SyncObject)
            {
                Current ??= new TelemetryClient(logWriter, configuration.IsTelemetryEnabled);
                return Current;
            }
        }

        /// <summary>
        /// Tracks the specified telemetry event.
        /// </summary>
        internal Task TrackEvent(string action, string result, int? bugsFound, double? testTime)
        {
            if (this.IsEnabled)
            {
                Task task = Task.CompletedTask;

                if (string.IsNullOrEmpty(this.DeviceId))
                {
                    this.DeviceId = GetOrCreateDeviceId(out bool isFirstTime);
                    if (isFirstTime)
                    {
                        task = this.TrackEvent("welcome", null, null, null);
                    }
                }

                try
                {
                    var analytics = new Analytics()
                    {
                        ApiSecret = ApiSecret,
                        MeasurementId = MeasurementId,
                        ClientId = this.DeviceId
                    };

                    var m = new TestEventMeasurement()
                    {
                        Action = action,
                        Result = result
                    };

                    m.Coyote = this.Version;

                    if (bugsFound.HasValue)
                    {
                        m.Bugs = bugsFound.Value;
                    }

                    if (testTime.HasValue)
                    {
                        m.TestTime = testTime.Value;
                    }

                    analytics.Events.Add(m);

                    this.LogWriter.LogDebug("[coyote::telemetry] Tracking event: {0}.", action);

                    // handy for debugging errors from google.
                    // var response = HttpProtocol.ValidateMeasurements(analytics).Result;

                    return Task.WhenAll(task, HttpProtocol.PostMeasurements(analytics));
                }
                catch (Exception ex)
                {
                    this.LogWriter.LogDebug("[coyote::telemetry] Unable to send event: {0}", ex.Message);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns the unique device id or creates a new one.
        /// </summary>
        private static string GetOrCreateDeviceId(out bool isFirstTime)
        {
            string deviceId = Guid.NewGuid().ToString();
            isFirstTime = true;

            int attempts = 5;
            while (attempts-- > 0)
            {
                try
                {
                    string fullpath = Path.Combine(CoyoteHomePath, IdFileName);
                    if (!File.Exists(fullpath))
                    {
                        Directory.CreateDirectory(CoyoteHomePath);
                        using StreamWriter writer = new StreamWriter(fullpath);
                        writer.Write(deviceId);
                    }
                    else
                    {
                        deviceId = File.ReadAllText(fullpath);
                        isFirstTime = false;
                    }

                    break;
                }
                catch
                {
                    Thread.Sleep(1);
                }
            }

            return deviceId;
        }

        /// <summary>
        /// Returns true if this is a Windows platform, else false.
        /// </summary>
        private static bool IsWindowsLike => Environment.OSVersion.Platform == PlatformID.Win32NT ||
            Environment.OSVersion.Platform == PlatformID.Win32S ||
            Environment.OSVersion.Platform == PlatformID.Win32Windows ||
            Environment.OSVersion.Platform == PlatformID.WinCE ||
            Environment.OSVersion.Platform == PlatformID.Xbox;
    }
}
