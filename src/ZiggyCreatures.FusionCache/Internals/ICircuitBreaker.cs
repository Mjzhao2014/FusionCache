namespace ZiggyCreatures.Caching.Fusion.Internals;

internal interface ICircuitBreaker
{
    TimeSpan BreakDuration { get; }
    bool TryOpen(out bool isStateChanged);
    void Close(out bool isStateChanged);
    bool IsClosed(out bool isStateChanged);
}
