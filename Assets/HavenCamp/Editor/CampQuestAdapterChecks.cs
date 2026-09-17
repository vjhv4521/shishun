using System;
using System.IO;
using Haven.Framework.CampQuests;
using SurvivalEngine;
using UnityEditor;
using UnityEngine;

namespace Haven.Camp.Editor
{
    // These checks live in the default Editor assembly so they exercise the real legacy inventory types.
    public static class CampQuestAdapterChecks
    {
        [MenuItem("Haven/Camp Quests/3. Run Adapter Checks")]
        public static void Run()
        {
            ItemData.Load("Items");
            var quest = new CampQuestDefinition { itemId = "wood", quantity = 3, rewardId = "bread", rewardQuantity = 1 };
            var main = Inventory("main", 1, "wood", 1);
            var bag = Inventory("bag", 1, "wood", 2);
            Check(CampQuestWorldBridge.PlanExchange(main, bag, quest).Succeeded, "cross-inventory deduction");
            Check(main.CountItem("wood") + bag.CountItem("wood") == 0 && main.CountItem("bread") + bag.CountItem("bread") == 1,
                "freed slot receives reward");

            main = Inventory("main", 1, "wood", 5);
            var copy = CampQuestWorldBridge.Copy(main);
            Check(!CampQuestWorldBridge.PlanExchange(copy, null, quest).Succeeded, "full inventory rejected");
            Check(main.CountItem("wood") == 5 && main.CountItem("bread") == 0, "planning never mutates live inventory");

            main = Inventory("main", 2, "wood", 8);
            main.AddItemAt("bread", 1, ItemData.Get("bread").inventory_max - 1, 100, "bread-main");
            bag = Inventory("bag", 1, "bread", ItemData.Get("bread").inventory_max - 1);
            quest.quantity = 5;
            quest.rewardQuantity = 2;
            Check(CampQuestWorldBridge.PlanExchange(main, bag, quest).Succeeded &&
                main.CountItem("bread") == ItemData.Get("bread").inventory_max && bag.CountItem("bread") == ItemData.Get("bread").inventory_max,
                "split reward across carried inventories");

            var filename = "HavenCamp_AtomicCheck_" + Guid.NewGuid().ToString("N") + ".test";
            var path = Path.Combine(Application.persistentDataPath, filename);
            try
            {
                Check(SaveTool.TrySaveFile(filename, "before", out _), "first atomic save");
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Check(!SaveTool.TrySaveFile(filename, "blocked", out _), "locked save reports failure");
                Check(SaveTool.LoadFile<string>(filename) == "before", "failed replacement preserves last good save");
                Check(SaveTool.TrySaveFile(filename, "after", out _) && SaveTool.LoadFile<string>(filename) == "after", "replacement succeeds after retry");
                Check(SaveTool.LoadFile<string>(filename + ".bak") == "before", "previous save retained as backup");
            }
            finally
            {
                // Delete only this invocation's randomly named test artifacts.
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
            Debug.Log("[CAMP_ADAPTER_PASS] 10 real inventory and atomic save checks.");
        }

        private static InventoryData Inventory(string id, int size, string item, int count)
        {
            var inventory = new InventoryData(InventoryType.Inventory, id) { size = size };
            inventory.AddItemAt(item, 0, count, 100, Guid.NewGuid().ToString("N"));
            return inventory;
        }

        private static void Check(bool result, string step)
        {
            if (!result) throw new InvalidOperationException("Camp adapter check failed: " + step);
            Debug.Log("[CAMP_ADAPTER_OK] " + step);
        }
    }
}
