using System.Threading;

namespace Yafc.Model;

/// <summary>
/// Defines the scheduling contract for committing undo batches. The host application provides an
/// implementation that determines the appropriate moment to finalize a pending undo batch (e.g., when
/// the user releases the mouse after a drag gesture). A default <see cref="ImmediateUndoBatchScheduler"/>
/// is used in headless or test scenarios.
/// </summary>
public interface IUndoBatchScheduler {
    /// <summary>
    /// Schedules <paramref name="callback"/> to be invoked when the current interaction gesture is
    /// complete. If no gesture is in progress the callback should be dispatched immediately or
    /// asynchronously on the appropriate thread.
    /// </summary>
    /// <param name="callback">The callback to invoke once the gesture finishes.</param>
    /// <param name="state">An opaque state object forwarded to <paramref name="callback"/>.</param>
    void ScheduleOnGestureFinish(SendOrPostCallback callback, object state);
}
