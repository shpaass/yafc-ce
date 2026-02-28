using System.Threading;

namespace Yafc.Model;

/// <summary>
/// An <see cref="IUndoBatchScheduler"/> that invokes the callback synchronously and immediately,
/// without waiting for any interaction gesture to finish. This implementation is suitable for
/// headless execution, unit tests, and any context where no UI event loop is present.
/// </summary>
public sealed class ImmediateUndoBatchScheduler : IUndoBatchScheduler {
    /// <inheritdoc />
    public void ScheduleOnGestureFinish(SendOrPostCallback callback, object state) => callback(state);
}
