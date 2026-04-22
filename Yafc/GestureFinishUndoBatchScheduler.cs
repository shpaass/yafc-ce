using System.Threading;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// An <see cref="IUndoBatchScheduler"/> that delegates to <see cref="InputSystem.DispatchOnGestureFinish"/>.
/// Undo batches are committed once the user releases the current mouse button (or immediately if no
/// mouse button is being held). This implementation is appropriate for interactive UI sessions.
/// </summary>
internal sealed class GestureFinishUndoBatchScheduler : IUndoBatchScheduler {
    /// <inheritdoc />
    public void ScheduleOnGestureFinish(SendOrPostCallback callback, object state)
        => InputSystem.Instance.DispatchOnGestureFinish(callback, state);
}
