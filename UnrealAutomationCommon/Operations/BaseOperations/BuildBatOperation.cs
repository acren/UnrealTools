using System.Collections.Generic;
using System.Linq;
using LocalAutomation.Commands;
using UnrealAutomationCommon.Operations.OperationOptionTypes;
using UnrealAutomationCommon.Unreal;
using RuntimeTarget = LocalAutomation.Runtime.OperationTarget;

namespace UnrealAutomationCommon.Operations.BaseOperations
{
    // Centralize the direct Build.bat wiring so all UBT-backed operations stay consistent.
    public class BuildBatOperation<T> : UnrealOperation<T> where T : RuntimeTarget
    {
        /// <summary>
        /// Composes direct Build.bat execution with Unreal argument and output policy.
        /// </summary>
        public BuildBatOperation()
        {
            UseExecutionBehavior(new CommandProcessBehavior(BuildCommand, UnrealCommandProcessPolicy.Instance, GetExecutionRetryPolicy));
        }

        /// <summary>
        /// Direct Build.bat-backed operations expose shared build behavior, configuration, and compiler overrides.
        /// </summary>
        protected override System.Collections.Generic.IEnumerable<System.Type> GetDeclaredOptionSetTypes(global::LocalAutomation.Runtime.IOperationTarget target)
        {
            return base.GetDeclaredOptionSetTypes(target)
                .Concat(new[]
                {
                    typeof(BuildConfigurationOptions),
                    typeof(BuildOptions),
                    typeof(UbtCompilerOptions)
                });
        }

        protected override IEnumerable<global::LocalAutomation.Runtime.ExecutionLock> GetExecutionLocks(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            /* Direct Build.bat flows participate in the shared Unreal build lock so multiple callbacks in the same app do
               not race on UnrealBuildTool's writable rules state. */
            foreach (global::LocalAutomation.Runtime.ExecutionLock executionLock in base.GetExecutionLocks(operationParameters))
            {
                yield return executionLock;
            }

            yield return UnrealExecutionLocks.GlobalBuild;
        }

        /// <summary>
        /// Direct Build.bat invocations can rerun the complete command body after build-tool contention or a Clang frontend crash.
        /// </summary>
        private global::LocalAutomation.Runtime.ExecutionRetryPolicy? GetExecutionRetryPolicy(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            return UnrealBuildRetryPolicies.Build;
        }

        // Validate shared direct-UBT overrides once so every Build.bat-backed operation enforces the same limits.
        protected override string? CheckRequirementsSatisfied(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            string? engineSelectionError = GetSingleEngineSelectionValidationMessage(operationParameters);
            if (engineSelectionError != null)
            {
                return engineSelectionError;
            }

            UbtCompilerOptions buildBatOptions = operationParameters.GetOptions<UbtCompilerOptions>();

            Engine? engine = GetTargetEngineInstall(operationParameters);
            if (engine == null)
            {
                return null;
            }

            // UE 5.5 and newer have dropped Cpp17 support, so fail early before invoking UBT with an invalid override.
            EngineVersion engineVersion = engine.Version;
            if (buildBatOptions.CppStandard == UbtCppStandard.Cpp17 && engineVersion >= new EngineVersion(5, 5, 0))
            {
                return $"C++17 is not supported for Unreal Engine {engineVersion.MajorMinorString} or newer";
            }

            if (buildBatOptions.Compiler == UbtCompiler.Clang)
            {
                // Clang builds need the selected engine's preferred family before UBT is launched.
                string? compilerVersionError = UbtClangToolchainPreferences.TryGetPreferredToolchain(engine, out _, out _);
                if (compilerVersionError != null)
                {
                    return compilerVersionError;
                }
            }

            return null;
        }

        private Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Arguments args = new();

            // Let derived operations describe the target-specific portion of the Build.bat invocation first.
            ConfigureBuildArguments(operationParameters, args);
            string? clangToolchainRoot = ApplySharedBuildArguments(operationParameters, args);
            Command command = new(GetRequiredTargetEngineInstall(operationParameters).GetBuildPath(), args.ToString());
            if (!string.IsNullOrWhiteSpace(clangToolchainRoot))
            {
                // UBT reads LLVM_PATH while discovering Clang, so set it only for this Build.bat process.
                command.EnvironmentVariables["LLVM_PATH"] = clangToolchainRoot;
            }

            return command;
        }

        // Derived operations provide the target-specific portion of the direct Build.bat invocation.
        protected virtual void ConfigureBuildArguments(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters, Arguments args)
        {
        }

        // Raw generic Build.bat children need a readable operation name even though the generic type name includes arity.
        protected override string GetOperationName()
        {
            if (GetType() == typeof(BuildBatOperation<T>))
            {
                return "Build.bat";
            }

            return base.GetOperationName();
        }

        // Apply the shared direct-UBT overrides only for Build.bat flows that are known to respect them.
        protected string? ApplySharedBuildArguments(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters, Arguments args)
        {
            BuildOptions buildOptions = operationParameters.GetOptions<BuildOptions>();
            UbtCompilerOptions buildBatOptions = operationParameters.GetOptions<UbtCompilerOptions>();
            UbtCompiler compiler = buildBatOptions.Compiler;
            UbtCppStandard cppStandard = buildBatOptions.CppStandard;
            string? clangToolchainRoot = null;

            // Automation builds can explicitly disable UnrealBuildTool hot reload.
            if (buildOptions.NoHotReload)
            {
                args.SetFlag("NoHotReload");
            }

            // Only emit an explicit compiler flag when the user has opted out of the engine default behavior.
            if (compiler != UbtCompiler.Default)
            {
                args.SetKeyValue("Compiler", compiler.ToString());
            }

            if (compiler == UbtCompiler.Clang)
            {
                Engine engine = GetRequiredTargetEngineInstall(operationParameters);
                string? compilerVersionError = UbtClangToolchainPreferences.TryGetPreferredToolchain(engine, out string compilerVersion, out clangToolchainRoot);
                if (compilerVersionError != null)
                {
                    throw new System.InvalidOperationException(compilerVersionError);
                }

                // Pin UBT to the engine-preferred Clang family so it cannot auto-select a later unsupported family.
                args.SetKeyValue("CompilerVersion", compilerVersion);
            }

            // Only emit an explicit language standard when the user has selected one of the supported UBT values.
            if (cppStandard != UbtCppStandard.Default)
            {
                args.SetKeyValue("CppStdEngine", cppStandard.ToString());
            }

            return clangToolchainRoot;
        }
    }
}
