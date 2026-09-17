using System;
using System.Collections;
using System.IO;
using System.Linq;
using Haven.Framework.Bootstrap;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using SurvivalEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haven.Camp
{
    // Runs only with explicit smoke-test arguments, against its own save slot.
    public sealed class CampQuestSmokeRunner : MonoBehaviour
    {
        private const string TestSave = "HavenCamp_AutomatedSmoke";
        private static bool _enabled;
        private static bool _reload;
        private static string _previousLastSave;
        private static bool _hadLastSave;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PrepareTestSave()
        {
            var args = Environment.GetCommandLineArgs();
            _reload = args.Contains("-campQuestReloadSmoke");
            _enabled = _reload || args.Contains("-campQuestSmoke");
            if (!_enabled) return;
            _hadLastSave = PlayerPrefs.HasKey(PlayerData.last_save_id);
            _previousLastSave = PlayerPrefs.GetString(PlayerData.last_save_id);
            if (_reload) PlayerData.Load(TestSave);
            else PlayerData.NewGame(TestSave);
            PlayerData.SetLastSave(TestSave);
            Application.quitting += RestoreLastSave;
        }

        private IEnumerator Start()
        {
            if (!_enabled) yield break;
            yield return SafeCoroutine.Run(Run(), exception =>
            {
                Debug.LogError("[CAMP_SMOKE_FAIL] " + exception);
                RestoreLastSave();
                Quit(1);
            });
        }

        private IEnumerator Run()
        {
            var deadline = Time.realtimeSinceStartup + 90f;
            while ((!GameBootstrap.Instance || GameBootstrap.Instance.State != BootstrapState.Running) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Check(GameBootstrap.Instance && GameBootstrap.Instance.State == BootstrapState.Running, "framework startup");
            var service = GameBootstrap.Instance.Context.Services.Resolve<ICampQuestService>();
            var player = PlayerCharacter.GetFirst();
            var npc = FindAnyObjectByType<CampQuestSteward>();
            var panel = FindAnyObjectByType<CampQuestPanel>();
            Check(player && npc && panel, "scene components");
            player.Teleport(npc.transform.position + new Vector3(0, 0, -1.8f));
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[CAMP_SMOKE] interaction distance=" + Vector3.Distance(player.transform.position, npc.transform.position) +
                " alive=" + !player.IsDead() + " selectable=" + npc.GetComponent<Selectable>().enabled +
                " world=" + JsonUtility.ToJson(service.GetView().World));
            npc.GetComponent<Selectable>().Use(player, npc.transform.position);
            Check(panel.IsOpen, "Selectable steward interaction opens panel");
            panel.Open(); // Opening twice must not lose ownership of the pause state.
            yield return new WaitForSecondsRealtime(0.3f);
            var chat = GameBootstrap.Instance.Context.Services.Resolve<ICampNpcChatService>();
            if (_reload) Check(chat.GetHistory().Length == 0, "chat history is session-only after reload");
            panel.OpenChat();
            Check(panel.IsChatOpen, "chat page opens from steward panel");
            FrameworkResult<CampNpcChatResponse> chatReply = default;
            yield return chat.Send("今晚营地要准备什么？", value => chatReply = value);
            Check(chatReply.Succeeded && !string.IsNullOrWhiteSpace(chatReply.Value.reply), "steward replies or falls back locally");
            Check(chat.GetHistory().Length == 2, "conversation appears in session history");
            Debug.Log("[CAMP_SMOKE] chat source=" + chatReply.Value.source);
            panel.ShowQuests();
            Check(!panel.IsChatOpen && panel.IsOpen, "return to quest page");

            if (_reload)
            {
                var restored = service.GetView();
                Check(restored.CompletedToday == 2 && restored.Active == null, "completed quests restored");
                Check(restored.ContributedWood == 3 && restored.ContributedRock == 2, "contributions restored");
                FrameworkResult blocked = default;
                yield return service.Propose("easy", "", value => blocked = value);
                Check(!blocked.Succeeded && blocked.Error.Code == "CAMP_DAILY_LIMIT", "no duplicate reward after reload");
            }
            else
            {
                for (var index = 0; index < 2; index++)
                {
                    FrameworkResult generated = default;
                    yield return service.Propose("easy", "今天有什么能帮忙的？", value => generated = value);
                    Check(generated.Succeeded && service.GetView().Offer != null, "proposal " + index);
                    var offer = service.GetView().Offer;
                    Debug.Log("[CAMP_SMOKE] proposal source=" + offer.source + " candidate=" + offer.definition.id);
                    if (index == 0)
                    {
                        yield return new WaitForSecondsRealtime(0.3f);
                        ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath, "CampQuestProposal.png"));
                        yield return new WaitForSecondsRealtime(0.5f);
                    }
                    Check(service.Accept().Succeeded, "accept");
                    panel.Close();
                    while (service.GetView().Held < offer.definition.quantity)
                    {
                        var item = Item.GetAll().FirstOrDefault(candidate => candidate && candidate.data.id == offer.definition.itemId && candidate.name.StartsWith("Camp supply", StringComparison.Ordinal));
                        Check(item, "nearby supplies");
                        player.Teleport(item.transform.position + new Vector3(0.5f, 0, 0));
                        var before = service.GetView().Held;
                        player.Inventory.TakeItem(item);
                        var collectDeadline = Time.realtimeSinceStartup + 10f;
                        while (service.GetView().Held <= before && Time.realtimeSinceStartup < collectDeadline) yield return null;
                        Check(service.GetView().Held > before, "actual inventory pickup");
                        yield return new WaitForSecondsRealtime(0.2f);
                    }
                    player.Teleport(npc.transform.position + new Vector3(0, 0, -1.8f));
                    panel.Open();
                    var beforeDelivery = service.GetView();
                    if (index == 0)
                    {
                        var savePath = Path.Combine(Application.persistentDataPath, TestSave + PlayerData.extension);
                        using (var locked = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            Check(!service.Deliver().Succeeded, "locked disk save rejects transaction (expected save error)");
                        var rolledBack = service.GetView();
                        Check(rolledBack.Active != null && rolledBack.World.wood == beforeDelivery.World.wood &&
                            rolledBack.World.rock == beforeDelivery.World.rock && rolledBack.World.bread == beforeDelivery.World.bread,
                            "save failure rolls back inventory and active quest");
                    }
                    Check(service.Deliver().Succeeded, "delivery transaction");
                    var afterDelivery = service.GetView();
                    var materialBefore = offer.definition.itemId == "wood" ? beforeDelivery.World.wood : beforeDelivery.World.rock;
                    var materialAfter = offer.definition.itemId == "wood" ? afterDelivery.World.wood : afterDelivery.World.rock;
                    Check(materialBefore - materialAfter == offer.definition.quantity, "exact material consumption");
                    Check(afterDelivery.World.bread - beforeDelivery.World.bread == offer.definition.rewardQuantity, "exact reward");
                    Check(!service.Deliver().Succeeded, "duplicate delivery rejected");
                }
                Check(service.GetView().CompletedToday == 2, "two daily quests");
                Check(File.Exists(Path.Combine(Application.persistentDataPath, TestSave + PlayerData.extension)), "durable world save");
            }
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath, "CampQuestCompleted.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log("[CAMP_SMOKE_PASS] " + (_reload ? "reload" : "two deliveries") + " save=" + TestSave);
            if (!_reload)
            {
                _reload = true;
                Debug.Log("[CAMP_SMOKE] Checking in-process scene reload and service recreation.");
                PlayerData.Load(TestSave);
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                yield break;
            }
            RestoreLastSave();
            Quit(0);
        }

        private static void Check(bool success, string step)
        {
            if (!success) throw new InvalidOperationException("Camp smoke failed: " + step);
            Debug.Log("[CAMP_SMOKE_OK] " + step);
        }

        private static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }

        private static void RestoreLastSave()
        {
            if (!_enabled) return;
            if (_hadLastSave) PlayerPrefs.SetString(PlayerData.last_save_id, _previousLastSave);
            else PlayerPrefs.DeleteKey(PlayerData.last_save_id);
            PlayerPrefs.Save();
        }
    }
}
