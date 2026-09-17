using System;
using UnityEngine;

namespace Haven.Framework.HotUpdate
{
    public enum HotUpdatePlayMode
    {
        EditorDirect,
        EditorSimulate,
        Offline,
        Host
    }

    [Serializable]
    public sealed class HotUpdateAssemblyReference
    {
        public HotUpdateAssemblyReference()
        {
        }

        public HotUpdateAssemblyReference(string assemblyName, string location)
        {
            AssemblyName = assemblyName;
            Location = location;
        }

        public string AssemblyName = string.Empty;
        public string Location = string.Empty;
    }

    [CreateAssetMenu(fileName = "HavenHotUpdateSettings", menuName = "Haven/Hot Update Settings")]
    public sealed class HotUpdateSettings : ScriptableObject
    {
        public const string DefaultResourceName = "HavenHotUpdateSettings";

        [Header("Startup")]
        [SerializeField] private HotUpdatePlayMode playMode = HotUpdatePlayMode.EditorDirect;
        [SerializeField] private string packageName = "DefaultPackage";
        [SerializeField] private bool autoStart = true;

        [Header("Remote content")]
        [SerializeField] private string primaryHost = "http://127.0.0.1:8080/CDN";
        [SerializeField] private string fallbackHost = "http://127.0.0.1:8080/CDN";
        [SerializeField] private string appVersion = "v1.0";
        [SerializeField] private bool appendPlatformAndVersion = true;

        [Header("Download and retry")]
        [SerializeField, Min(1)] private int requestTimeoutSeconds = 60;
        [SerializeField, Min(0)] private int workflowRetryCount = 2;
        [SerializeField, Min(1)] private int downloadMaxConcurrency = 8;
        [SerializeField, Min(0)] private int downloadFileRetryCount = 3;
        [SerializeField, Min(0f)] private float retryDelaySeconds = 1f;

        [Header("HybridCLR")]
        [SerializeField] private string hotfixEntryType = "Haven.Hotfix.HotfixEntry";
        [SerializeField] private HotUpdateAssemblyReference[] hotUpdateAssemblies =
        {
            new HotUpdateAssemblyReference("Haven.Hotfix", "Haven.Hotfix.dll")
        };
        [SerializeField] private string[] aotMetadataLocations =
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "Haven.Framework.dll",
            "UnityEngine.JSONSerializeModule.dll"
        };
        [SerializeField] private bool useLoadedHotfixAssemblyInEditor = true;

        public HotUpdatePlayMode PlayMode => playMode;
        public string PackageName => packageName;
        public bool AutoStart => autoStart;
        public string PrimaryHost => primaryHost;
        public string FallbackHost => fallbackHost;
        public string AppVersion => appVersion;
        public bool AppendPlatformAndVersion => appendPlatformAndVersion;
        public int RequestTimeoutSeconds => requestTimeoutSeconds;
        public int WorkflowRetryCount => workflowRetryCount;
        public int DownloadMaxConcurrency => downloadMaxConcurrency;
        public int DownloadFileRetryCount => downloadFileRetryCount;
        public float RetryDelaySeconds => retryDelaySeconds;
        public string HotfixEntryType => hotfixEntryType;
        public HotUpdateAssemblyReference[] HotUpdateAssemblies => hotUpdateAssemblies ?? Array.Empty<HotUpdateAssemblyReference>();
        public string[] AotMetadataLocations => aotMetadataLocations ?? Array.Empty<string>();
        public bool UseLoadedHotfixAssemblyInEditor => useLoadedHotfixAssemblyInEditor;

        public static HotUpdateSettings CreateRuntimeDefault()
        {
            var settings = CreateInstance<HotUpdateSettings>();
            settings.hideFlags = HideFlags.DontSave;
            return settings;
        }

        private void OnValidate()
        {
            packageName = string.IsNullOrWhiteSpace(packageName) ? "DefaultPackage" : packageName.Trim();
            appVersion = string.IsNullOrWhiteSpace(appVersion) ? "v1.0" : appVersion.Trim();
            hotfixEntryType = string.IsNullOrWhiteSpace(hotfixEntryType) ? "Haven.Hotfix.HotfixEntry" : hotfixEntryType.Trim();
            requestTimeoutSeconds = Mathf.Max(1, requestTimeoutSeconds);
            workflowRetryCount = Mathf.Max(0, workflowRetryCount);
            downloadMaxConcurrency = Mathf.Max(1, downloadMaxConcurrency);
            downloadFileRetryCount = Mathf.Max(0, downloadFileRetryCount);
            retryDelaySeconds = Mathf.Max(0f, retryDelaySeconds);
        }
    }
}
