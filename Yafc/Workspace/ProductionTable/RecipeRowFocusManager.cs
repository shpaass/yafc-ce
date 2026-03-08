using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// The target element within a <see cref="RecipeRow"/> that should receive keyboard focus.
/// </summary>
internal enum RecipeRowFocusTarget {
    BuiltCount,
    FixedCount,
}

/// <summary>
/// Manages transient keyboard focus requests for <see cref="RecipeRow"/> UI elements,
/// keeping UI focus state out of the domain model.
/// </summary>
internal sealed class RecipeRowFocusManager {
    private (RecipeRow Row, RecipeRowFocusTarget Target)? _pending;

    /// <summary>
    /// Requests that the specified element on <paramref name="row"/> receives keyboard focus on its next draw.
    /// </summary>
    /// <param name="row">The recipe row whose UI element should receive focus.</param>
    /// <param name="target">Which focusable element within <paramref name="row"/> should receive focus.</param>
    /// <remarks>
    /// Only one pending request is retained at a time. Calling this again before the previous request is
    /// consumed (via <see cref="ConsumeFocus"/>) will silently overwrite the earlier request. This is safe
    /// under the assumption that at most one focus request is issued per user gesture per frame.
    /// Call <see cref="CancelFocus"/> when a row is removed from the table to release the stored reference
    /// immediately; otherwise the stale request will be silently overwritten on the next call to
    /// <see cref="RequestFocus"/>.
    /// </remarks>
    public void RequestFocus(RecipeRow row, RecipeRowFocusTarget target) {
        _pending = (row, target);
    }

    /// <summary>
    /// Cancels any pending focus request for <paramref name="row"/>, releasing the stored reference.
    /// Has no effect if <paramref name="row"/> has no pending request.
    /// </summary>
    /// <remarks>
    /// Not calling this when removing a row is safe: the stale request will be silently overwritten by the
    /// next <see cref="RequestFocus"/> call. However, calling it explicitly releases the reference to the
    /// removed row immediately, preventing it from being retained in <c>_pending</c> until then.
    /// </remarks>
    public void CancelFocus(RecipeRow row) {
        if (_pending is { Row: var pendingRow } && ReferenceEquals(pendingRow, row)) {
            _pending = null;
        }
    }

    /// <summary>
    /// Cancels any pending focus request, releasing the stored reference.
    /// Should be called when the owning view switches its model (e.g. on tab switch) to prevent a stale
    /// request from being consumed the next time the former model is displayed.
    /// </summary>
    public void Clear() => _pending = null;

    /// <summary>
    /// If <paramref name="row"/> has a pending focus request for <paramref name="target"/>,
    /// consumes and returns <see cref="SetKeyboardFocus.Always"/>; otherwise returns <see cref="SetKeyboardFocus.No"/>.
    /// </summary>
    /// <param name="row">The recipe row to check.</param>
    /// <param name="target">Which focusable element within <paramref name="row"/> to check.</param>
    public SetKeyboardFocus ConsumeFocus(RecipeRow row, RecipeRowFocusTarget target) {
        if (_pending is { Row: var pendingRow, Target: var pendingTarget }
            && pendingTarget == target && ReferenceEquals(pendingRow, row)) {
            _pending = null;
            return SetKeyboardFocus.Always;
        }

        return SetKeyboardFocus.No;
    }
}
