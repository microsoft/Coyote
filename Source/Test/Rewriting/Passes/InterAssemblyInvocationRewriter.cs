﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using Microsoft.Coyote.IO;
using Microsoft.Coyote.Runtime;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ControlledTasks = Microsoft.Coyote.Interception;

namespace Microsoft.Coyote.Rewriting
{
    /// <summary>
    /// Rewriting pass for invocations between assemblies.
    /// </summary>
    internal class InterAssemblyInvocationRewriter : AssemblyRewriter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InterAssemblyInvocationRewriter"/> class.
        /// </summary>
        internal InterAssemblyInvocationRewriter(ILogger log)
            : base(log)
        {
        }

        /// <inheritdoc/>
        internal override void VisitType(TypeDefinition type)
        {
            this.Method = null;
            this.Processor = null;
        }

        /// <inheritdoc/>
        internal override void VisitMethod(MethodDefinition method)
        {
            this.Method = null;

            // Only non-abstract method bodies can be rewritten.
            if (method.IsAbstract)
            {
                return;
            }

            this.Method = method;
            this.Processor = method.Body.GetILProcessor();

            // Rewrite the method body instructions.
            this.VisitInstructions(method);
        }

        /// <inheritdoc/>
        protected override Instruction VisitInstruction(Instruction instruction)
        {
            if (this.Method is null)
            {
                return instruction;
            }

            try
            {
                if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                    instruction.Operand is MethodReference methodReference &&
                    this.IsForeignType(methodReference.DeclaringType.Resolve()))
                {
                    if (IsTaskType(methodReference.ReturnType.Resolve()))
                    {
                        string methodName = GetFullyQualifiedMethodName(methodReference);
                        Debug.WriteLine($"............. [+] injected returned uncontrolled task assertion for method '{methodName}'");

                        var providerType = this.Module.ImportReference(typeof(ExceptionProvider)).Resolve();
                        MethodReference providerMethod = providerType.Methods.FirstOrDefault(
                            m => m.Name is nameof(ExceptionProvider.FailOnUncontrolledReturnedTask));
                        providerMethod = this.Module.ImportReference(providerMethod);

                        this.Processor.InsertAfter(instruction, Instruction.Create(OpCodes.Call, providerMethod));
                        this.Processor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, methodName));
                        this.Processor.InsertAfter(instruction, Instruction.Create(OpCodes.Dup));

                        this.ModifiedMethodBody = true;
                    }
                    else if (methodReference.Name is "GetAwaiter" &&
                        IsTaskAwaiterType(methodReference.ReturnType.Resolve()))
                    {
                        var declaringType = methodReference.DeclaringType;
                        TypeDefinition providerType = this.Module.ImportReference(typeof(ControlledTasks.TaskAwaiter)).Resolve();
                        MethodReference wrapMethod = null;
                        if (declaringType is GenericInstanceType gt)
                        {
                            MethodDefinition genericMethod = providerType.Methods.FirstOrDefault(m => m.Name == "Wrap" && m.HasGenericParameters);
                            MethodReference wrapReference = this.Module.ImportReference(genericMethod);

                            TypeReference argType = gt.GenericArguments.FirstOrDefault().GetElementType();
                            wrapMethod = MakeGenericMethod(wrapReference, argType);
                        }
                        else
                        {
                            wrapMethod = providerType.Methods.FirstOrDefault(
                               m => m.Name is nameof(ControlledTasks.TaskAwaiter.Wrap));
                        }

                        wrapMethod = this.Module.ImportReference(wrapMethod);

                        Instruction newInstruction = Instruction.Create(OpCodes.Call, wrapMethod);
                        Debug.WriteLine($"............. [+] {newInstruction}");

                        this.Processor.InsertAfter(instruction, newInstruction);

                        this.ModifiedMethodBody = true;
                    }
                }
            }
            catch (AssemblyResolutionException)
            {
                // Skip this instruction, we are only interested in types that can be resolved.
            }

            return instruction;
        }

        /// <summary>
        /// Checks if the specified type is foreign.
        /// </summary>
        private bool IsForeignType(TypeDefinition type)
        {
            if (type is null || this.Module == type.Module)
            {
                return false;
            }

            string module = Path.GetFileName(type.Module.FileName);
            if (module is "Microsoft.Coyote.dll" || module is "Microsoft.Coyote.Test.dll")
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the specified type is a task type.
        /// </summary>
        private static bool IsTaskType(TypeReference type)
        {
            if (type is null)
            {
                return false;
            }

            string module = Path.GetFileName(type.Module.FileName);
            if (!(module is "System.Private.CoreLib.dll" || module is "mscorlib.dll"))
            {
                return false;
            }

            if (type.Namespace == CachedNameProvider.SystemTasksNamespace &&
                (type.Name == CachedNameProvider.TaskName || type.Name.StartsWith("Task`")))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if the specified type is a task type.
        /// </summary>
        private static bool IsTaskAwaiterType(TypeReference type)
        {
            if (type is null)
            {
                return false;
            }

            string module = Path.GetFileName(type.Module.FileName);
            if (!(module is "System.Private.CoreLib.dll" || module is "mscorlib.dll"))
            {
                return false;
            }

            if (type.Namespace == CachedNameProvider.SystemCompilerNamespace &&
                (type.Name == CachedNameProvider.TaskAwaiterName || type.Name.StartsWith("TaskAwaiter`")))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if the specified type is a task-like type.
        /// </summary>
        private static bool IsTaskLikeType(TypeDefinition type)
        {
            if (type is null)
            {
                return false;
            }

            var interfaceTypes = type.Interfaces.Select(i => i.InterfaceType);
            if (!interfaceTypes.Any(
                i => i.FullName is "System.Runtime.CompilerServices.INotifyCompletion" ||
                i.FullName is "System.Runtime.CompilerServices.INotifyCompletion"))
            {
                return false;
            }

            if (type.Methods.Any(m => m.Name is "get_IsCompleted"))
            {
                return true;
            }

            return false;
        }
    }
}
