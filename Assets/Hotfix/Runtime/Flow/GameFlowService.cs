using System;
using Haven.Framework.Core;

namespace Haven.Hotfix.Flow
{
    public enum GameFlowState
    {
        Boot,
        MainMenu,
        Lobby,
        Room,
        LoadingGame,
        InGame,
        ReturningToLobby
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
        private readonly IEventBus _events;

        public GameFlowService(IEventBus events)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public GameFlowState Current { get; private set; } = GameFlowState.Boot;

        public bool CanTransition(GameFlowState target)
        {
            if (target == Current)
                return false;
            return Current switch
            {
                GameFlowState.Boot => target == GameFlowState.MainMenu,
                GameFlowState.MainMenu => target == GameFlowState.Lobby,
                GameFlowState.Lobby => target == GameFlowState.Room || target == GameFlowState.MainMenu,
                GameFlowState.Room => target == GameFlowState.LoadingGame || target == GameFlowState.Lobby || target == GameFlowState.MainMenu,
                GameFlowState.LoadingGame => target == GameFlowState.InGame || target == GameFlowState.Room ||
                                             target == GameFlowState.Lobby || target == GameFlowState.MainMenu,
                GameFlowState.InGame => target == GameFlowState.ReturningToLobby || target == GameFlowState.MainMenu,
                GameFlowState.ReturningToLobby => target == GameFlowState.Lobby || target == GameFlowState.InGame ||
                                                  target == GameFlowState.MainMenu,
                _ => false
            };
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
