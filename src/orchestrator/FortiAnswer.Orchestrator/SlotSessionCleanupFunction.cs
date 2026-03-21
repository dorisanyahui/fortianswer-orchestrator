using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// Timer-triggered function that runs daily at 02:00 UTC to delete stale slot sessions.
///
/// Removes:
///   - Sessions with status "complete" or "expired" (any age)
///   - Sessions with status "active" but UpdatedUtc older than 2 hours
///     (the in-process TTL check marks them expired on read, this cleans up the table row)
///
/// Schedule: "0 0 2 * * *" = every day at 02:00:00 UTC
/// </summary>
public sealed class SlotSessionCleanupFunction
{
    private readonly SlotSessionService _sessions;
    private readonly ILogger<SlotSessionCleanupFunction> _log;

    public SlotSessionCleanupFunction(SlotSessionService sessions, ILogger<SlotSessionCleanupFunction> log)
    {
        _sessions = sessions;
        _log      = log;
    }

    [Function("SlotSession_Cleanup")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer)
    {
        _log.LogInformation("SlotSession cleanup timer fired. IsPastDue={IsPastDue}", timer.IsPastDue);

        var deleted = await _sessions.CleanupExpiredAsync(olderThan: TimeSpan.FromHours(2));

        _log.LogInformation("SlotSession cleanup complete. Deleted={Deleted}", deleted);
    }
}
