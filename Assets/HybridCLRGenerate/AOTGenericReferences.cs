using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"Haven.Framework.dll",
		"System.Core.dll",
		"UnityEngine.CoreModule.dll",
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
	// System.Collections.Generic.ArrayBuilder<System.Collections.Generic.Marker>
	// System.Collections.Generic.ArrayBuilder<int>
	// System.Collections.Generic.ArrayBuilder<object>
	// System.Collections.Generic.ArraySortHelper<object>
	// System.Collections.Generic.Comparer<byte>
	// System.Collections.Generic.Comparer<int>
	// System.Collections.Generic.Comparer<object>
	// System.Collections.Generic.ComparisonComparer<byte>
	// System.Collections.Generic.ComparisonComparer<int>
	// System.Collections.Generic.ComparisonComparer<object>
	// System.Collections.Generic.EqualityComparer<object>
	// System.Collections.Generic.HashSet.Enumerator<object>
	// System.Collections.Generic.HashSet<object>
	// System.Collections.Generic.HashSetEqualityComparer<object>
	// System.Collections.Generic.ICollection<object>
	// System.Collections.Generic.IComparer<byte>
	// System.Collections.Generic.IComparer<object>
	// System.Collections.Generic.IEnumerable<object>
	// System.Collections.Generic.IEnumerator<object>
	// System.Collections.Generic.IEqualityComparer<object>
	// System.Collections.Generic.IList<object>
	// System.Collections.Generic.LargeArrayBuilder<object>
	// System.Collections.Generic.List.Enumerator<object>
	// System.Collections.Generic.List<object>
	// System.Collections.Generic.ObjectComparer<byte>
	// System.Collections.Generic.ObjectComparer<int>
	// System.Collections.Generic.ObjectComparer<object>
	// System.Collections.Generic.ObjectEqualityComparer<object>
	// System.Collections.Generic.SparseArrayBuilder<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<object>
	// System.Comparison<byte>
	// System.Comparison<int>
	// System.Comparison<object>
	// System.Func<object,byte>
	// System.Func<object,int>
	// System.Func<object,object>
	// System.Func<object>
	// System.Linq.Buffer<object>
	// System.Linq.CachingComparer<object,byte>
	// System.Linq.CachingComparer<object>
	// System.Linq.CachingComparerWithChild<object,byte>
	// System.Linq.Enumerable.Concat2Iterator<object>
	// System.Linq.Enumerable.ConcatIterator<object>
	// System.Linq.Enumerable.ConcatNIterator<object>
	// System.Linq.Enumerable.DistinctIterator<object>
	// System.Linq.Enumerable.Iterator<object>
	// System.Linq.Enumerable.WhereArrayIterator<object>
	// System.Linq.Enumerable.WhereEnumerableIterator<object>
	// System.Linq.Enumerable.WhereListIterator<object>
	// System.Linq.EnumerableSorter<object,byte>
	// System.Linq.EnumerableSorter<object>
	// System.Linq.IIListProvider<object>
	// System.Linq.IOrderedEnumerable<object>
	// System.Linq.OrderedEnumerable.<GetEnumerator>d__3<object>
	// System.Linq.OrderedEnumerable.<GetEnumerator>d__7<object>
	// System.Linq.OrderedEnumerable<object,byte>
	// System.Linq.OrderedEnumerable<object>
	// System.Linq.OrderedPartition<object>
	// System.Linq.Set<object>
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
		// object[] System.Collections.Generic.EnumerableHelpers.ToArray<object>(System.Collections.Generic.IEnumerable<object>)
		// bool System.Linq.Enumerable.Any<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Concat<object>(System.Collections.Generic.IEnumerable<object>,System.Collections.Generic.IEnumerable<object>)
		// bool System.Linq.Enumerable.Contains<object>(System.Collections.Generic.IEnumerable<object>,object)
		// bool System.Linq.Enumerable.Contains<object>(System.Collections.Generic.IEnumerable<object>,object,System.Collections.Generic.IEqualityComparer<object>)
		// int System.Linq.Enumerable.Count<object>(System.Collections.Generic.IEnumerable<object>)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Distinct<object>(System.Collections.Generic.IEnumerable<object>)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Distinct<object>(System.Collections.Generic.IEnumerable<object>,System.Collections.Generic.IEqualityComparer<object>)
		// object System.Linq.Enumerable.First<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
		// System.Linq.IOrderedEnumerable<object> System.Linq.Enumerable.OrderByDescending<object,byte>(System.Collections.Generic.IEnumerable<object>,System.Func<object,byte>)
		// System.Linq.IOrderedEnumerable<object> System.Linq.Enumerable.ThenBy<object,byte>(System.Linq.IOrderedEnumerable<object>,System.Func<object,byte>)
		// System.Linq.IOrderedEnumerable<object> System.Linq.Enumerable.ThenBy<object,object>(System.Linq.IOrderedEnumerable<object>,System.Func<object,object>,System.Collections.Generic.IComparer<object>)
		// System.Linq.IOrderedEnumerable<object> System.Linq.Enumerable.ThenByDescending<object,int>(System.Linq.IOrderedEnumerable<object>,System.Func<object,int>)
		// object[] System.Linq.Enumerable.ToArray<object>(System.Collections.Generic.IEnumerable<object>)
		// object System.Linq.Enumerable.TryGetFirst<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>,bool&)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Where<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
		// System.Linq.IOrderedEnumerable<object> System.Linq.IOrderedEnumerable<object>.CreateOrderedEnumerable<byte>(System.Func<object,byte>,System.Collections.Generic.IComparer<byte>,bool)
		// System.Linq.IOrderedEnumerable<object> System.Linq.IOrderedEnumerable<object>.CreateOrderedEnumerable<int>(System.Func<object,int>,System.Collections.Generic.IComparer<int>,bool)
		// System.Linq.IOrderedEnumerable<object> System.Linq.IOrderedEnumerable<object>.CreateOrderedEnumerable<object>(System.Func<object,object>,System.Collections.Generic.IComparer<object>,bool)
		// object UnityEngine.JsonUtility.FromJson<object>(string)
		// object UnityEngine.Object.FindAnyObjectByType<object>()
	}
}