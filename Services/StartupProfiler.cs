using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog;

namespace Boutique.Services;

public sealed class StartupProfiler : IDisposable
{
    private readonly Stopwatch _totalStopwatch = Stopwatch.StartNew();
    private readonly ConcurrentDictionary<string, (Stopwatch Stopwatch, string? Parent)> _activeOperations = new();
    private readonly ConcurrentBag<(string Operation, long ElapsedMs, string? Parent)> _completedOperations = new();
    private bool _disposed;

    public static StartupProfiler Instance { get; } = new();

    public IDisposable BeginOperation(string operationName, string? parent = null)
    {
        var sw = Stopwatch.StartNew();
        _activeOperations[operationName] = (sw, parent);
        Log.Debug("[Profiler] Starting: {Operation}", operationName);
        return new OperationScope(this, operationName);
    }

    public void EndOperation(string operationName)
    {
        if (!_activeOperations.TryRemove(operationName, out var entry))
        {
            return;
        }

        entry.Stopwatch.Stop();
        var elapsed = entry.Stopwatch.ElapsedMilliseconds;
        _completedOperations.Add((operationName, elapsed, entry.Parent));

        if (entry.Parent is not null)
        {
            Log.Information("[Profiler] {Operation} completed in {ElapsedMs}ms (parent: {Parent})", operationName, elapsed, entry.Parent);
        }
        else
        {
            Log.Information("[Profiler] {Operation} completed in {ElapsedMs}ms", operationName, elapsed);
        }
    }

    public void LogSummary()
    {
        _totalStopwatch.Stop();

        var operations = _completedOperations.ToList();
        if (operations.Count == 0)
        {
            return;
        }

        Log.Information("[Profiler] ═══════════════════════════════════════════════════════════════");
        Log.Information("[Profiler] STARTUP PROFILING SUMMARY");
        Log.Information("[Profiler] ═══════════════════════════════════════════════════════════════");
        Log.Information("[Profiler] Total startup time: {TotalMs}ms", _totalStopwatch.ElapsedMilliseconds);
        Log.Information("[Profiler] ───────────────────────────────────────────────────────────────");

        var topLevel = operations
            .Where(o => o.Parent is null)
            .OrderBy(o => operations.IndexOf(o))
            .ToList();

        foreach (var op in topLevel)
        {
            var children = operations.Where(o => o.Parent == op.Operation).ToList();
            if (children.Count > 0)
            {
                Log.Information("[Profiler] {Operation}: {ElapsedMs}ms", op.Operation, op.ElapsedMs);
                foreach (var child in children.OrderByDescending(c => c.ElapsedMs))
                {
                    Log.Information("[Profiler]   └─ {Operation}: {ElapsedMs}ms", child.Operation, child.ElapsedMs);
                }
            }
            else
            {
                Log.Information("[Profiler] {Operation}: {ElapsedMs}ms", op.Operation, op.ElapsedMs);
            }
        }

        var ungrouped = operations.Where(o => o.Parent is not null && !topLevel.Any(t => t.Operation == o.Parent)).ToList();
        if (ungrouped.Count > 0)
        {
            Log.Information("[Profiler] ───────────────────────────────────────────────────────────────");
            Log.Information("[Profiler] Other operations:");
            foreach (var op in ungrouped.OrderByDescending(o => o.ElapsedMs))
            {
                Log.Information("[Profiler]   {Operation}: {ElapsedMs}ms", op.Operation, op.ElapsedMs);
            }
        }

        Log.Information("[Profiler] ═══════════════════════════════════════════════════════════════");

        var slowest = operations.OrderByDescending(o => o.ElapsedMs).Take(5).ToList();
        if (slowest.Count > 0)
        {
            Log.Information("[Profiler] Top 5 slowest operations:");
            foreach (var op in slowest)
            {
                var pct = _totalStopwatch.ElapsedMilliseconds > 0
                    ? (double)op.ElapsedMs / _totalStopwatch.ElapsedMilliseconds * 100
                    : 0;
                Log.Information("[Profiler]   {Operation}: {ElapsedMs}ms ({Percentage:F1}%)", op.Operation, op.ElapsedMs, pct);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LogSummary();
    }

    private sealed class OperationScope(StartupProfiler profiler, string operationName) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            profiler.EndOperation(operationName);
        }
    }
}
