using System.Globalization;
using System.Text;

namespace EvilTwinsUpdater.Core;

// SimulationCraft's simple progress output is tab-separated and newline-terminated.
// Pipe reads can split a line anywhere, so retain only the unfinished line between reads.
public sealed class SimulationProgress(int totalRuns)
{
    private readonly StringBuilder pending = new();
    public int TotalRuns { get; } = Math.Clamp(totalRuns, 1, BagComparisons.MaximumCombinations + 1);
    public int CompletedRuns { get; private set; }
    public double Fraction { get; private set; }
    public bool HasProgress { get; private set; }

    public void Append(string chunk)
    {
        lock (pending)
        {
            foreach (var character in chunk)
            {
                if (character is '\r' or '\n')
                {
                    ReadLine(pending.ToString()); pending.Clear();
                }
                else if (pending.Length < 16_000) pending.Append(character);
            }
        }
    }

    private void ReadLine(string line)
    {
        var fields = line.Split('\t');
        if (fields.Length < 7 || fields[0] is not "Baseline" and not "Profileset") return;
        // Baseline has no phase name; profilesets include their generated gear-set key.
        var offset = fields[0] == "Profileset" ? 2 : 1;
        if (fields.Length < offset + 6
            || !int.TryParse(fields[offset], NumberStyles.None, CultureInfo.InvariantCulture, out var phase)
            || !int.TryParse(fields[offset + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var phases)
            || !int.TryParse(fields[offset + 2], NumberStyles.None, CultureInfo.InvariantCulture, out var done)
            || !int.TryParse(fields[offset + 3], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || phase < 1 || phase > TotalRuns || phases != TotalRuns || iterations < 1 || done < 0 || done > iterations) return;
        var complete = phase - 1 + (done == iterations ? 1 : 0);
        var fraction = (phase - 1 + (double)done / iterations) / TotalRuns;
        CompletedRuns = Math.Max(CompletedRuns, complete);
        Fraction = Math.Max(Fraction, fraction);
        HasProgress = true;
    }

    public TimeSpan? EstimateRemaining(TimeSpan elapsed)
    {
        if (!HasProgress || Fraction < 0.001 || elapsed.TotalSeconds < 1) return null;
        var seconds = elapsed.TotalSeconds * (1 - Fraction) / Fraction;
        return double.IsFinite(seconds) && seconds <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(Math.Max(0, seconds)) : null;
    }
}
