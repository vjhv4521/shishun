using System;
using Haven.Framework.Services;
using Haven.Networking;
using NUnit.Framework;
using UnityEngine;

namespace Haven.Framework.Tests
{
    public sealed class AuthoritativeGameplaySessionTests
    {
        [Test]
        public void Collect_IsServerAuthoritativeAndRejectsDuplicateRequest()
        {
            var session = new AuthoritativeGameplaySession();
            var position = new Vector3(3f, 1f, 0f);

            var first = session.Collect(10, "collect-1", "wood_01", position, 1f);
            var duplicate = session.Collect(10, "collect-1", "wood_01", position, 2f);

            Assert.IsTrue(first.Succeeded, first.Error?.ToString());
            Assert.IsFalse(duplicate.Succeeded);
            Assert.AreEqual(GameplayErrorCodes.DuplicateRequest, duplicate.Error.Code);
            Assert.AreEqual(1, Quantity(session.SnapshotFor(10, 2f), 10, GameplayItemIds.Wood));
            Assert.AreEqual(7, FindNode(session.SnapshotFor(10, 2f), "wood_01").Remaining);
        }

        [Test]
        public void Collect_UsesServerPlayerPositionForRangeCheck()
        {
            var session = new AuthoritativeGameplaySession();

            var result = session.Collect(10, "too-far", "wood_01", new Vector3(20f, 1f, 20f), 1f);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(GameplayErrorCodes.OutOfRange, result.Error.Code);
            Assert.AreEqual(0, Quantity(session.SnapshotFor(10, 1f), 10, GameplayItemIds.Wood));
        }

        [Test]
        public void Build_DeductsServerInventoryAndRejectsDuplicate()
        {
            var session = new AuthoritativeGameplaySession();
            Gather(session, 10, GameplayItemIds.Wood, "wood_01", new Vector3(3f, 1f, 0f), 2, 1f);
            Gather(session, 10, GameplayItemIds.Stone, "stone_01", new Vector3(-3f, 1f, 0f), 1, 2f);

            var position = new Vector3(-1f, 1f, 0f);
            var first = session.Build(10, "build-1", GameplayBuildingTypes.Firepit, position, 0f,
                new Vector3(-3f, 1f, 0f), 3f);
            var duplicate = session.Build(10, "build-1", GameplayBuildingTypes.Firepit, position + Vector3.right, 0f,
                new Vector3(-3f, 1f, 0f), 4f);
            var overlapping = session.Build(10, "build-2", GameplayBuildingTypes.Firepit, position, 0f,
                new Vector3(-3f, 1f, 0f), 5f);

            Assert.IsTrue(first.Succeeded, first.Error?.ToString());
            Assert.IsFalse(duplicate.Succeeded);
            Assert.AreEqual(GameplayErrorCodes.DuplicateRequest, duplicate.Error.Code);
            Assert.IsFalse(overlapping.Succeeded);
            Assert.AreEqual(GameplayErrorCodes.InvalidPlacement, overlapping.Error.Code);
            var snapshot = session.SnapshotFor(10, 4f);
            Assert.AreEqual(1, snapshot.Buildings.Length);
            Assert.AreEqual(0, Quantity(snapshot, 10, GameplayItemIds.Wood));
            Assert.AreEqual(0, Quantity(snapshot, 10, GameplayItemIds.Stone));
        }

        [Test]
        public void QuestReward_CanOnlyBeClaimedOnce()
        {
            var session = new AuthoritativeGameplaySession();
            Gather(session, 10, GameplayItemIds.Wood, "wood_01", new Vector3(3f, 1f, 0f), 3, 1f);
            Gather(session, 20, GameplayItemIds.Stone, "stone_01", new Vector3(-3f, 1f, 0f), 2, 3f);
            Assert.IsTrue(session.Contribute(10, "wood-part-1", GameplayItemIds.Wood, 1, Vector3.zero, 4f).Succeeded);
            Assert.IsTrue(session.Contribute(10, "wood-part-2", GameplayItemIds.Wood, 2, Vector3.zero, 4.5f).Succeeded);
            Assert.IsTrue(session.Contribute(20, "stone-all", GameplayItemIds.Stone, 2, Vector3.zero, 5f).Succeeded);

            var first = session.ClaimReward(10, "claim-1", Vector3.zero, 8f);
            var second = session.ClaimReward(10, "claim-2", Vector3.zero, 9f);
            var teammate = session.ClaimReward(20, "claim-teammate", Vector3.zero, 9f);

            Assert.IsTrue(first.Succeeded, first.Error?.ToString());
            Assert.IsFalse(second.Succeeded);
            Assert.AreEqual(GameplayErrorCodes.RewardClaimed, second.Error.Code);
            Assert.IsTrue(teammate.Succeeded, teammate.Error?.ToString());
            Assert.AreEqual(10, Quantity(session.SnapshotFor(10, 9f), 10, GameplayItemIds.Coin));
            Assert.AreEqual(10, Quantity(session.SnapshotFor(20, 9f), 20, GameplayItemIds.Coin));
        }

        [Test]
        public void LateJoinSnapshot_ContainsExistingWorldAndFreshPlayerState()
        {
            var session = new AuthoritativeGameplaySession();
            Gather(session, 10, GameplayItemIds.Wood, "wood_01", new Vector3(3f, 1f, 0f), 2, 1f);
            Gather(session, 10, GameplayItemIds.Stone, "stone_01", new Vector3(-3f, 1f, 0f), 1, 2f);
            Assert.IsTrue(session.Build(10, "existing-building", GameplayBuildingTypes.Firepit,
                new Vector3(-1f, 1f, 0f), 0f, new Vector3(-3f, 1f, 0f), 3f).Succeeded);

            session.AddOrUpdatePlayer(20, Vector3.zero);
            var snapshot = session.SnapshotFor(20, 4f);

            Assert.AreEqual(20, snapshot.LocalPlayerId);
            Assert.AreEqual(2, snapshot.Players.Length);
            Assert.AreEqual(1, snapshot.Buildings.Length);
            Assert.AreEqual(0, Quantity(snapshot, 20, GameplayItemIds.Wood));
            Assert.AreEqual(6, FindNode(snapshot, "wood_01").Remaining);
            Assert.AreEqual(0, snapshot.Players[0].Inventory.Length, "Other players' inventories must remain private.");
        }

        [Test]
        public void CraftAxe_DeductsMaterialsAndRejectsReplay()
        {
            var session = new AuthoritativeGameplaySession();
            Gather(session, 10, GameplayItemIds.Wood, "wood_01", new Vector3(3f, 1f, 0f), 2, 1f);
            Gather(session, 10, GameplayItemIds.Stone, "stone_01", new Vector3(-3f, 1f, 0f), 1, 2f);

            var first = session.CraftAxe(10, "axe-1", Vector3.zero, 3f);
            var replay = session.CraftAxe(10, "axe-1", Vector3.zero, 4f);

            Assert.IsTrue(first.Succeeded, first.Error?.ToString());
            Assert.AreEqual(GameplayErrorCodes.DuplicateRequest, replay.Error.Code);
            Assert.AreEqual(1, Quantity(session.SnapshotFor(10, 4f), 10, GameplayItemIds.Axe));
            Assert.AreEqual(0, Quantity(session.SnapshotFor(10, 4f), 10, GameplayItemIds.Wood));
        }

        [Test]
        public void SharedQuest_RequiresTwoContributorsAndProtectsPrivateInventory()
        {
            var session = new AuthoritativeGameplaySession();
            Gather(session, 10, GameplayItemIds.Wood, "wood_01", new Vector3(3f, 1f, 0f), 3, 1f);
            Gather(session, 10, GameplayItemIds.Stone, "stone_01", new Vector3(-3f, 1f, 0f), 2, 3f);
            Assert.IsTrue(session.Contribute(10, "wood-all", GameplayItemIds.Wood, 3, Vector3.zero, 5f).Succeeded);
            Assert.IsTrue(session.Contribute(10, "stone-part", GameplayItemIds.Stone, 1, Vector3.zero, 5.5f).Succeeded);
            Assert.IsFalse(session.SnapshotFor(10, 6f).SharedQuest.IsComplete);
            Assert.AreEqual(GameplayErrorCodes.QuestIncomplete,
                session.ClaimReward(10, "claim-early", Vector3.zero, 6f).Error.Code);

            Gather(session, 20, GameplayItemIds.Stone, "stone_01", new Vector3(-3f, 1f, 0f), 1, 7f);
            Assert.IsTrue(session.Contribute(20, "last-stone", GameplayItemIds.Stone, 1, Vector3.zero, 8f).Succeeded);
            var otherView = session.SnapshotFor(20, 8f);
            Assert.IsTrue(otherView.SharedQuest.IsComplete);
            Assert.AreEqual(0, otherView.Players[0].Inventory.Length);
            Assert.AreEqual(0, Quantity(otherView, 20, GameplayItemIds.Stone));
        }

        [Test]
        public void Enemy_DeathAndRespawnAreServerControlled()
        {
            var session = new AuthoritativeGameplaySession();
            var position = new Vector3(0f, 1f, 5f);
            for (var index = 0; index < 4; index++)
                Assert.IsTrue(session.Attack(10, "hit-" + index, "raider_01", position, 1f + index * 0.5f).Succeeded);

            Assert.IsFalse(session.SnapshotFor(10, 3f).Enemies[0].IsAlive);
            Assert.IsFalse(session.Step(8.9f, 0.1f));
            Assert.IsTrue(session.Step(10.5f, 0.1f));
            Assert.IsTrue(session.SnapshotFor(10, 10.5f).Enemies[0].IsAlive);
            Assert.AreEqual(100, session.SnapshotFor(10, 10.5f).Enemies[0].Health);
        }

        [Test]
        public void RemovePlayer_RemovesItFromFutureLateJoinSnapshots()
        {
            var session = new AuthoritativeGameplaySession();
            session.AddOrUpdatePlayer(10, Vector3.zero);
            session.AddOrUpdatePlayer(20, Vector3.right);

            Assert.IsTrue(session.RemovePlayer(10));

            var snapshot = session.SnapshotFor(20, 1f);
            Assert.AreEqual(1, snapshot.Players.Length);
            Assert.AreEqual(20, snapshot.Players[0].PlayerId);
        }

        private static void Gather(
            AuthoritativeGameplaySession session,
            int playerId,
            string itemId,
            string nodeId,
            Vector3 position,
            int count,
            float startTime)
        {
            for (var index = 0; index < count; index++)
            {
                var result = session.Collect(playerId, $"{itemId}-{startTime}-{index}", nodeId, position,
                    startTime + index * 0.5f);
                Assert.IsTrue(result.Succeeded, result.Error?.ToString());
            }
        }

        private static int Quantity(GameplaySnapshot snapshot, int playerId, string itemId)
        {
            foreach (var player in snapshot.Players ?? Array.Empty<GameplayPlayerSnapshot>())
            {
                if (player.PlayerId != playerId)
                    continue;
                foreach (var item in player.Inventory ?? Array.Empty<GameplayInventoryItemSnapshot>())
                {
                    if (item.ItemId == itemId)
                        return item.Quantity;
                }
            }
            return 0;
        }

        private static GameplayResourceNodeSnapshot FindNode(GameplaySnapshot snapshot, string nodeId)
        {
            foreach (var node in snapshot.ResourceNodes ?? Array.Empty<GameplayResourceNodeSnapshot>())
            {
                if (node.NodeId == nodeId)
                    return node;
            }
            Assert.Fail("Missing resource node: " + nodeId);
            return default;
        }
    }
}
