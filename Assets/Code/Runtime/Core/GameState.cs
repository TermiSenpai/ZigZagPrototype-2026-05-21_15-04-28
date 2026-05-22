namespace ZigZag.Runtime.Core
{
    /// <summary>
    /// High-level lifecycle state of a play session. Drives UI panel visibility,
    /// ball motion and tap routing inside <see cref="GameStateMachine"/>.
    /// </summary>
    public enum GameState
    {
        Menu,
        Playing,
        GameOver
    }
}
