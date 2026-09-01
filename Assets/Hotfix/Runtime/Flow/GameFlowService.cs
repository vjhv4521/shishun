using System;
using System.Collections.Generic;
using Haven.Framework.Core;

namespace Haven.Hotfix.Flow
{
    public enum GameFlowState
    {
        Boot,
        MainMenu,
        Lobby,
        LoadingGame,
        InGame,
        ReturningToMenu
    }

    public readonly struct GameFlowStateChanged
    {
        public GameFlowStateChanged(GameFlowState previous, GameFlowState current)
        {
            Previous = previous;
            Current = current;
        }

        public GameFlowState Previous { get; }
        public GameFlowState Current { get; }
    }

    public interface IGameFlowService
    {
        GameFlowState Current { get; }
        bool CanTransition(GameFlowState target);
        bool TryTransition(GameFlowState target);
    }

    public sealed class GameFlowService : IGameFlowService
    {
        private static readonly Dictionary<GameFlowState, HashSet<GameFlowState>> AllowedTransitions =
            new Dictionary<GameFlowState, HashSet<GameFlowState>>
            {
                { GameFlowState.Boot, new HashSet<GameFlowState> { GameFlowState.MainMenu } },
                { GameFlowState.MainMenu, new HashSet<GameFlowState> { GameFlowState.Lobby } },
                { GameFlowState.Lobby, new HashSet<GameFlowState> { GameFlowState.LoadingGame, GameFlowState.MainMenu } },
                { GameFlowState.LoadingGame, new HashSet<GameFlowState> { GameFlowState.InGame, GameFlowState.Lobby } },
                { GameFlowState.InGame, new HashSet<GameFlowState> { GameFlowState.ReturningToMenu } },
                { GameFlowState.ReturningToMenu, new HashSet<GameFlowState> { GameFlowState.MainMenu } }
            };

        private readonly IEventBus _events;

        public GameFlowService(IEventBus events)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public GameFlowState Current { get; private set; } = GameFlowState.Boot;

        public bool CanTransition(GameFlowState target)
        {
            return target != Current && AllowedTransitions.TryGetValue(Current, out var targets) && targets.Contains(target);
        }

        public bool TryTransition(GameFlowState target)
        {
            if (!CanTransition(target))
            {
                GameLog.Warning("GameFlow", $"Rejected transition {Current} -> {target}.", "FLOW_INVALID_TRANSITION");
                return false;
            }

            var previous = Current;
            Current = target;
            _events.Publish(new GameFlowStateChanged(previous, target));
            GameLog.Info("GameFlow", $"State changed {previous} -> {target}.", "FLOW_CHANGED");
            return true;
        }
    }
}
