using System.Collections.Concurrent;
using cadll.Models;

namespace cadll.Services;

public class JobStore : IDisposable
{
    private readonly ConcurrentDictionary<string, JobResult> _jobs = new();
    private readonly Timer _cleanup;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public JobStore()
    {
        _cleanup = new Timer(_ => Cleanup(), null, Ttl, Ttl);
    }

    public string Create()
    {
        var id = Guid.NewGuid().ToString("N");
        _jobs[id] = new JobResult();
        return id;
    }

    public JobResult? Get(string id) =>
        _jobs.TryGetValue(id, out var r) ? r : null;

    public void SetPhase(string id, string phase)
    {
        if (_jobs.TryGetValue(id, out var r))
            r.Phase = phase;
    }

    public void SetDone(string id, byte[] zip)
    {
        if (_jobs.TryGetValue(id, out var r))
        {
            r.ZipBytes = zip;
            r.Status = JobStatus.Done;
            r.Phase = "done";
        }
    }

    public void SetError(string id, string message, IReadOnlyList<string>? errors = null)
    {
        if (_jobs.TryGetValue(id, out var r))
        {
            r.ErrorMessage = message;
            r.Errors = errors;
            r.Status = JobStatus.Error;
            r.Phase = "error";
        }
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - Ttl;
        foreach (var key in _jobs.Keys.ToList())
        {
            if (_jobs.TryGetValue(key, out var r) && r.CreatedAt < cutoff)
                _jobs.TryRemove(key, out _);
        }
    }

    public void Dispose() => _cleanup.Dispose();
}
