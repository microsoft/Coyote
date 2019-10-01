﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

using Microsoft.Coyote.IO;
using Microsoft.Coyote.Utilities;

namespace Microsoft.Coyote
{
    /// <summary>
    /// The Coyote trace replayer.
    /// </summary>
    internal class Program
    {
        private static void Main(string[] args)
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(UnhandledExceptionHandler);

            // Parses the command line options to get the configuration.
            var configuration = new ReplayerCommandLineOptions().Parse(args);

            // Creates and starts a replaying process.
            ReplayingProcess.Create(configuration).Start();

            Console.WriteLine(". Done");
        }

        /// <summary>
        /// Handler for unhandled exceptions.
        /// </summary>
        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            var ex = (Exception)args.ExceptionObject;
            Error.Report("[CoyoteReplayer] internal failure: {0}: {1}", ex.GetType().ToString(), ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}
