﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET || NETCOREAPP3_1
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Coyote.Rewriting.Types.Net.Http;
using Microsoft.Coyote.Runtime;
using WebFramework = Microsoft.AspNetCore.Http;
using WebTesting = Microsoft.AspNetCore.Mvc.Testing;

using SystemDelegatingHandler = System.Net.Http.DelegatingHandler;
using SystemHttpClient = System.Net.Http.HttpClient;
using SystemTask = System.Threading.Tasks.Task;
using SystemTaskCreationOptions = System.Threading.Tasks.TaskCreationOptions;
using SystemThread = System.Threading.Thread;

namespace Microsoft.Coyote.Rewriting.Types.Web
{
    /// <summary>
    /// Provides methods for controlling a web application during testing.
    /// </summary>
    /// <remarks>This type is intended for compiler use rather than use directly in code.</remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class WebApplication
    {
        /// <summary>
        /// Controls the specified request.
        /// </summary>
        public static SystemTask ControlRequest(WebFramework.HttpContext context, WebFramework.RequestDelegate next)
        {
            WebFramework.HttpRequest request = context.Request;
            if (request.Headers.TryGetValue("ms-coyote-runtime-id", out var runtimeId))
            {
                request.Headers.Remove("ms-coyote-runtime-id");
                if (RuntimeProvider.TryGetFromId(System.Guid.Parse(runtimeId), out CoyoteRuntime runtime))
                {
                    IO.Debug.WriteLine("<CoyoteDebug> Invoking '{0} {1}' handler on runtime '{2}' from thread '{3}'.",
                        request.Method, request.Path, runtimeId, SystemThread.CurrentThread.ManagedThreadId);
                    return runtime.TaskFactory.StartNew(() =>
                    {
                        SystemTask task = next(context);
                        runtime.WaitUntilTaskCompletes(task);
                        task.GetAwaiter().GetResult();
                    },
                    default,
                    runtime.TaskFactory.CreationOptions | SystemTaskCreationOptions.DenyChildAttach,
                    runtime.TaskFactory.Scheduler);
                }
            }

            return next(context);
        }

        /// <summary>
        /// Controls the specified request.
        /// </summary>
        public static SystemTask ControlRequest(WebFramework.HttpContext context, Func<SystemTask> next)
        {
            WebFramework.HttpRequest request = context.Request;
            if (request.Headers.TryGetValue("ms-coyote-runtime-id", out var runtimeId))
            {
                request.Headers.Remove("ms-coyote-runtime-id");
                if (RuntimeProvider.TryGetFromId(System.Guid.Parse(runtimeId), out CoyoteRuntime runtime))
                {
                    IO.Debug.WriteLine("<CoyoteDebug> Invoking '{0} {1}' handler on runtime '{2}' from thread '{3}'.",
                        request.Method, request.Path, runtimeId, SystemThread.CurrentThread.ManagedThreadId);
                    return runtime.TaskFactory.StartNew(() =>
                    {
                        SystemTask task = next();
                        runtime.WaitUntilTaskCompletes(task);
                        task.GetAwaiter().GetResult();
                    },
                    default,
                    runtime.TaskFactory.CreationOptions | SystemTaskCreationOptions.DenyChildAttach,
                    runtime.TaskFactory.Scheduler);
                }
            }

            return next();
        }

        /// <summary>
        /// Creates an instance of an HTTP client that automatically follows redirects and handles cookies.
        /// </summary>
        public static SystemHttpClient CreateClient<TEntryPoint>(WebTesting.WebApplicationFactory<TEntryPoint> factory)
            where TEntryPoint : class => CreateClient(factory, factory.ClientOptions);

        /// <summary>
        /// Creates an instance of an HTTP client that automatically follows redirects and handles cookies.
        /// </summary>
        public static SystemHttpClient CreateClient<TEntryPoint>(WebTesting.WebApplicationFactory<TEntryPoint> factory,
            WebTesting.WebApplicationFactoryClientOptions options)
            where TEntryPoint : class =>
            CreateDefaultClient(factory, options.BaseAddress, CreateHandlers(options).ToArray());

        /// <summary>
        /// Creates a new instance of an HTTP client that can be used to send an HTTP request message to
        /// the server. The base address of the HTTP client instance will be set to http://localhost.
        /// </summary>
        public static SystemHttpClient CreateDefaultClient<TEntryPoint>(WebTesting.WebApplicationFactory<TEntryPoint> factory,
            params SystemDelegatingHandler[] handlers)
            where TEntryPoint : class
        {
            var delegatingHandlers = handlers.ToList();
            delegatingHandlers.Insert(0, HttpMessageHandler.Create());
            return factory.CreateDefaultClient(delegatingHandlers.ToArray());
        }

        /// <summary>
        /// Creates a new instance of an HTTP client that can be used to send an HTTP request message to the server.
        /// </summary>
        public static SystemHttpClient CreateDefaultClient<TEntryPoint>(
            WebTesting.WebApplicationFactory<TEntryPoint> factory, Uri baseAddress,
            params SystemDelegatingHandler[] handlers)
            where TEntryPoint : class
        {
            var client = CreateDefaultClient(factory, handlers);
            client.BaseAddress = baseAddress;
            return client;
        }

        private static IEnumerable<SystemDelegatingHandler> CreateHandlers(
            WebTesting.WebApplicationFactoryClientOptions options)
        {
            if (options.AllowAutoRedirect)
            {
                yield return new RedirectHandler(options.MaxAutomaticRedirections);
            }

            if (options.HandleCookies)
            {
                yield return new CookieContainerHandler();
            }
        }
    }
}
#endif
