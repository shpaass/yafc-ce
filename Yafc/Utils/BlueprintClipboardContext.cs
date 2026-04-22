using Yafc.Blueprints;
using Yafc.UI;

namespace Yafc;

public static class BlueprintClipboardContext {
    /// <summary>
    /// Gets the blueprint format requested for clipboard export based on the current keyboard modifiers.
    /// Hold Ctrl to export raw JSON; otherwise export compressed Base64.
    /// </summary>
    public static BlueprintFormat RequestedFormat
        => InputSystem.Instance.control ? BlueprintFormat.RawJson : BlueprintFormat.CompressedBase64;
}
