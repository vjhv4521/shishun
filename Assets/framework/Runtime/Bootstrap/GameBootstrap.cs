using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haven.Framework.Composition;
using Haven.Framework.Core;
using Haven.Framework.HotUpdate;
using Haven.Framework.Resources;
using UnityEngine;

namespace Haven.Framework.Bootstrap
{
    public enum BootstrapState
    {
        Idle,
        Starting,
        Running,
        Failed,
        ShuttingDown
    }

    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private const string Module = "Bootstrap";
        private static GameBootstrap _instance;

        [SerializeField] private HotUpdateSettings settings;
        [SerializeField] private bool persistAcrossScenes = true;

        private EventBus _events;
        private ServiceRegistry _services;
        private FrameworkContext _context;
        private HybridClrHotfixLoader _hotfixLoader;
        private Coroutine _startupCoroutine;
        private readonly List<IFrameworkServiceInstaller> _installedServices = new List<IFrameworkServiceInstaller>();

        public static GameBootstrap Instance => _instance;
        public event Action<HotUpdateProgress> ProgressChanged;
        public BootstrapState State { get; private set; } = BootstrapState.Idle;
        public FrameworkError LastError { get; private set; }
        public HotUpdateProgress LastProgress { get; private set; }
        public bool CanRetry => State == BootstrapState.Failed && LastError?.Retryable == true;
        public FrameworkContext Context => _context;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapExists()
        {
            if (_instance)
                return;
            var existing = FindAnyObjectByType<GameBootstrap>();
            if (existing)
            {
                _instance = existing;
                return;
            }
            var root = new GameObject("[HavenFramework]");
            root.AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            if (_instance && _instance != this)
            {
                // A scene bootstrap may share its GameObject with scene-specific installers
                // and gameplay objects. Keep that object alive when a persistent bootstrap
                // already exists; only the duplicate bootstrap component is redundant.
                Destroy(this);
                return;
            }

            _instance = this;
            if (persistAcrossScenes)
                DontDestroyOnLoad(gameObject);

            if (!settings)
                settings = UnityEngine.Resources.Load<HotUpdateSettings>(HotUpdateSettings.DefaultResourceName);
            if (!settings)
            {
                settings = HotUpdateSettings.CreateRuntimeDefault();
                GameLog.Warning(Module, "HavenHotUpdateSettings asset not found; using editor-direct runtime defaults.", "BOOT_DEFAULT_SETTINGS");
            }

            if (settings.AutoStart)
                StartFramework();
        }

        public void StartFramework()
        {
            if (State == BootstrapState.Starting || State == BootstrapState.Running)
                return;
            if (_startupCoroutine != null)
                StopCoroutine(_startupCoroutine);
            _startupCoroutine = StartCoroutine(StartupFlow());
        }

        public void Retry()
        {
            if (!CanRetry)
                return;
            ShutdownRuntime();
            State = BootstrapState.Idle;
            LastError = null;
            StartFramework();
        }

        public void RestartForCurrentScene()
        {
            PrepareForSceneTransition();
            StartFramework();
        }

        public void PrepareForSceneTransition()
        {
            ShutdownRuntime();
            State = BootstrapState.Idle;
            LastError = null;
        }

        private IEnumerator StartupFlow()
        {
            State = BootstrapState.Starting;
            LastError = null;

            _events = new EventBus();
            _services = new ServiceRegistry();
            _context = new FrameworkContext(gameObject, settings, _services, _events);
            var resources = new YooAssetResourceService();
            _context.Resources = resources;
            _services.Register<IEventBus>(_events);
            _services.Register<IServiceRegistry>(_services);
            _services.Register<IResourceService>(resources);

            Report(new HotUpdateProgress(HotUpdateStage.InitializeFramework, 1f, "Core services initialized."));

            FrameworkError installerError = null;
            yield return InstallAotServices(error => installerError = error);
            if (installerError != null)
            {
                Fail(installerError);
                yield break;
            }

#if UNITY_SERVER && !UNITY_EDITOR
            _context.ContentVersion = Application.version;
            CompleteStartup("Dedicated Server core services are ready.");
            yield break;
#endif

            var updateService = new YooAssetUpdateService(_context, resources, Report);
            Exception updateException = null;
            yield return SafeCoroutine.Run(updateService.Run(), exception => updateException = exception);
            if (updateException != null)
            {
                Fail(new FrameworkError(
                    "BOOT_UPDATE_EXCEPTION",
                    "The content update workflow threw an unhandled exception.",
                    Module,
                    true,
                    updateException));
                yield break;
            }
            if (!updateService.Result.Succeeded)
            {
                Fail(updateService.Result.Error);
                yield break;
            }

            _hotfixLoader = new HybridClrHotfixLoader(_context, Report);
            Exception hotfixException = null;
            yield return SafeCoroutine.Run(_hotfixLoader.Run(), exception => hotfixException = exception);
            if (hotfixException != null)
            {
                Fail(new FrameworkError(
                    "BOOT_HOTFIX_EXCEPTION",
                    "The hotfix startup workflow threw an unhandled exception.",
                    Module,
                    false,
                    hotfixException));
                yield break;
            }
            if (!_hotfixLoader.Result.Succeeded)
            {
                Fail(_hotfixLoader.Result.Error);
                yield break;
            }

            CompleteStartup("Framework and hotfix runtime are ready.");
        }

        private IEnumerator InstallAotServices(Action<FrameworkError> failed)
        {
            var installers = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
                .OfType<IFrameworkServiceInstaller>()
                .OrderBy(item => item.Order)
                .ToArray();

            foreach (var installer in installers)
            {
                Exception exception = null;
                IEnumerator routine;
                try
                {
                    routine = installer.Install(_context);
                }
                catch (Exception caught)
                {
                    routine = null;
                    exception = caught;
                }

                if (routine != null && exception == null)
                    yield return SafeCoroutine.Run(routine, caught => exception = caught);
                if (exception != null)
                {
                    failed?.Invoke(new FrameworkError(
                        "BOOT_SERVICE_INSTALL_FAILED",
                        $"AOT service installer '{installer.GetType().FullName}' failed.",
                        Module,
                        false,
                        exception));
                    yield break;
                }

                _installedServices.Add(installer);
            }
        }

        private void CompleteStartup(string message)
        {
            State = BootstrapState.Running;
            _startupCoroutine = null;
            _events.Publish(new HotUpdateCompleted(_context.ContentVersion));
            Report(new HotUpdateProgress(HotUpdateStage.Completed, 1f, message));
            GameLog.Info(Module, $"Framework started. contentVersion={_context.ContentVersion}", "BOOT_COMPLETED", _context.CorrelationId);
        }

        private void Update()
        {
            if (State == BootstrapState.Running)
                _hotfixLoader?.Tick(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (State == BootstrapState.Running)
                _hotfixLoader?.FixedTick(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            if (State == BootstrapState.Running)
                _hotfixLoader?.LateTick(Time.deltaTime);
        }

        private void OnApplicationQuit()
        {
            ShutdownRuntime();
        }

        private void OnDestroy()
        {
            if (_instance != this)
                return;
            ShutdownRuntime();
            ProgressChanged = null;
            _instance = null;
        }

        private void Fail(FrameworkError error)
        {
            _hotfixLoader?.Shutdown();
            LastError = error ?? new FrameworkError("BOOT_UNKNOWN", "Unknown startup error.", Module);
            State = BootstrapState.Failed;
            _startupCoroutine = null;
            _events?.Publish(new HotUpdateFailed(LastError));
            Report(new HotUpdateProgress(HotUpdateStage.Failed, 0f, LastError.ToString()));
            GameLog.Error(Module, LastError.Message, LastError.Code, LastError.Exception, _context?.CorrelationId);
        }

        private void Report(HotUpdateProgress progress)
        {
            LastProgress = progress;
            var listeners = ProgressChanged?.GetInvocationList();
            if (listeners != null)
            {
                foreach (var listener in listeners)
                {
                    try
                    {
                        ((Action<HotUpdateProgress>)listener).Invoke(progress);
                    }
                    catch (Exception exception)
                    {
                        GameLog.Error(Module, "A hot-update progress listener failed.", "BOOT_PROGRESS_LISTENER_FAILED", exception, _context?.CorrelationId);
                    }
                }
            }
            _events?.Publish(progress);
            if (progress.Stage != HotUpdateStage.DownloadFiles || progress.NormalizedProgress >= 1f)
                GameLog.Info(Module, progress.Message, progress.Stage.ToString(), _context?.CorrelationId);
        }

        private void ShutdownRuntime()
        {
            if (State == BootstrapState.ShuttingDown)
                return;
            State = BootstrapState.ShuttingDown;
            _hotfixLoader?.Shutdown();
            _hotfixLoader = null;
            for (var index = _installedServices.Count - 1; index >= 0; index--)
            {
                try
                {
                    _installedServices[index].Uninstall();
                }
                catch (Exception exception)
                {
                    GameLog.Error(Module, $"AOT service uninstall failed: {_installedServices[index].GetType().FullName}", "BOOT_SERVICE_UNINSTALL_FAILED", exception);
                }
            }
            _installedServices.Clear();
            _services?.Clear();
            _events?.Clear();
            _context = null;
            _services = null;
            _events = null;
            if (_startupCoroutine != null)
            {
                StopCoroutine(_startupCoroutine);
                _startupCoroutine = null;
            }
            State = BootstrapState.Idle;
        }
    }
}
