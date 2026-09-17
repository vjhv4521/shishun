using System;
using System.Collections;
using Haven.Framework.Core;

namespace Haven.Framework.CampQuests
{
    [Serializable]
    public sealed class CampQuestDefinition
    {
        public string id;
        public string title;
        public string itemId;
        public int quantity;
        public string rewardId;
        public int rewardQuantity;
        public string condition;
        public int priority;
        public bool large;
        public string fallbackDialogue;
    }

    [Serializable]
    public sealed class CampQuestCatalog
    {
        public int version;
        public CampQuestDefinition[] quests;
    }

    [Serializable]
    public sealed class CampQuestWorldState
    {
        public string sessionId;
        public int day;
        public float hour;
        public bool hasFirepit;
        public int wallCount;
        public int wood;
        public int rock;
        public int bread;
        public float distance;
        public bool alive;
    }

    [Serializable]
    public sealed class CampQuestRequest
    {
        public string requestId;
        public int catalogVersion;
        public string preference;
        public string playerMessage;
        public CampQuestWorldState context;
        public string[] completedItems;
        public CampQuestDefinition[] candidates;
    }

    [Serializable]
    public sealed class CampQuestProposal
    {
        public string requestId;
        public string candidateId;
        public string dialogue;
        public string source;
    }

    [Serializable]
    public sealed class CampQuestRecord
    {
        public string questId;
        public int offeredDay;
        public CampQuestDefinition definition;
        public string dialogue;
        public string source;
    }

    [Serializable]
    public sealed class CampQuestSave
    {
        public int version = 1;
        public int completionDay;
        public string[] completedItems = Array.Empty<string>();
        public int contributedWood;
        public int contributedRock;
        // JsonUtility materializes inline null classes; explicit presence bits preserve empty slots.
        public bool hasOffer;
        public bool hasActive;
        public CampQuestRecord offer;
        public CampQuestRecord active;
        public string lastCompletedQuestId;
    }

    public sealed class CampQuestView
    {
        public CampQuestRecord Offer { get; set; }
        public CampQuestRecord Active { get; set; }
        public CampQuestWorldState World { get; set; }
        public bool Busy { get; set; }
        public string Error { get; set; }
        public int CompletedToday { get; set; }
        public int ContributedWood { get; set; }
        public int ContributedRock { get; set; }
        public int Held => Active == null || World == null ? 0 :
            Active.definition.itemId == "wood" ? World.wood : World.rock;
    }

    public sealed class CampQuestConfiguration
    {
        public string CatalogJson { get; set; }
    }

    public interface ICampQuestWorldBridge
    {
        bool IsReady { get; }
        CampQuestWorldState Capture();
        string ReadSave();
        // The bridge commits inventory and quest state to the SAME world save, or restores both.
        FrameworkResult Commit(string sessionId, string saveJson, CampQuestDefinition exchange = null);
    }

    public interface ICampQuestGenerator : IDisposable
    {
        IEnumerator Generate(CampQuestRequest request, Action<FrameworkResult<CampQuestProposal>> completed);
        void Cancel();
    }

    public interface ICampQuestService : IDisposable
    {
        CampQuestView GetView();
        IEnumerator Propose(string preference, string message, Action<FrameworkResult> completed);
        FrameworkResult Accept();
        FrameworkResult Deliver();
        FrameworkResult Abandon();
    }

    [Serializable]
    public sealed class CampNpcChatTurn
    {
        public string role;
        public string text;
    }

    [Serializable]
    public sealed class CampNpcChatRequest
    {
        public string requestId;
        public string playerMessage;
        public CampQuestWorldState context;
        public string activeQuestId;
        public int activeHeld;
        public int completedToday;
        public CampNpcChatTurn[] history;
    }

    [Serializable]
    public sealed class CampNpcChatResponse
    {
        public string requestId;
        public string reply;
        public string source;
    }

    public interface ICampNpcChatGenerator : IDisposable
    {
        IEnumerator Generate(CampNpcChatRequest request, Action<FrameworkResult<CampNpcChatResponse>> completed);
        void Cancel();
    }

    public interface ICampNpcChatService : IDisposable
    {
        bool Busy { get; }
        CampNpcChatTurn[] GetHistory();
        IEnumerator Send(string message, Action<FrameworkResult<CampNpcChatResponse>> completed);
        void Clear();
    }

    public readonly struct CampQuestChanged
    {
        public CampQuestChanged(string message) { Message = message; }
        public string Message { get; }
    }
}
