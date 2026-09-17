using System;
using System.Collections.Generic;
using System.Linq;
using Haven.Framework.CampQuests;

namespace Haven.Hotfix.CampQuests
{
    public static class CampQuestRules
    {
        public static void ValidateCatalog(CampQuestCatalog catalog)
        {
            if (catalog == null || catalog.version != 1 || catalog.quests == null || catalog.quests.Length == 0)
                throw new ArgumentException("营地任务目录为空或版本不兼容。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quest in catalog.quests)
            {
                if (!ValidDefinition(quest) || !ids.Add(quest.id))
                    throw new ArgumentException("营地任务目录含无效或重复的配置 ID。");
            }
        }

        public static bool ValidDefinition(CampQuestDefinition quest)
        {
            return quest != null && !string.IsNullOrWhiteSpace(quest.id) &&
                !string.IsNullOrWhiteSpace(quest.title) && !string.IsNullOrWhiteSpace(quest.fallbackDialogue) &&
                (quest.itemId == "wood" || quest.itemId == "rock") && quest.rewardId == "bread" &&
                quest.quantity == (quest.itemId == "wood" ? (quest.large ? 5 : 3) : (quest.large ? 4 : 2)) &&
                quest.rewardQuantity == (quest.large ? 2 : 1) &&
                new[] { "night_fire", "no_fire", "few_walls", "always" }.Contains(quest.condition);
        }

        public static CampQuestDefinition[] Candidates(CampQuestCatalog catalog, CampQuestWorldState world,
            IEnumerable<string> completed, string preference)
        {
            var done = new HashSet<string>(completed ?? Array.Empty<string>(), StringComparer.Ordinal);
            return catalog.quests.Where(q => !done.Contains(q.itemId) &&
                    (preference != "easy" || !q.large) && Eligible(q, world))
                .OrderByDescending(q => preference == "more" && q.large)
                .ThenByDescending(q => q.priority)
                .ThenBy(q => q.large)
                .ThenBy(q => q.id, StringComparer.Ordinal).ToArray();
        }

        public static bool Eligible(CampQuestDefinition quest, CampQuestWorldState world)
        {
            switch (quest.condition)
            {
                case "night_fire": return world.hasFirepit && (world.hour >= 16f || world.hour < 6f);
                case "no_fire": return !world.hasFirepit;
                case "few_walls": return world.wallCount < 2;
                case "always": return true;
                default: return false;
            }
        }

        public static bool ValidProposal(CampQuestProposal proposal, CampQuestRequest request)
        {
            return proposal != null && proposal.requestId == request.requestId &&
                request.candidates.Any(q => q.id == proposal.candidateId) &&
                !string.IsNullOrWhiteSpace(proposal.dialogue) && proposal.dialogue.Length <= 200 &&
                !proposal.dialogue.Contains("<") && !proposal.dialogue.Contains(">") &&
                (proposal.source == "deepseek" || proposal.source == "local-fallback");
        }
    }
}
