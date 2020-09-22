﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Coyote.IO;
using Microsoft.Coyote.SystematicTesting.Interception;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Microsoft.Coyote.Rewriting
{
    /// <summary>
    /// Rewrites an assembly for systematic testing.
    /// </summary>
    /// <remarks>
    /// See <see href="/coyote/learn/tools/rewriting">Coyote rewriting tool</see> for more information.
    /// </remarks>
    public class AssemblyRewriter
    {
        /// <summary>
        /// Temporary directory that is used to write the rewritten assemblies
        /// in the case that they are replacing the original ones.
        /// </summary>
        /// <remarks>
        /// We need this because it seems Mono.Cecil does not allow to rewrite in-place.
        /// </remarks>
        private const string TempDirectory = "__temp_coyote__";

        /// <summary>
        /// Options for rewriting assemblies.
        /// </summary>
        private readonly RewritingOptions Options;

        /// <summary>
        /// List of assemblies that are not allowed to be rewritten.
        /// </summary>
        private readonly Regex DisallowedAssemblies;

        private readonly string[] DefaultDisallowedList = new string[]
        {
            @"Newtonsoft\.Json",
            @"Microsoft\.Coyote\.dll",
            @"Microsoft\.Coyote.Test\.dll",
            @"Microsoft\.VisualStudio\.TestPlatform.*",
            @"Microsoft\.TestPlatform.*",
            @"System\.Private\.CoreLib",
            @"mscorlib"
        };

        /// <summary>
        /// List of transforms we are applying while rewriting.
        /// </summary>
        private readonly List<AssemblyTransform> Transforms;

        /// <summary>
        /// Map from assembly name to full name definition of the rewritten assemblies.
        /// </summary>
        private readonly Dictionary<string, AssemblyNameDefinition> RewrittenAssemblies;

        /// <summary>
        /// The output log.
        /// </summary>
        private readonly ConsoleLogger Log;

        /// <summary>
        /// List of assemblies to be rewritten.
        /// </summary>
        private Queue<string> Pending = new Queue<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="AssemblyRewriter"/> class.
        /// </summary>
        /// <param name="options">The <see cref="RewritingOptions"/> for this rewriter.</param>
        private AssemblyRewriter(RewritingOptions options)
        {
            this.Log = new ConsoleLogger();
            this.Options = options;
            this.RewrittenAssemblies = new Dictionary<string, AssemblyNameDefinition>();
            var userList = options.DisallowedAssemblies ?? Array.Empty<string>();
            StringBuilder combined = new StringBuilder();
            foreach (var e in this.DefaultDisallowedList.Concat(userList))
            {
                if (combined.Length == 0)
                {
                    combined.Append("(");
                }
                else
                {
                    combined.Append("|");
                }

                combined.Append(e);
            }

            combined.Append(")");
            try
            {
                this.DisallowedAssemblies = new Regex(combined.ToString());
            }
            catch (Exception ex)
            {
                throw new Exception("DisallowedAssemblies not a valid regular expression\n" + ex.Message);
            }

            this.Transforms = new List<AssemblyTransform>()
            {
                 new TaskTransform(this.Log),
                 new MonitorTransform(this.Log),
                 new ExceptionFilterTransform(this.Log)
            };

            if (this.Options.IsRewritingUnitTests)
            {
                // We are running this pass last, as we are rewriting the original method, and
                // we need the other rewriting passes to happen before this pass.
                this.Transforms.Add(new MSTestTransform(this.Log));
            }

            // expand folder
            if (this.Options.AssemblyPaths == null || this.Options.AssemblyPaths.Count == 0)
            {
                // Expand to include all .dll files in AssemblyPaths.
                foreach (var file in Directory.GetFiles(this.Options.AssembliesDirectory, "*.dll"))
                {
                    if (this.IsDisallowed(Path.GetFileNameWithoutExtension(file)))
                    {
                        this.Options.AssemblyPaths.Add(file);
                    }
                    else
                    {
                        Debug.WriteLine("Skipping " + file);
                    }
                }
            }
        }

        private bool IsDisallowed(string assemblyName)
        {
            if (this.DisallowedAssemblies == null)
            {
                return false;
            }

            return this.DisallowedAssemblies.IsMatch(assemblyName);
        }

        /// <summary>
        /// Rewrites the assemblies specified in the rewriting options.
        /// </summary>
        public static void Rewrite(RewritingOptions options)
        {
            var binaryRewriter = new AssemblyRewriter(options);
            binaryRewriter.Rewrite();
        }

        /// <summary>
        /// Performs the assembly rewriting.
        /// </summary>
        private void Rewrite()
        {
            // Create the output directory and copy any necessary files.
            string outputDirectory = this.CreateOutputDirectoryAndCopyFiles();

            this.Pending = new Queue<string>();

            int errors = 0;
            // Rewrite the assembly files to the output directory.
            foreach (string assemblyPath in this.Options.AssemblyPaths)
            {
                this.Pending.Enqueue(assemblyPath);
            }

            while (this.Pending.Count > 0)
            {
                try
                {
                    var assemblyPath = this.Pending.Dequeue();
                    this.RewriteAssembly(assemblyPath, outputDirectory);
                }
                catch (Exception ex)
                {
                    if (ex is AggregateException ae && ae.InnerException != null)
                    {
                        this.Log.WriteErrorLine(ae.InnerException.Message);
                    }
                    else
                    {
                        this.Log.WriteErrorLine(ex.Message);
                    }

                    errors++;
                }
            }

            if (errors == 0 && this.Options.IsReplacingAssemblies)
            {
                // If we are replacing the original assemblies, then delete the temporary output directory.
                Directory.Delete(outputDirectory, true);
            }
        }

        /// <summary>
        /// Rewrites the specified assembly definition.
        /// </summary>
        private void RewriteAssembly(string assemblyPath, string outputDirectory)
        {
            string assemblyName = Path.GetFileName(assemblyPath);
            if (this.RewrittenAssemblies.ContainsKey(Path.GetFileNameWithoutExtension(assemblyPath)))
            {
                // already done!
                return;
            }

            string outputPath = Path.Combine(outputDirectory, assemblyName);
            var isSymbolFileAvailable = IsSymbolFileAvailable(assemblyPath);
            var readParams = new ReaderParameters()
            {
                AssemblyResolver = this.GetAssemblyResolver(),
                ReadSymbols = isSymbolFileAvailable
            };

            using (var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readParams))
            {
                if (this.IsDisallowed(assemblyName))
                {
                    throw new InvalidOperationException($"Rewriting the '{assemblyName}' assembly ({assembly.FullName}) is not allowed.");
                }

                this.Log.WriteLine($"... Rewriting the '{assemblyName}' assembly ({assembly.FullName})");
                if (this.Options.IsRewritingDependencies)
                {
                    // check for dependencies and if those are found in the same folder then rewrite them also, and fix up the
                    // version numbers so the rewritten assemblies are bound to these versions.
                    this.AddLocalDependencies(assemblyPath, assembly);
                }

                this.RewrittenAssemblies[assembly.Name.Name] = assembly.Name;

                if (IsAssemblyRewritten(assembly))
                {
                    // The assembly has been already rewritten by this version of Coyote, so skip it.
                    this.Log.WriteWarningLine($"..... Skipping assembly (reason: already rewritten by Coyote v{GetAssemblyRewritterVersion()})");
                    return;
                }

                this.FixAssemblyReferences(assembly);

                ApplyIsAssemblyRewrittenAttribute(assembly);
                foreach (var transform in this.Transforms)
                {
                    // Traverse the assembly to apply each transformation pass.
                    Debug.WriteLine($"..... Applying the '{transform.GetType().Name}' transform");
                    foreach (var module in assembly.Modules)
                    {
                        RewriteModule(module, transform);
                    }
                }

                // Write the binary in the output path with portable symbols enabled.
                this.Log.WriteLine($"... Writing the modified '{assemblyName}' assembly to " +
                    $"{(this.Options.IsReplacingAssemblies ? assemblyPath : outputPath)}");

                var writeParams = new WriterParameters()
                {
                    WriteSymbols = isSymbolFileAvailable,
                    SymbolWriterProvider = new PortablePdbWriterProvider()
                };

                if (!string.IsNullOrEmpty(this.Options.StrongNameKeyFile))
                {
                    using (FileStream fs = File.Open(this.Options.StrongNameKeyFile, FileMode.Open))
                    {
                        writeParams.StrongNameKeyPair = new StrongNameKeyPair(fs);
                    }
                }

                assembly.Write(outputPath, writeParams);
            }

            // dispose any resolved assemblies also!
            using var r = readParams.AssemblyResolver;

            if (this.Options.IsReplacingAssemblies)
            {
                string targetPath = Path.Combine(this.Options.AssembliesDirectory, assemblyName);
                this.CopyWithRetriesAsync(outputPath, assemblyPath).Wait();
                if (isSymbolFileAvailable)
                {
                    string pdbFile = Path.ChangeExtension(outputPath, "pdb");
                    string targetPdbFile = Path.ChangeExtension(targetPath, "pdb");
                    this.CopyWithRetriesAsync(pdbFile, targetPdbFile).Wait();
                }
            }
        }

        private async Task CopyWithRetriesAsync(string srcFile, string targetFile)
        {
            for (int retries = 10; retries >= 0; retries--)
            {
                try
                {
                    File.Copy(srcFile, targetFile, true);
                }
                catch (Exception)
                {
                    if (retries == 0)
                    {
                        throw;
                    }

                    await Task.Delay(100);
                    this.Log.WriteLine($"... Retrying write to {targetFile}");
                }
            }
        }

        private void FixAssemblyReferences(AssemblyDefinition assembly)
        {
            foreach (var module in assembly.Modules)
            {
                for (int i = 0, n = module.AssemblyReferences.Count; i < n; i++)
                {
                    var ar = module.AssemblyReferences[i];
                    var name = ar.Name;
                    if (this.RewrittenAssemblies.TryGetValue(name, out AssemblyNameDefinition rewrittenName))
                    {
                        // rewrite this reference to point to the newly rewritten assembly.
                        var refName = AssemblyNameReference.Parse(rewrittenName.FullName);
                        module.AssemblyReferences[i] = refName;
                    }
                }
            }
        }

        /// <summary>
        /// Enqueue any dependent assemblies that also exist in the assemblyPath and have not
        /// already been rewritten.
        /// </summary>
        private void AddLocalDependencies(string assemblyPath, AssemblyDefinition assembly)
        {
            var assemblyDir = Path.GetDirectoryName(assemblyPath);
            foreach (var module in assembly.Modules)
            {
                foreach (var ar in module.AssemblyReferences)
                {
                    var name = ar.Name;
                    var localName = Path.Combine(assemblyDir, name + ".dll");
                    if (!this.IsDisallowed(name) &&
                        File.Exists(localName) && !this.Pending.Contains(localName))
                    {
                        this.Pending.Enqueue(localName);
                    }
                }
            }
        }

        /// <summary>
        /// Rewrites the specified module definition using the specified transform.
        /// </summary>
        private static void RewriteModule(ModuleDefinition module, AssemblyTransform transform)
        {
            Debug.WriteLine($"....... Module: {module.Name} ({module.FileName})");
            transform.VisitModule(module);
            foreach (var type in module.GetTypes())
            {
                RewriteType(type, transform);
            }
        }

        /// <summary>
        /// Rewrites the specified type definition using the specified transform.
        /// </summary>
        private static void RewriteType(TypeDefinition type, AssemblyTransform transform)
        {
            Debug.WriteLine($"......... Type: {type.FullName}");
            transform.VisitType(type);
            foreach (var field in type.Fields.ToArray())
            {
                Debug.WriteLine($"........... Field: {field.FullName}");
                transform.VisitField(field);
            }

            foreach (var method in type.Methods.ToArray())
            {
                RewriteMethod(method, transform);
            }
        }

        /// <summary>
        /// Rewrites the specified method definition using the specified transform.
        /// </summary>
        private static void RewriteMethod(MethodDefinition method, AssemblyTransform transform)
        {
            if (method.Body == null)
            {
                return;
            }

            Debug.WriteLine($"........... Method {method.FullName}");
            transform.VisitMethod(method);
        }

        /// <summary>
        /// Applies the <see cref="IsAssemblyRewrittenAttribute"/> attribute to the specified assembly. This attribute
        /// indicates that the assembly has been rewritten with the current version of Coyote.
        /// </summary>
        private static void ApplyIsAssemblyRewrittenAttribute(AssemblyDefinition assembly)
        {
            CustomAttribute attribute = GetCustomAttribute(assembly, typeof(IsAssemblyRewrittenAttribute));
            var attributeArgument = new CustomAttributeArgument(
                assembly.MainModule.ImportReference(typeof(string)),
                GetAssemblyRewritterVersion().ToString());

            if (attribute is null)
            {
                MethodReference attributeConstructor = assembly.MainModule.ImportReference(
                    typeof(IsAssemblyRewrittenAttribute).GetConstructor(new Type[] { typeof(string) }));
                attribute = new CustomAttribute(attributeConstructor);
                attribute.ConstructorArguments.Add(attributeArgument);
                assembly.CustomAttributes.Add(attribute);
            }
            else
            {
                attribute.ConstructorArguments[0] = attributeArgument;
            }
        }

        /// <summary>
        /// Creates the output directory, if it does not already exists, and copies all necessery files.
        /// </summary>
        /// <returns>The output directory path.</returns>
        private string CreateOutputDirectoryAndCopyFiles()
        {
            string sourceDirectory = this.Options.AssembliesDirectory;
            string outputDirectory = Directory.CreateDirectory(this.Options.IsReplacingAssemblies ?
                Path.Combine(this.Options.OutputDirectory, TempDirectory) : this.Options.OutputDirectory).FullName;

            if (!this.Options.IsReplacingAssemblies)
            {
                this.Log.WriteLine($"... Copying all files to the '{outputDirectory}' directory");

                // Copy all files to the output directory, while preserving directory structure.
                foreach (string directoryPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    // Avoid copying the output directory itself.
                    if (!directoryPath.StartsWith(outputDirectory))
                    {
                        Debug.WriteLine($"..... Copying the '{directoryPath}' directory");
                        Directory.CreateDirectory(Path.Combine(outputDirectory, Path.GetDirectoryName(directoryPath)));
                    }
                }

                foreach (string filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    // Avoid copying any files from the output directory.
                    if (!filePath.StartsWith(outputDirectory))
                    {
                        Debug.WriteLine($"..... Copying the '{filePath}' file");
                        File.Copy(filePath, Path.Combine(outputDirectory, Path.GetFileName(filePath)), true);
                    }
                }
            }

            // Copy all the dependent assemblies
            foreach (var type in new Type[]
                {
                    typeof(ControlledTask),
                    typeof(AssemblyRewriter),
                    typeof(TelemetryConfiguration),
                    typeof(EventTelemetry),
                    typeof(ITelemetry),
                    typeof(TelemetryClient)
                })
            {
                string assemblyPath = type.Assembly.Location;
                File.Copy(assemblyPath, Path.Combine(this.Options.OutputDirectory, Path.GetFileName(assemblyPath)), true);
            }

            return outputDirectory;
        }

        /// <summary>
        /// Returns a new assembly resolver.
        /// </summary>
        private IAssemblyResolver GetAssemblyResolver()
        {
            // TODO: can we reuse it, or do we need a new one for each assembly?
            var assemblyResolver = new DefaultAssemblyResolver();

            // Add known search directories for resolving assemblies.
            assemblyResolver.AddSearchDirectory(Path.GetDirectoryName(typeof(ControlledTask).Assembly.Location));
            assemblyResolver.AddSearchDirectory(this.Options.AssembliesDirectory);

            // Add the assembly resolution error handler.
            assemblyResolver.ResolveFailure += this.OnResolveAssemblyFailure;
            return assemblyResolver;
        }

        /// <summary>
        /// Checks if the specified assembly has been already rewritten with the current version of Coyote.
        /// </summary>
        /// <param name="assembly">The assembly to check.</param>
        /// <returns>True if the assembly has been rewritten, else false.</returns>
        public static bool IsAssemblyRewritten(Assembly assembly) =>
            assembly.GetCustomAttribute(typeof(IsAssemblyRewrittenAttribute)) is IsAssemblyRewrittenAttribute attribute &&
            attribute.Version == GetAssemblyRewritterVersion().ToString();

        /// <summary>
        /// Checks if the specified assembly has been already rewritten with the current version of Coyote.
        /// </summary>
        /// <param name="assembly">The assembly to check.</param>
        /// <returns>True if the assembly has been rewritten, else false.</returns>
        private static bool IsAssemblyRewritten(AssemblyDefinition assembly)
        {
            var attribute = GetCustomAttribute(assembly, typeof(IsAssemblyRewrittenAttribute));
            return attribute != null && (string)attribute.ConstructorArguments[0].Value == GetAssemblyRewritterVersion().ToString();
        }

        /// <summary>
        /// Returns the first found custom attribute with the specified type, if such an attribute
        /// is applied to the specified assembly, else null.
        /// </summary>
        private static CustomAttribute GetCustomAttribute(AssemblyDefinition assembly, Type attributeType) =>
            assembly.CustomAttributes.FirstOrDefault(
                attr => attr.AttributeType.Namespace == attributeType.Namespace &&
                attr.AttributeType.Name == attributeType.Name);

        /// <summary>
        /// Returns the version of the assembly rewritter.
        /// </summary>
        private static Version GetAssemblyRewritterVersion() => Assembly.GetExecutingAssembly().GetName().Version;

        /// <summary>
        /// Checks if the symbol file for the specified assembly is available.
        /// </summary>
        private static bool IsSymbolFileAvailable(string assemblyPath) =>
            File.Exists(Path.ChangeExtension(assemblyPath, "pdb"));

        /// <summary>
        /// Handles an assembly resolution error.
        /// </summary>
        private AssemblyDefinition OnResolveAssemblyFailure(object sender, AssemblyNameReference reference)
        {
            this.Log.WriteErrorLine("Error resolving assembly: " + reference.FullName);
            return null;
        }
    }
}
