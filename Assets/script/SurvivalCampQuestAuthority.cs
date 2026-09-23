using System;
using System.Linq;
using Haven.Framework.Bootstrap;
using Haven.Framework.CampQuests;
using Haven.Framework.Services;
using Haven.Camp;
using SurvivalEngine;
using UnityEngine;

namespace Haven.Gameplay
{
    // The client's quest JSON is a proposal, never an authoritative save or reward instruction.
    internal static class SurvivalCampQuestAuthority
    {
        public static bool Execute(PlayerCharacter player, SurvivalCommand command,
            out string errorCode, out string message)
        {
            errorCode = GameplayErrorCodes.InvalidRequest;
            message = "营地委托请求无效。";
            var steward = UnityEngine.Object.FindAnyObjectByType<CampQuestSteward>();
            if (!steward || !player || player.IsDead() ||
                Vector3.Distance(player.transform.position, steward.transform.position) > 3f)
            {
                errorCode = GameplayErrorCodes.OutOfRange;
                message = "请走到管事身边再操作委托。";
                return false;
            }

            var context = GameBootstrap.Instance?.Context;
            if (context == null || !context.Services.TryResolve<CampQuestConfiguration>(out var configuration) ||
                string.IsNullOrWhiteSpace(configuration.CatalogJson))
            {
                errorCode = GameplayErrorCodes.NotInGame;
                message = "营地委托配置尚未准备好。";
                return false;
            }

            var catalog = JsonUtility.FromJson<CampQuestCatalog>(configuration.CatalogJson);
            if (catalog?.quests == null || catalog.version != 1)
                return false;
            var data = PlayerData.Get();
            if (data == null)
                return false;

            data.unique_strings.TryGetValue(CampQuestWorldBridge.SaveKey, out var previousJson);
            CampQuestSave previous;
            try
            {
                previous = string.IsNullOrWhiteSpace(previousJson)
                    ? new CampQuestSave() : Read(previousJson);
            }
            catch (Exception)
            {
                errorCode = GameplayErrorCodes.InvalidRequest;
                message = "营地委托状态损坏，未扣除物资。";
                return false;
            }
            if (previous == null || previous.version != 1 || previous.completedItems == null ||
                previous.completedItems.Distinct().Count() != previous.completedItems.Length ||
                previous.completedItems.Any(item => item != "wood" && item != "rock") ||
                (previous.active != null && !IsCanonical(previous.active.definition, catalog)) ||
                (previous.offer != null && !IsCanonical(previous.offer.definition, catalog)))
                return false;
            if (previous.completionDay != data.day)
            {
                previous.completionDay = data.day;
                previous.completedItems = Array.Empty<string>();
            }

            if (command.ActionId == "save")
            {
                if (string.IsNullOrWhiteSpace(command.TargetUid) || command.TargetUid.Length > 8192)
                    return false;
                CampQuestSave proposed;
                try { proposed = Read(command.TargetUid); }
                catch (Exception) { return false; }
                if (!ValidSaveTransition(previous, proposed, catalog, data, steward.transform))
                    return false;
                data.unique_strings[CampQuestWorldBridge.SaveKey] = JsonUtility.ToJson(proposed);
                errorCode = string.Empty;
                message = "营地委托已由房主确认。";
                return true;
            }

            if (command.ActionId != "deliver" || previous.active?.definition == null)
                return false;
            var definition = previous.active.definition;
            if (previous.completedItems.Contains(definition.itemId) ||
                command.DataId != definition.itemId || command.Quantity != definition.quantity ||
                command.SourceSlot != definition.rewardQuantity || command.TargetSlot != 1 ||
                !IsCanonical(definition, catalog))
                return false;

            var inventory = player.Inventory;
            if (!inventory)
                return false;
            var main = inventory.InventoryData;
            var bag = inventory.BagData;
            var mainCopy = CampQuestWorldBridge.Copy(main);
            var bagCopy = bag == null ? null : CampQuestWorldBridge.Copy(bag);
            var planned = CampQuestWorldBridge.PlanExchange(mainCopy, bagCopy, definition);
            if (!planned.Succeeded)
            {
                errorCode = GameplayErrorCodes.InsufficientItems;
                message = planned.Error.Message;
                return false;
            }

            // All validation is complete. The room's temporary data and inventory change together on the host.
            previous.completedItems = previous.completedItems.Concat(new[] { definition.itemId }).ToArray();
            previous.lastCompletedQuestId = previous.active.questId;
            previous.active = null;
            previous.hasActive = false;
            previous.offer = null;
            previous.hasOffer = false;
            if (definition.itemId == "wood") previous.contributedWood += definition.quantity;
            else previous.contributedRock += definition.quantity;
            main.items = mainCopy.items;
            if (bag != null) bag.items = bagCopy.items;
            data.unique_strings[CampQuestWorldBridge.SaveKey] = JsonUtility.ToJson(previous);
            errorCode = string.Empty;
            message = $"委托完成，获得面包 ×{definition.rewardQuantity}。";
            return true;
        }

        private static CampQuestSave Read(string json)
        {
            var save = JsonUtility.FromJson<CampQuestSave>(json);
            if (save != null)
            {
                if (!save.hasOffer) save.offer = null;
                if (!save.hasActive) save.active = null;
            }
            return save;
        }

        private static bool ValidSaveTransition(CampQuestSave old, CampQuestSave next,
            CampQuestCatalog catalog, PlayerData data, Transform steward)
        {
            if (next == null || next.version != 1 || next.completionDay != data.day ||
                next.completedItems == null || old.completedItems == null ||
                !next.completedItems.SequenceEqual(old.completedItems) ||
                next.contributedWood != old.contributedWood || next.contributedRock != old.contributedRock ||
                next.lastCompletedQuestId != old.lastCompletedQuestId)
                return false;

            if (old.active == null && old.offer == null && next.active == null && next.offer != null)
            {
                var offered = next.offer;
                return offered.offeredDay == data.day &&
                    !string.IsNullOrWhiteSpace(offered.questId) && offered.questId.Length <= 64 &&
                    !next.completedItems.Contains(offered.definition?.itemId) &&
                    ValidPresentation(offered) && IsCanonical(offered.definition, catalog) &&
                    IsEligible(offered.definition, data, steward);
            }
            if (old.offer != null && old.active == null && next.offer == null && next.active != null)
                return JsonUtility.ToJson(old.offer) == JsonUtility.ToJson(next.active) &&
                    IsEligible(old.offer.definition, data, steward);
            if ((old.offer != null || old.active != null) && next.offer == null && next.active == null)
                return true; // Dismiss an offer or abandon an active task; no material is exchanged.
            return false;
        }

        private static bool ValidPresentation(CampQuestRecord record)
        {
            return !string.IsNullOrWhiteSpace(record.dialogue) && record.dialogue.Length <= 200 &&
                !record.dialogue.Contains("<") && !record.dialogue.Contains(">") &&
                (record.source == "deepseek" || record.source == "local-fallback");
        }

        private static bool IsCanonical(CampQuestDefinition submitted, CampQuestCatalog catalog)
        {
            if (submitted == null || string.IsNullOrWhiteSpace(submitted.id))
                return false;
            var known = catalog.quests.FirstOrDefault(item => item.id == submitted.id);
            return known != null && known.itemId == submitted.itemId && known.quantity == submitted.quantity &&
                known.rewardId == submitted.rewardId && known.rewardQuantity == submitted.rewardQuantity &&
                known.condition == submitted.condition && known.large == submitted.large;
        }

        private static bool IsEligible(CampQuestDefinition definition, PlayerData data, Transform steward)
        {
            var hasFirepit = false;
            var walls = 0;
            foreach (var construction in Construction.GetAll())
            {
                if (!construction || !construction.data || !construction.IsBuilt() ||
                    Vector3.SqrMagnitude(construction.transform.position - steward.position) > 625f)
                    continue;
                if (construction.data.id == "firepit") hasFirepit = true;
                if (construction.data.id == "wall_wood") walls++;
            }
            switch (definition.condition)
            {
                case "night_fire": return hasFirepit && (data.day_time >= 16f || data.day_time < 6f);
                case "no_fire": return !hasFirepit;
                case "few_walls": return walls < 2;
                case "always": return true;
                default: return false;
            }
        }
    }
}
