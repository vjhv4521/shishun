namespace Haven.Networking
{
    public readonly struct RoomAdmissionDecision
    {
        public RoomAdmissionDecision(bool accepted, string errorCode, string errorMessage)
        {
            Accepted = accepted;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Accepted { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
    }

    public static class RoomAdmissionRules
    {
        public static RoomAdmissionDecision Evaluate(
            int clientProtocolVersion,
            int serverProtocolVersion,
            int connectionCountIncludingCandidate,
            int maximumPlayers)
        {
            if (clientProtocolVersion != serverProtocolVersion)
            {
                return new RoomAdmissionDecision(
                    false,
                    "NETWORK_PROTOCOL_MISMATCH",
                    $"Client protocol {clientProtocolVersion} does not match server protocol {serverProtocolVersion}.");
            }
            if (connectionCountIncludingCandidate > maximumPlayers)
            {
                return new RoomAdmissionDecision(
                    false,
                    "NETWORK_SERVER_FULL",
                    $"The room is full ({maximumPlayers} players maximum).");
            }
            return new RoomAdmissionDecision(true, string.Empty, string.Empty);
        }
    }
}
