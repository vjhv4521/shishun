using System;
using System.Collections.Generic;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using SurvivalEngine;
using UnityEngine;

namespace Haven.Camp
{
    public sealed class CampQuestWorldBridge : ICampQuestWorldBridge, IDisposable
    {
        public const string SaveKey = "haven.campQuests.v1";
        private readonly Transform _steward;
        private PlayerData _observedSave;
        private string _sessionId;
        private bool _disposed;
        private bool _committing;

        public CampQuestWorldBridge(Transform steward) { _steward = steward; }
        public bool IsReady => !_disposed && _steward && TheGame.Get() && PlayerCharacter.GetFirst() &&
            PlayerCharacter.GetFirst().Inventory;

        public CampQuestWorldState Capture()
        {
            if (!IsReady) throw new InvalidOperationException("Camp world is not ready.");
            var data = PlayerData.Get();
            if (!ReferenceEquals(data, _observedSave))
            {
                _observedSave = data;
                _sessionId = Guid.NewGuid().ToString("N");
            }
            var player = PlayerCharacter.GetFirst();
            var inventory = player.Inventory;
            var state = new CampQuestWorldState
            {
                sessionId = _sessionId, day = Math.Max(1, data.day), hour = Mathf.Repeat(data.day_time, 24f),
                wood = Count(inventory, "wood"), rock = Count(inventory, "rock"), bread = Count(inventory, "bread"),
                alive = !player.IsDead(), distance = Vector3.Distance(player.transform.position, _steward.position)
            };
            foreach (var construction in Construction.GetAll())
            {
                if (!construction || !construction.data || !construction.IsBuilt() ||
                    Vector3.SqrMagnitude(construction.transform.position - _steward.position) > 625f) continue;
                if (construction.data.id == "firepit") state.hasFirepit = true;
                if (construction.data.id == "wall_wood") state.wallCount++;
            }
            return state;
        }

        public string ReadSave()
        {
            return PlayerData.Get().unique_strings.TryGetValue(SaveKey, out var json) ? json : null;
        }

        public FrameworkResult Commit(string sessionId, string saveJson, CampQuestDefinition exchange = null)
        {
            if (!IsReady || _committing) return Failure("NOT_READY", "当前不能保存营地委托。");
            var state = Capture();
            if (state.sessionId != sessionId || !state.alive || state.distance > 3f)
                return Failure("STALE", "请在当前存档中走到管事身边再试。");
            var data = PlayerData.Get();
            var inventory = PlayerCharacter.GetFirst().Inventory;
            var main = inventory.InventoryData;
            var bag = inventory.BagData;
            var mainBefore = main.items;
            var bagBefore = bag?.items;
            var hadSave = data.unique_strings.TryGetValue(SaveKey, out var previousSave);
            _committing = true;
            var succeeded = false;
            try
            {
                if (exchange != null)
                {
                    var plannedMain = Copy(main);
                    var plannedBag = bag == null ? null : Copy(bag);
                    var planned = PlanExchange(plannedMain, plannedBag, exchange);
                    if (!planned.Succeeded) return planned;
                    main.items = plannedMain.items;
                    if (bag != null) bag.items = plannedBag.items;
                }
                data.unique_strings[SaveKey] = saveJson;
                if (!TheGame.Get().Save(data.filename))
                    return Failure("SAVE_FAILED", "存档失败，本次材料和奖励未发生变化，请检查磁盘空间后重试。");
                succeeded = true;
                return FrameworkResult.Success();
            }
            catch (Exception exception)
            {
                GameLog.Error("CampWorld", "Camp quest transaction failed.", "CAMP_COMMIT_FAILED", exception);
                return Failure("SAVE_FAILED", "交付未完成，已恢复材料和奖励。");
            }
            finally
            {
                if (!succeeded)
                {
                    main.items = mainBefore;
                    if (bag != null) bag.items = bagBefore;
                    if (hadSave) data.unique_strings[SaveKey] = previousSave;
                    else data.unique_strings.Remove(SaveKey);
                }
                _committing = false;
            }
        }

        public static FrameworkResult PlanExchange(InventoryData main, InventoryData bag, CampQuestDefinition quest)
        {
            if (main == null || quest == null || quest.quantity <= 0 || quest.rewardQuantity <= 0 || quest.rewardQuantity > 2 ||
                (quest.itemId != "wood" && quest.itemId != "rock") || quest.rewardId != "bread")
                return Failure("ITEM_CONFIG", "任务物品配置无效。");
            var reward = ItemData.Get(quest.rewardId);
            if (reward == null || ItemData.Get(quest.itemId) == null) return Failure("ITEM_CONFIG", "任务物品配置缺失。");
            if (main.CountItem(quest.itemId) + (bag?.CountItem(quest.itemId) ?? 0) < quest.quantity)
                return Failure("MATERIALS", "随身物资不足。");
            var fromMain = Math.Min(main.CountItem(quest.itemId), quest.quantity);
            main.RemoveItem(quest.itemId, fromMain);
            bag?.RemoveItem(quest.itemId, quest.quantity - fromMain);
            // Plan on copies: split rewards across partially filled stacks and the carried bag.
            for (var index = 0; index < quest.rewardQuantity; index++)
            {
                var target = main.CanTakeItem(reward.id, 1) ? main : bag != null && bag.CanTakeItem(reward.id, 1) ? bag : null;
                if (target == null || target.AddItem(reward.id, 1, reward.durability, Guid.NewGuid().ToString("N")) < 0)
                    return Failure("CAPACITY", "交付后仍没有空间放入口粮，请先整理背包。");
            }
            return FrameworkResult.Success();
        }

        public static InventoryData Copy(InventoryData source)
        {
            var copy = new InventoryData(source.type, source.uid) { size = source.size };
            foreach (var pair in source.items)
            {
                var value = pair.Value;
                copy.items[pair.Key] = value == null ? null : new InventoryItemData(value.item_id, value.quantity, value.durability, value.uid);
            }
            return copy;
        }

        public void Dispose() { _disposed = true; }
        private static int Count(PlayerCharacterInventory inventory, string item) =>
            inventory.InventoryData.CountItem(item) + (inventory.BagData?.CountItem(item) ?? 0);
        private static FrameworkResult Failure(string code, string message) => FrameworkResult.Failure(new FrameworkError("CAMP_" + code, message, "CampWorld", true));
    }
}
