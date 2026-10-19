namespace EvilTwinsUpdater.Core;

// One consumer runs players; UI actions may pause/resume or reorder only the waiting players.
public sealed class RaidSimulationQueue
{
    private readonly object gate = new();
    private readonly List<string> pending;
    private string? current;
    private bool pauseRequested;
    private TaskCompletionSource resumed = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public RaidSimulationQueue(IEnumerable<string> players) => pending = players.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public string? Current { get { lock (gate) return current; } }
    public IReadOnlyList<string> Pending { get { lock (gate) return pending.ToArray(); } }
    public bool PauseRequested { get { lock (gate) return pauseRequested; } }
    public bool IsPaused { get { lock (gate) return pauseRequested && current == null && pending.Count > 0; } }
    public IReadOnlyList<string> Unfinished { get { lock (gate) return current == null ? pending.ToArray() : new[] { current }.Concat(pending).ToArray(); } }
    public void PauseAfterCurrent() { lock (gate) pauseRequested = true; }
    public void Resume()
    {
        TaskCompletionSource signal;
        lock (gate) { pauseRequested = false; signal = resumed; resumed = NewSignal(); }
        signal.TrySetResult();
    }
    public bool MoveNext(string player)
    {
        lock (gate) {
            var index = pending.FindIndex(value => value.Equals(player, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            var value = pending[index]; pending.RemoveAt(index); pending.Insert(0, value); return true;
        }
    }
    public void FinishCurrent() { lock (gate) current = null; }
    public async Task<string?> NextAsync(CancellationToken cancellation = default)
    {
        while (true) {
            cancellation.ThrowIfCancellationRequested();
            Task wait;
            lock (gate) {
                if (current != null) throw new InvalidOperationException("Finish the current player before starting another.");
                if (pending.Count == 0) return null;
                if (!pauseRequested) { current = pending[0]; pending.RemoveAt(0); return current; }
                wait = resumed.Task;
            }
            await wait.WaitAsync(cancellation);
        }
    }
}
