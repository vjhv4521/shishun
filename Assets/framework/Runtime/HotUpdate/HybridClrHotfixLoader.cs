using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Haven.Framework.Core;
using Haven.Framework.Resources;
using HybridCLR;

namespace Haven.Framework.HotUpdate
{
    public sealed class HybridClrHotfixLoader
    {
        private const string Module = "HybridCLR";
        private readonly FrameworkContext _context;
        private readonly HotUpdateSettings _settings;
        private readonly IResourceService _resources;
        private readonly Action<HotUpdateProgress> _report;
        private IHotfixEntry _entry;

        public HybridClrHotfixLoader(FrameworkContext context, Action<HotUpdateProgress> report)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _settings = context.Settings;
            _resources = context.Resources ?? throw new ArgumentNullException(nameof(context.Resources));
            _report = report;
            Result = FrameworkResult.Failure(new FrameworkError("HYBRID_NOT_STARTED", "HybridCLR loader has not started.", Module));
        }

        public FrameworkResult Result { get; private set; }
        public bool IsRunning => _entry != null;

        public IEnumerator Run()
        {
#if !UNITY_EDITOR
            yield return LoadAotMetadata();
            if (!Result.Succeeded)
                yield break;
#else
            Report(HotUpdateStage.LoadAotMetadata, 1f, "AOT metadata loading is skipped in the Unity Editor.");
#endif

            yield return LoadHotfixAssemblies();
            if (!Result.Succeeded)
                yield break;

            yield return StartEntry();
        }

        public void Tick(float deltaTime)
        {
            _entry?.Tick(deltaTime);
        }

        public void FixedTick(float fixedDeltaTime)
        {
            _entry?.FixedTick(fixedDeltaTime);
        }

        public void LateTick(float deltaTime)
        {
            _entry?.LateTick(deltaTime);
        }

        public void Shutdown()
        {
            if (_entry == null)
                return;
            try
            {
                _entry.Shutdown();
            }
            catch (Exception exception)
            {
                GameLog.Error(Module, "Hotfix shutdown failed.", "HOTFIX_SHUTDOWN_FAILED", exception, _context.CorrelationId);
            }
            finally
            {
                _entry = null;
            }
        }

        private IEnumerator LoadAotMetadata()
        {
            var locations = _settings.AotMetadataLocations;
            if (locations.Length == 0)
            {
                Result = FrameworkResult.Success();
                Report(HotUpdateStage.LoadAotMetadata, 1f, "No supplemental AOT metadata configured.");
                yield break;
            }

            for (var index = 0; index < locations.Length; index++)
            {
                var location = locations[index];
                FrameworkResult<byte[]> loadResult = default;
                yield return _resources.LoadRawBytes(location, value => loadResult = value);
                if (!loadResult.Succeeded)
                {
                    Result = FrameworkResult.Failure(new FrameworkError(
                        "AOT_METADATA_RESOURCE_FAILED",
                        $"Failed to load supplemental metadata '{location}': {loadResult.Error}",
                        Module,
                        true,
                        loadResult.Error?.Exception));
                    yield break;
                }

                LoadImageErrorCode errorCode;
                try
                {
                    errorCode = RuntimeApi.LoadMetadataForAOTAssembly(loadResult.Value, HomologousImageMode.SuperSet);
                }
                catch (Exception exception)
                {
                    Result = FrameworkResult.Failure(new FrameworkError(
                        "AOT_METADATA_EXCEPTION",
                        $"HybridCLR rejected supplemental metadata '{location}'.",
                        Module,
                        false,
                        exception));
                    yield break;
                }

                if (errorCode != LoadImageErrorCode.OK && errorCode != LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED)
                {
                    Result = FrameworkResult.Failure(new FrameworkError(
                        "AOT_METADATA_REJECTED",
                        $"HybridCLR metadata load failed for '{location}' with {errorCode}.",
                        Module));
                    yield break;
                }

                Report(HotUpdateStage.LoadAotMetadata, (index + 1f) / locations.Length, $"Loaded supplemental metadata: {location}.");
            }
            Result = FrameworkResult.Success();
        }

        private IEnumerator LoadHotfixAssemblies()
        {
            var descriptors = _settings.HotUpdateAssemblies;
            if (descriptors.Length == 0)
            {
                Result = FrameworkResult.Failure(new FrameworkError("HOTFIX_ASSEMBLY_EMPTY", "No hotfix assemblies are configured.", Module));
                yield break;
            }

#if UNITY_EDITOR
            if (_settings.UseLoadedHotfixAssemblyInEditor)
            {
                var missing = descriptors
                    .Where(descriptor => descriptor != null && FindLoadedAssembly(descriptor.AssemblyName) == null)
                    .Select(descriptor => descriptor.AssemblyName)
                    .ToArray();
                if (missing.Length == 0)
                {
                    Result = FrameworkResult.Success();
                    Report(HotUpdateStage.LoadHotfixAssemblies, 1f, "Using hotfix assemblies compiled by the Unity Editor.");
                    yield break;
                }
                Result = FrameworkResult.Failure(new FrameworkError(
                    "HOTFIX_EDITOR_ASSEMBLY_MISSING",
                    $"Unity Editor did not load: {string.Join(", ", missing)}. Reimport scripts or disable editor direct loading.",
                    Module,
                    true));
                yield break;
            }
#endif

            for (var index = 0; index < descriptors.Length; index++)
            {
                var descriptor = descriptors[index];
                if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.AssemblyName) || string.IsNullOrWhiteSpace(descriptor.Location))
                {
                    Result = FrameworkResult.Failure(new FrameworkError("HOTFIX_DESCRIPTOR_INVALID", $"Hotfix assembly descriptor at index {index} is invalid.", Module));
                    yield break;
                }

                if (FindLoadedAssembly(descriptor.AssemblyName) != null)
                {
                    Report(HotUpdateStage.LoadHotfixAssemblies, (index + 1f) / descriptors.Length, $"Assembly already loaded: {descriptor.AssemblyName}.");
                    continue;
                }

                FrameworkResult<byte[]> loadResult = default;
                yield return _resources.LoadRawBytes(descriptor.Location, value => loadResult = value);
                if (!loadResult.Succeeded)
                {
                    Result = FrameworkResult.Failure(new FrameworkError(
                        "HOTFIX_DLL_RESOURCE_FAILED",
                        $"Failed to load hotfix assembly '{descriptor.AssemblyName}': {loadResult.Error}",
                        Module,
                        true,
                        loadResult.Error?.Exception));
                    yield break;
                }

                try
                {
                    Assembly.Load(loadResult.Value);
                }
                catch (Exception exception)
                {
                    Result = FrameworkResult.Failure(new FrameworkError(
                        "HOTFIX_DLL_LOAD_FAILED",
                        $"Assembly.Load failed for '{descriptor.AssemblyName}'. Ensure dependencies are listed first.",
                        Module,
                        false,
                        exception));
                    yield break;
                }

                Report(HotUpdateStage.LoadHotfixAssemblies, (index + 1f) / descriptors.Length, $"Loaded hotfix assembly: {descriptor.AssemblyName}.");
            }

            Result = FrameworkResult.Success();
        }

        private IEnumerator StartEntry()
        {
            Report(HotUpdateStage.StartHotfix, 0f, $"Starting {_settings.HotfixEntryType}.");
            Type entryType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                entryType = assembly.GetType(_settings.HotfixEntryType, false);
                if (entryType != null)
                    break;
            }

            if (entryType == null)
            {
                Result = FrameworkResult.Failure(new FrameworkError("HOTFIX_ENTRY_NOT_FOUND", $"Hotfix entry type not found: {_settings.HotfixEntryType}.", Module));
                yield break;
            }
            if (!typeof(IHotfixEntry).IsAssignableFrom(entryType))
            {
                Result = FrameworkResult.Failure(new FrameworkError("HOTFIX_ENTRY_CONTRACT", $"{entryType.FullName} must implement {typeof(IHotfixEntry).FullName}.", Module));
                yield break;
            }

            try
            {
                _entry = (IHotfixEntry)Activator.CreateInstance(entryType);
            }
            catch (Exception exception)
            {
                Result = FrameworkResult.Failure(new FrameworkError("HOTFIX_ENTRY_CREATE_FAILED", $"Could not create {entryType.FullName}.", Module, false, exception));
                yield break;
            }

            IEnumerator initializer;
            try
            {
                initializer = _entry.Initialize(new HotfixContext(_context));
            }
            catch (Exception exception)
            {
                _entry = null;
                Result = FrameworkResult.Failure(new FrameworkError("HOTFIX_ENTRY_INIT_START_FAILED", "Hotfix entry initialization threw an exception.", Module, false, exception));
                yield break;
            }

            if (initializer != null)
                yield return initializer;

            Result = FrameworkResult.Success();
            Report(HotUpdateStage.StartHotfix, 1f, "Hotfix entry started.");
        }

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                return null;
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal));
        }

        private void Report(HotUpdateStage stage, float progress, string message)
        {
            _report?.Invoke(new HotUpdateProgress(stage, progress, message));
        }
    }
}
