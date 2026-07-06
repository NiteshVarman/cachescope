using CacheScope.Shared.Traffic;

namespace CacheScope.TrafficGenerator;

public interface ITrafficRunner
{
    /// <summary>Starts a run. Throws <see cref="InvalidOperationException"/> if one is already active.</summary>
    string Start(TrafficConfig config);

    /// <summary>Requests cancellation of the active run, if any.</summary>
    void Stop();

    TrafficRunStatus CurrentStatus();
}
