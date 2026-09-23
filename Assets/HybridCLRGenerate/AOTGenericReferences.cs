using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"Haven.Framework.dll",
		"UnityEngine.JSONSerializeModule.dll",
		"mscorlib.dll",
	};
	// }}

	// {{ constraint implement type
	// }} 

	// {{ AOT generic types
	// Haven.Framework.Core.FrameworkResult<Haven.Framework.Services.AiQuestResponse>
	// Haven.Framework.Core.FrameworkResult<Haven.Framework.Services.RoomSnapshot>
	// Haven.Framework.Core.FrameworkResult<object>
	// Haven.Framework.Resources.ResourceLease<object>
	// System.Action<Haven.Framework.Core.FrameworkResult<Haven.Framework.Services.AiQuestResponse>>
	// System.Action<Haven.Framework.Core.FrameworkResult<Haven.Framework.Services.RoomSnapshot>>
	// System.Action<Haven.Framework.Core.FrameworkResult<object>>
	// System.Action<Haven.Framework.Core.FrameworkResult>
	// System.Action<Haven.Framework.Services.NetworkStateChanged>
	// System.Action<Haven.Framework.Services.RoomGameLoadProgressChanged>
	// System.Action<Haven.Framework.Services.RoomSnapshotChanged>
	// System.Action<byte>
	// System.Action<object>
	// System.Collections.Generic.ArraySortHelper<object>
	// System.Collections.Generic.Comparer<object>
	// System.Collections.Generic.ComparisonComparer<object>
	// System.Collections.Generic.ICollection<object>
	// System.Collections.Generic.IComparer<object>
	// System.Collections.Generic.IEnumerable<object>
	// System.Collections.Generic.IEnumerator<object>
	// System.Collections.Generic.IList<object>
	// System.Collections.Generic.List.Enumerator<object>
	// System.Collections.Generic.List<object>
	// System.Collections.Generic.ObjectComparer<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<object>
	// System.Comparison<object>
	// System.Func<object,byte>
	// System.Func<object,int>
	// System.Func<object,object>
	// System.Func<object>
	// System.Predicate<object>
	// }}

	public void RefMethods()
	{
		// System.Void Haven.Framework.Core.IEventBus.Publish<Haven.Framework.CampQuests.CampQuestChanged>(Haven.Framework.CampQuests.CampQuestChanged)
		// System.Void Haven.Framework.Core.IEventBus.Publish<Haven.Hotfix.Flow.GameFlowStateChanged>(Haven.Hotfix.Flow.GameFlowStateChanged)
		// System.Void Haven.Framework.Core.IEventBus.Publish<Haven.Hotfix.HotfixRuntimeReady>(Haven.Hotfix.HotfixRuntimeReady)
		// System.IDisposable Haven.Framework.Core.IEventBus.Subscribe<Haven.Framework.Services.NetworkStateChanged>(System.Action<Haven.Framework.Services.NetworkStateChanged>)
		// System.IDisposable Haven.Framework.Core.IEventBus.Subscribe<Haven.Framework.Services.RoomGameLoadProgressChanged>(System.Action<Haven.Framework.Services.RoomGameLoadProgressChanged>)
		// System.IDisposable Haven.Framework.Core.IEventBus.Subscribe<Haven.Framework.Services.RoomSnapshotChanged>(System.Action<Haven.Framework.Services.RoomSnapshotChanged>)
		// System.Collections.IEnumerator Haven.Framework.Resources.IResourceService.LoadAsset<object>(string,System.Action<Haven.Framework.Core.FrameworkResult<Haven.Framework.Resources.ResourceLease<object>>>)
		// object[] System.Array.Empty<object>()
		// object UnityEngine.JsonUtility.FromJson<object>(string)
	}
}