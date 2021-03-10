﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Coyote.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Microsoft.Coyote.Rewriting
{
    /// <summary>
    /// Rewrites Monitor.Enter, Monitor.Wait, Monitor.Pulse, Monitor.Exit to use the
    /// Coyote ControlledMonitor instead which allows systematic testing of code that
    /// uses monitors.
    /// </summary>
    internal class MonitorTransform : AssemblyTransform
    {
        /// <summary>
        /// The current module being transformed.
        /// </summary>
        private ModuleDefinition Module;

        /// <summary>
        /// The current method being transformed.
        /// </summary>
        private MethodDefinition Method;

        /// <summary>
        /// A helper class for editing method body.
        /// </summary>
        private ILProcessor Processor;

        /// <summary>
        /// The cached imported TypeDefinition for the Microsoft.Coyote.SystematicTesting.Interception.ControlledMonitor.
        /// </summary>
        private TypeDefinition ControlledMonitorType;

        private const string MonitorClassName = "System.Threading.Monitor";

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitorTransform"/> class.
        /// </summary>
        internal MonitorTransform(ILogger logger)
            : base(logger)
        {
        }

        /// <inheritdoc/>
        internal override void VisitModule(ModuleDefinition module)
        {
            this.Module = module;
            this.ControlledMonitorType = null;
        }

        /// <inheritdoc/>
        internal override void VisitMethod(MethodDefinition method)
        {
            this.Method = null;

            // Only non-abstract method bodies can be rewritten.
            if (!method.IsAbstract)
            {
                this.Method = method;
                this.Processor = method.Body.GetILProcessor();
                this.VisitInstructions(method);
            }
        }

        /// <inheritdoc/>
        protected override Instruction VisitInstruction(Instruction instruction)
        {
            if (this.Method is null)
            {
                return instruction;
            }

            if (instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference method &&
                method.DeclaringType.FullName == MonitorClassName)
            {
                var newMethod = this.GetControlledMonitorMethod(method);
                if (newMethod != null)
                {
                    // Since the method parameters match there's no need to modify the parameter setup code
                    // we can simply switch out the call.
                    Debug.WriteLine($"............. [-] call '{method}'");
                    var newInstruction = Instruction.Create(OpCodes.Call, newMethod);
                    Debug.WriteLine($"............. [+] call '{newMethod}'");
                    this.Processor.Replace(instruction, newInstruction);
                    instruction = newInstruction;
                }
            }

            return instruction;
        }

        private MethodReference GetControlledMonitorMethod(MethodReference monitorMethod)
        {
            if (this.ControlledMonitorType is null)
            {
                var tr = this.Module.ImportReference(typeof(Interception.ControlledMonitor));
                this.ControlledMonitorType = tr.Resolve();
                if (this.ControlledMonitorType is null)
                {
                    throw new InvalidOperationException("The rewriter is not finding the right version of Microsoft.Coyote.");
                }
            }

            // ensure we have full parameter names!
            var methodDef = monitorMethod.Resolve();

            foreach (var method in this.ControlledMonitorType.Methods)
            {
                if (method.Name == monitorMethod.Name && CheckMethodParametersMatch(method, methodDef))
                {
                    return this.Module.ImportReference(method);
                }
            }

            Debug.WriteLine($"............. [?] Monitor method not found '{monitorMethod}'");
            return null;
        }
    }
}
