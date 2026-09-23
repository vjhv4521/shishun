using System;
using System.Collections;
using System.Collections.Generic;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    internal sealed class SurvivalCoopGameplayFacade : ICoopGameplayService
    {
        private readonly ISurvivalSessionService _survival;

        public SurvivalCoopGameplayFacade(ISurvivalSessionService survival)
        {
            _survival = survival ?? throw new ArgumentNullException(nameof(survival));
        }

        public GameplaySnapshot Current => Convert(_survival.Current);

        public IEnumerator Refresh(Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            FrameworkResult<SurvivalSnapshot> result = default;
            yield return _survival.Refresh(value => result = value);
            completed?.Invoke(result.Succeeded
                ? FrameworkResult<GameplaySnapshot>.Success(Convert(result.Value))
                : FrameworkResult<GameplaySnapshot>.Failure(result.Error));
        }

        public IEnumerator Collect(string resourceNodeId, Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            var command = SurvivalCommand.Create(SurvivalCommandType.Interact);
            command.TargetUid = resourceNodeId;
            yield return SubmitAndRefresh(command, completed);
        }

        public IEnumerator CraftAxe(Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            var command = SurvivalCommand.Create(SurvivalCommandType.Craft);
            command.DataId = GameplayItemIds.Axe;
            yield return SubmitAndRefresh(command, completed);
        }

        public IEnumerator Contribute(string itemId, int quantity, Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            var command = SurvivalCommand.Create(SurvivalCommandType.QuestAction);
            command.DataId = itemId;
            command.Quantity = quantity;
            yield return SubmitAndRefresh(command, completed);
        }

        public IEnumerator Build(string buildingType, Vector3 requestedPosition, float yaw,
            Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            var command = SurvivalCommand.Create(SurvivalCommandType.Build);
            command.DataId = buildingType;
            command.Position = requestedPosition;
            command.Rotation = Quaternion.Euler(0f, yaw, 0f);
            yield return SubmitAndRefresh(command, completed);
        }

        public IEnumerator Attack(string enemyId, Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            var command = SurvivalCommand.Create(SurvivalCommandType.Attack);
            command.TargetUid = enemyId;
            yield return SubmitAndRefresh(command, completed);
        }

        public IEnumerator ClaimQuestReward(Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            var command = SurvivalCommand.Create(SurvivalCommandType.QuestAction);
            command.ActionId = "claim";
            yield return SubmitAndRefresh(command, completed);
        }

        private IEnumerator SubmitAndRefresh(SurvivalCommand command,
            Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            if (!_survival.Submit(command))
            {
                completed?.Invoke(FrameworkResult<GameplaySnapshot>.Failure(new FrameworkError(
                    GameplayErrorCodes.NotConnected, "生存会话尚未准备完成。", "SurvivalSession", true)));
                yield break;
            }
            yield return null;
            yield return Refresh(completed);
        }

        private static GameplaySnapshot Convert(SurvivalSnapshot snapshot)
        {
            if (!snapshot.HasState)
                return GameplaySnapshot.Empty;
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var slot in snapshot.PrivateInventory ?? Array.Empty<SurvivalInventorySlotSnapshot>())
            {
                if (string.IsNullOrEmpty(slot.ItemId))
                    continue;
                totals.TryGetValue(slot.ItemId, out var value);
                totals[slot.ItemId] = value + slot.Quantity;
            }
            var items = new GameplayInventoryItemSnapshot[totals.Count];
            var index = 0;
            foreach (var pair in totals)
                items[index++] = new GameplayInventoryItemSnapshot { ItemId = pair.Key, Quantity = pair.Value };

            var players = new GameplayPlayerSnapshot[snapshot.Players?.Length ?? 0];
            for (var playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                var source = snapshot.Players[playerIndex];
                players[playerIndex] = new GameplayPlayerSnapshot
                {
                    PlayerId = source.PlayerId,
                    Position = source.Position,
                    Inventory = source.PlayerId == snapshot.LocalPlayerId ? items : Array.Empty<GameplayInventoryItemSnapshot>()
                };
            }
            return new GameplaySnapshot
            {
                Revision = snapshot.Revision,
                LocalPlayerId = snapshot.LocalPlayerId,
                Players = players,
                ResourceNodes = Array.Empty<GameplayResourceNodeSnapshot>(),
                Buildings = Array.Empty<GameplayBuildingSnapshot>(),
                Enemies = Array.Empty<GameplayEnemySnapshot>()
            };
        }
    }
}
