namespace ConnectivityKit
{
    /// <summary>Whether the game server answered the last probe. Unknown until the first probe.</summary>
    public enum ConnectivityState
    {
        Unknown,
        Online,
        Offline
    }
}
