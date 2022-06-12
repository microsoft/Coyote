// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ImageGallery
{
    public class TaskYieldInjector
    {
        private readonly int Iyo = 10;

        public void PrintHello(string str)
        {
            Console.WriteLine("Hello, World! str: " + str + this.Iyo);
        }

//         public static async Task InjectYieldsAtMethodStart()
//         {
//             string envYiledLoop = Environment.GetEnvironmentVariable("YIELDS_METHOD_START");
//             int envYiledLoopInt = 0;
//             if (envYiledLoop != null)
//             {
// #pragma warning disable CA1305 // Specify IFormatProvider
//                 envYiledLoopInt = int.Parse(envYiledLoop);
// #pragma warning restore CA1305 // Specify IFormatProvider
//             }

//             for (int i = 0; i < envYiledLoopInt; i++)
//             {
//                 await Task.Yield();
//             }
//         }

//         public static async Task InjectYieldsAtMethodMiddle()
//         {
//             string envYiledLoop = Environment.GetEnvironmentVariable("YIELDS_LOOP");
//             int envYiledLoopInt = 100;
//             if (envYiledLoop != null)
//             {
// #pragma warning disable CA1305 // Specify IFormatProvider
//                 envYiledLoopInt = int.Parse(envYiledLoop);
// #pragma warning restore CA1305 // Specify IFormatProvider
//             }

//             for (int i = 0; i < envYiledLoopInt; i++)
//             {
//                 await Task.Delay(0); // creat more of both continuations and spawns!
//                 await Task.Yield();
//             }
//         }

//         public static async Task InjectYieldsAtMethodEnd()
//         {
//             string envYiledLoop = Environment.GetEnvironmentVariable("YIELDS_METHOD_END");
//             int envYiledLoopInt = 0;
//             if (envYiledLoop != null)
//             {
// #pragma warning disable CA1305 // Specify IFormatProvider
//                 envYiledLoopInt = int.Parse(envYiledLoop);
// #pragma warning restore CA1305 // Specify IFormatProvider
//             }

//             for (int i = 0; i < envYiledLoopInt; i++)
//             {
//                 await Task.Yield();
//             }
//         }
    }
}