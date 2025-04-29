using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using SDL2;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

// Contained draws the milestone like this
// +----+
// |    |
// |  +-+
// |  | |
// +--+-+
// Normal draws the milestone like this
// +----+
// |    |
// |    |
// |   +-+
// +---| |
//     +-+
public enum MilestoneDisplay {
    /// <summary>
    /// Draw the highest locked milestone centered on the lower-right corner of the base icon.
    /// </summary>
    Normal,
    /// <summary>
    /// Draw the highest locked milestone with its lower-right corner aligned with the lower-right corner of the base icon.
    /// </summary>
    Contained,
    /// <summary>
    /// Draw the highest milestone, regardless of (un)lock state, centered on the lower-right corner of the base icon.
    /// </summary>
    Always,
    /// <summary>
    /// Draw the highest milestone, regardless of (un)lock state, with its lower-right corner aligned with the lower-right corner of the base icon.
    /// </summary>
    ContainedAlways,
    /// <summary>
    /// Do not draw a milestone icon.
    /// </summary>
    None
}

public enum GoodsWithAmountEvent {
    None,
    LeftButtonClick,
    RightButtonClick,
    TextEditing,
}

public enum Click {
    None,
    Left,
    Right,
}

public static class ImmediateWidgets {
    /// <summary>Draws the icon belonging to a <see cref="FactorioObject"/>, or an empty box as a placeholder if no object is available.</summary>
    /// <param name="obj">Draw the icon for this object, or an empty box if this is <see langword="null"/>.</param>
    public static void BuildFactorioObjectIcon(this ImGui gui, IFactorioObjectWrapper? obj, IconDisplayStyle? displayStyle = null) {
        displayStyle ??= IconDisplayStyle.Default;
        if (obj == null) {
            gui.BuildIcon(Icon.Empty, displayStyle.Size, SchemeColor.BackgroundTextFaint);
            return;
        }

        SchemeColor color = (obj.target.IsAccessible() || displayStyle.AlwaysAccessible) ? SchemeColor.Source : SchemeColor.SourceFaint;
        if (displayStyle.UseScaleSetting) {
            Rect rect = gui.AllocateRect(displayStyle.Size, displayStyle.Size, RectAlignment.Middle);
            gui.DrawIcon(rect.Expand(displayStyle.Size * (Project.current.preferences.iconScale - 1) / 2), obj.target.icon, color);
        }
        else {
            gui.BuildIcon(obj.target.icon, displayStyle.Size, color);
        }
        if (gui.isBuilding && displayStyle.MilestoneDisplay != MilestoneDisplay.None
            && (obj.target.IsAccessible() || Project.current.preferences.showMilestoneOnInaccessible)) {

            bool contain = (displayStyle.MilestoneDisplay & MilestoneDisplay.Contained) != 0;
            FactorioObject? milestone = Milestones.Instance.GetHighest(obj.target, displayStyle.MilestoneDisplay >= MilestoneDisplay.Always);
            if (milestone != null) {
                Vector2 size = new Vector2(displayStyle.Size / 2f);
                var delta = contain ? size : size / 2f;
                Rect milestoneIcon = new Rect(gui.lastRect.BottomRight - delta, size);
                var icon = milestone == Database.voidEnergy.target ? DataUtils.HandIcon : milestone.icon;
                gui.DrawIcon(milestoneIcon, icon, color);
            }
        }

        Quality? quality = (obj as IObjectWithQuality<FactorioObject>)?.quality;
        if (gui.isBuilding && quality != null && quality != Quality.Normal) {
            Vector2 size = new Vector2(displayStyle.Size / 2.5f);
            Vector2 delta = new(0, size.Y);
            Rect qualityRect = new Rect(gui.lastRect.BottomLeft - delta, size);

            gui.DrawIcon(qualityRect, quality.icon, SchemeColor.Source);
        }
        if (gui.isBuilding && obj.target is Item { baseSpoilTime: > 0 } or Entity { baseSpoilTime: > 0 }) {
            Vector2 size = new Vector2(displayStyle.Size / 2.5f);
            Rect spoilableRect = new Rect(gui.lastRect.TopLeft, size);

            gui.DrawIcon(spoilableRect, Icon.Time, SchemeColor.PureForeground);
        }
    }

    public static bool BuildFloatInput(this ImGui gui, DisplayAmount amount, TextBoxDisplayStyle displayStyle, SetKeyboardFocus setKeyboardFocus = SetKeyboardFocus.No) {
        if (gui.BuildTextInput(DataUtils.FormatAmount(amount.Value, amount.Unit), out string newText, null, displayStyle, true, setKeyboardFocus)
            && DataUtils.TryParseAmount(newText, out float newValue, amount.Unit)) {
            amount.Value = newValue;
            return true;
        }

        return false;
    }

    public static Click BuildFactorioObjectButtonBackground(this ImGui gui, Rect rect, IFactorioObjectWrapper? obj,
        SchemeColor bgColor = SchemeColor.None, bool drawTransparent = false, ObjectTooltipOptions tooltipOptions = default) {

        SchemeColor overColor;

        if (bgColor == SchemeColor.None) {
            overColor = SchemeColor.Grey;
        }
        else {
            overColor = bgColor + 1;
        }

        if (MainScreen.Instance.IsSameObjectHovered(gui, obj?.target)) {
            bgColor = overColor;
        }

        var evt = gui.BuildButton(rect, bgColor, overColor, button: 0, drawTransparent: drawTransparent);

        if (evt == ButtonEvent.MouseOver && obj != null) {
            MainScreen.Instance.ShowTooltip(obj, gui, rect, tooltipOptions);
        }
        else if (evt == ButtonEvent.Click) {
            if (gui.actionParameter == SDL.SDL_BUTTON_MIDDLE && obj != null) {
                if (obj.target.showInExplorers) {
                    if (obj.target is Goods goods && obj.target.IsAccessible()) {
                        NeverEnoughItemsPanel.Show(goods);
                    }
                    else {
                        DependencyExplorer.Show(obj.target);
                    }
                }
            }
            else if (gui.actionParameter == SDL.SDL_BUTTON_LEFT) {
                return Click.Left;
            }
            else if (gui.actionParameter == SDL.SDL_BUTTON_RIGHT) {
                return Click.Right;
            }
        }

        return Click.None;
    }

    /// <summary>Draws a button displaying the icon belonging to a <see cref="FactorioObject"/>, or an empty box as a placeholder if no object is available.</summary>
    /// <param name="obj">Draw the icon for this object, or an empty box if this is <see langword="null"/>.</param>
    public static Click BuildFactorioObjectButton(this ImGui gui, IFactorioObjectWrapper? obj, ButtonDisplayStyle displayStyle, ObjectTooltipOptions tooltipOptions = default) {
        gui.BuildFactorioObjectIcon(obj, displayStyle);
        return gui.BuildFactorioObjectButtonBackground(gui.lastRect, obj, displayStyle.BackgroundColor, displayStyle.DrawTransparent, tooltipOptions);
    }

    public static Click BuildFactorioObjectButtonWithText(this ImGui gui, IFactorioObjectWrapper? obj, string? extraText = null,
        IconDisplayStyle? iconDisplayStyle = null, float indent = 0f, ObjectTooltipOptions tooltipOptions = default) {

        iconDisplayStyle ??= IconDisplayStyle.Default;
        using (gui.EnterRow()) {
            if (indent > 0) {
                // Relying on EnterRow using a default spacing of 0.5.
                gui.AllocateRect(indent - 0.5f, 0);
            }
            gui.BuildFactorioObjectIcon(obj, iconDisplayStyle);
            var color = gui.textColor;
            if (obj != null && !obj.target.IsAccessible() && !iconDisplayStyle.AlwaysAccessible) {
                color += 1;
            }

            if (Project.current.preferences.favorites.Contains(obj!)) { // null-forgiving: non-nullable collections are happy to report they don't contain null values.
                gui.BuildIcon(Icon.StarFull, 1f);
            }

            if (extraText != null) {
                gui.AllocateSpacing();
                gui.allocator = RectAllocator.RightRow;
                gui.BuildText(extraText, TextBlockDisplayStyle.Default(color));
            }
            _ = gui.RemainingRow();
            gui.BuildText(obj == null ? LSs.FactorioObjectNone : obj.target.locName, TextBlockDisplayStyle.WrappedText with { Color = color });
        }

        return gui.BuildFactorioObjectButtonBackground(gui.lastRect, obj, tooltipOptions: tooltipOptions);
    }

    internal static bool BuildInlineObjectList<T>(this ImGui gui, IEnumerable<T> list, [NotNullWhen(true)] out T? selected, ObjectSelectOptions<T> options) where T : class, IFactorioObjectWrapper {
        if (options.Header != null) {
            gui.BuildText(options.Header, Font.productionTableHeader);
        }
        IEnumerable<T> sortedList = list;

        if (options.Ordering != DataUtils.AlreadySortedRecipe) {
            sortedList = list.Order(options.Ordering);
        }

        selected = null;

        foreach (T elem in sortedList.Take(options.MaxCount)) {
            string? extraText = options.ExtraText?.Invoke(elem);

            if (gui.BuildFactorioObjectButtonWithText(elem, extraText) == Click.Left) {
                selected = elem;
            }

            if (gui.isBuilding && (options.Checkmark?.Invoke(elem) ?? false)) {
                gui.DrawIcon(Rect.Square(gui.lastRect.Right - 1f, gui.lastRect.Center.Y, 1.5f), Icon.Check, SchemeColor.Green);
            }
            else if (gui.isBuilding && (options.YellowMark?.Invoke(elem) ?? false)) {
                gui.DrawIcon(Rect.Square(gui.lastRect.Right - 1f, gui.lastRect.Center.Y, 1.5f), Icon.Check, SchemeColor.TagColorYellowText);
            }
        }

        return selected != null;
    }

    public static void BuildInlineObjectListAndButton<T>(this ImGui gui, ICollection<T> list, Action<T> selectItem, ObjectSelectOptions<T> options) where T : FactorioObject {
        using (gui.EnterGroup(default, RectAllocator.Stretch)) {
            if (gui.BuildInlineObjectList(list, out var selected, options)) {
                selectItem(selected);
                if (!options.Multiple || !InputSystem.Instance.control) {
                    _ = gui.CloseDropdown();
                }
            }

            if (list.Count > options.MaxCount && gui.BuildButton(LSs.SeeFullListButton) && gui.CloseDropdown()) {
                if (options.Multiple) {
                    SelectMultiObjectPanel.Select(list, options.Header, selectItem, options.Ordering, options.Checkmark, options.YellowMark);
                }
                else {
                    SelectSingleObjectPanel.Select(list, options.Header, selectItem, options.Ordering);
                }
            }
        }
    }

    public static void BuildInlineObjectListAndButtonWithNone<T>(this ImGui gui, ICollection<T> list, Action<T?> selectItem, ObjectSelectOptions<T> options) where T : FactorioObject {
        using (gui.EnterGroup(default, RectAllocator.Stretch)) {
            if (gui.BuildInlineObjectList(list, out var selected, options)) {
                selectItem(selected);
                _ = gui.CloseDropdown();
            }
            if (gui.BuildRedButton(LSs.ClearButton) && gui.CloseDropdown()) {
                selectItem(null);
            }

            if (list.Count > options.MaxCount && gui.BuildButton(LSs.SeeFullListButton) && gui.CloseDropdown()) {
                SelectSingleObjectPanel.SelectWithNone(list, options.Header, selectItem, options.Ordering);
            }
        }
    }

    /// <summary>Draws a button displaying the icon belonging to a <see cref="FactorioObject"/>, or an empty box as a placeholder if no object is available.
    /// Also draws a label under the button, containing the supplied <paramref name="amount"/>.</summary>
    /// <param name="goods">Draw the icon for this object, or an empty box if this is <see langword="null"/>.</param>
    /// <param name="amount">Display this value and unit.</param>
    /// <param name="useScale">If <see langword="true"/>, this icon will be displayed at <see cref="ProjectPreferences.iconScale"/>, instead of at 100% scale.</param>
    public static Click BuildFactorioObjectWithAmount(this ImGui gui, IFactorioObjectWrapper? goods, DisplayAmount amount, ButtonDisplayStyle buttonDisplayStyle,
        TextBlockDisplayStyle? textDisplayStyle = null, ObjectTooltipOptions tooltipOptions = default) {
        textDisplayStyle ??= new(Alignment: RectAlignment.Middle);
        using (gui.EnterFixedPositioning(buttonDisplayStyle.Size, buttonDisplayStyle.Size, default)) {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            Click clicked = gui.BuildFactorioObjectButton(goods, buttonDisplayStyle, tooltipOptions);

            if (goods != null) {
                gui.BuildText(DataUtils.FormatAmount(amount.Value, amount.Unit), textDisplayStyle);
                if (InputSystem.Instance.control && gui.BuildButton(gui.lastRect, SchemeColor.None, SchemeColor.Grey) == ButtonEvent.MouseOver) {
                    ShowPrecisionValueTooltip(gui, amount, goods);
                }
            }

            return clicked;
        }
    }

    public static void ShowPrecisionValueTooltip(ImGui gui, DisplayAmount amount, IFactorioObjectWrapper goods) {
        string text;
        switch (amount.Unit) {
            case UnitOfMeasure.PerSecond:
            case UnitOfMeasure.FluidPerSecond:
            case UnitOfMeasure.ItemPerSecond:
                string perSecond = DataUtils.FormatAmountRaw(amount.Value, 1f, LSs.PerSecondSuffix, DataUtils.PreciseFormat);
                string perMinute = DataUtils.FormatAmountRaw(amount.Value, 60f, LSs.PerMinuteSuffix, DataUtils.PreciseFormat);
                string perHour = DataUtils.FormatAmountRaw(amount.Value, 3600f, LSs.PerHourSuffix, DataUtils.PreciseFormat);
                text = perSecond + "\n" + perMinute + "\n" + perHour;

                if (goods.target is Item item) {
                    text += "\n" + LSs.SecondsPerStack.L(DataUtils.FormatAmount(MathF.Abs(item.stackSize / amount.Value), UnitOfMeasure.Second));
                }

                break;
            default:
                text = DataUtils.FormatAmount(amount.Value, amount.Unit, precise: true);
                break;
        }
        gui.ShowTooltip(gui.lastRect, x => {
            _ = x.BuildFactorioObjectButtonWithText(goods);
            x.BuildText(text, TextBlockDisplayStyle.WrappedText);
        }, 10f);
    }

    /// <summary>Shows a dropdown containing the (partial) <paramref name="list"/> of elements, with an action for when an element is selected.</summary>
    /// <param name="width">Width of the popup. Make sure the header text fits!</param>
    public static void BuildObjectSelectDropDown<T>(this ImGui gui, ICollection<T> list, Action<T> selectItem, ObjectSelectOptions<T> options, float width = 20f) where T : FactorioObject
        => gui.ShowDropDown(imGui => imGui.BuildInlineObjectListAndButton(list, selectItem, options), width);

    /// <summary>Shows a dropdown containing the (partial) <paramref name="list"/> of elements, with an action for when an element is selected.
    /// Also shows the available quality levels, and allows the user to select a quality. May or may not close after the user selects a quality, depending on <paramref name="selectQuality"/>.</summary>
    /// <param name="selectQuality">If not <see langword="null"/>, this will be called, and the dropdown will be closed, when the user selects a quality.
    /// In general, set this parameter when modifying an existing object, and leave it <see langword="null"/> when setting a new object.</param>
    /// <param name="width">Width of the popup. Make sure the header text fits!</param>
    public static void BuildObjectQualitySelectDropDown<T>(this ImGui gui, ICollection<T> list, Action<IObjectWithQuality<T>> selectItem, ObjectSelectOptions<T> options,
        Quality quality, Action<Quality>? selectQuality = null, float width = 20f) where T : FactorioObject

        => gui.ShowDropDown(gui => {
            if (gui.BuildQualityList(quality, out quality) && selectQuality != null && gui.CloseDropdown()) {
                selectQuality(quality);
            }
            gui.BuildInlineObjectListAndButton(list, i => selectItem(i.With(quality)), options);
        }, width);

    /// <summary>Shows a dropdown containing the (partial) <paramref name="list"/> of elements, with an action for when an element is selected.
    /// An additional "Clear" or "None" option will also be displayed.</summary>
    /// <param name="width">Width of the popup. Make sure the header text fits!</param>
    public static void BuildObjectSelectDropDownWithNone<T>(this ImGui gui, ICollection<T> list, Action<T?> selectItem, ObjectSelectOptions<T> options, float width = 20f) where T : FactorioObject
        => gui.ShowDropDown(imGui => imGui.BuildInlineObjectListAndButtonWithNone(list, selectItem, options), width);

    /// <summary>Shows a dropdown containing the (partial) <paramref name="list"/> of elements, with an action for when an element is selected.
    /// Also shows the available quality levels, and allows the user to select a quality.</summary>
    /// <param name="selectQuality">This will be called, and the dropdown will be closed, when the user selects a quality.</param>
    /// <param name="width">Width of the popup. Make sure the header text fits!</param>
    public static void BuildObjectQualitySelectDropDownWithNone<T>(this ImGui gui, ICollection<T> list, Action<IObjectWithQuality<T>?> selectItem, ObjectSelectOptions<T> options,
        Quality quality, Action<Quality> selectQuality, float width = 20f) where T : FactorioObject

        => gui.ShowDropDown(gui => {
            if (gui.BuildQualityList(quality, out quality) && gui.CloseDropdown()) {
                selectQuality(quality);
            }
            gui.BuildInlineObjectListAndButtonWithNone(list, i => selectItem(i.With(quality)), options);
        }, width);

    /// <summary>Draws a button displaying the icon belonging to a <see cref="FactorioObject"/>, or an empty box as a placeholder if no object is available.
    /// Also draws an editable textbox under the button, containing the supplied <paramref name="amount"/>.</summary>
    /// <param name="obj">Draw the icon for this object, or an empty box if this is <see langword="null"/>.</param>
    /// <param name="useScale">If <see langword="true"/>, this icon will be displayed at <see cref="ProjectPreferences.iconScale"/>, instead of at 100% scale.</param>
    /// <param name="amount">Display this value and unit. If the user edits the value, the new value will be stored in <see cref="DisplayAmount.Value"/> before returning.</param>
    /// <param name="allowScroll">If <see langword="true"/>, the default, the user can adjust the value by using the scroll wheel while hovering over the editable text.
    /// If <see langword="false"/>, the scroll wheel will be ignored when hovering.</param>
    public static GoodsWithAmountEvent BuildFactorioObjectWithEditableAmount(this ImGui gui, IFactorioObjectWrapper? obj, DisplayAmount amount, ButtonDisplayStyle buttonDisplayStyle,
        bool allowScroll = true, ObjectTooltipOptions tooltipOptions = default, SetKeyboardFocus setKeyboardFocus = SetKeyboardFocus.No) {

        using var group = gui.EnterGroup(default, RectAllocator.Stretch, spacing: 0f);
        group.SetWidth(buttonDisplayStyle.Size);
        GoodsWithAmountEvent evt = (GoodsWithAmountEvent)gui.BuildFactorioObjectButton(obj, buttonDisplayStyle, tooltipOptions);

        if (gui.BuildFloatInput(amount, TextBoxDisplayStyle.FactorioObjectInput, setKeyboardFocus)) {
            return GoodsWithAmountEvent.TextEditing;
        }

        if (allowScroll && gui.action == ImGuiAction.MouseScroll && gui.ConsumeEvent(gui.lastRect)) {
            float digit = MathF.Pow(10, MathF.Floor(MathF.Log10(amount.Value) - 2f));
            amount.Value = MathF.Round((amount.Value / digit) + gui.actionParameter) * digit;
            return GoodsWithAmountEvent.TextEditing;
        }

        return evt;
    }

    /// <summary>
    /// Builds a selection list for the available qualities, assuming multiple qualities are available.
    /// </summary>
    /// <param name="quality">The <see cref="Quality"/> to initially display selected, if any.</param>
    /// <param name="newQuality">The <see cref="Quality"/> selected by the user.</param>
    /// <param name="header">The localizable string for the text to draw, defaults to <see cref="LSs.SelectQuality"/></param>
    /// <returns><see langword="true"/> if the user selected a quality. <see langword="false"/> if they did not, or if the loaded mods do not provide multiple qualities.</returns>
    public static bool BuildQualityList(this ImGui gui, Quality? quality, [NotNullWhen(true), NotNullIfNotNull(nameof(quality))] out Quality? newQuality, LocalizableString0? headerKey = null, bool drawCentered = false) {
        newQuality = quality;
        if (Quality.Normal.nextQuality == null) {
            return false; // Nothing to do; normal quality is the only one defined.
        }

        headerKey ??= LSs.SelectQuality;

        if (drawCentered) {
            gui.BuildText(headerKey, TextBlockDisplayStyle.Centered with { Font = Font.productionTableHeader });
        }
        else {
            gui.BuildText(headerKey, Font.productionTableHeader);
        }

        using ImGui.OverlappingAllocations controller = gui.StartOverlappingAllocations(false);
        drawGrid(gui, ref newQuality, out bool addSpacing);
        float width = gui.lastRect.Width;
        controller.StartNextAllocatePass(true);
        using (gui.EnterRow(0)) {
            if (drawCentered && addSpacing) {
                gui.AllocateRect((gui.statePosition.Width - width) / 2, 0);
            }
            return drawGrid(gui, ref newQuality, out _);
        }

        static bool drawGrid(ImGui gui, ref Quality? newQuality, out bool addSpacing) {
            addSpacing = false;
            using ImGuiUtils.InlineGridBuilder grid = gui.EnterInlineGrid(2, .5f);
            Quality? drawQuality = Quality.Normal;
            int qualityCount = 0;
            while (drawQuality != null) {
                grid.Next();
                if (newQuality == drawQuality) {
                    _ = gui.BuildFactorioObjectButton(drawQuality, ButtonDisplayStyle.Default with { BackgroundColor = SchemeColor.Primary });
                }
                else if (gui.BuildFactorioObjectButton(drawQuality, ButtonDisplayStyle.Default) == Click.Left) {
                    newQuality = drawQuality;
                    addSpacing = false; // This has to be the second call, where the value doesn't matter.
                    return true;
                }
                qualityCount++;
                drawQuality = drawQuality.nextQuality;
            }
            addSpacing = qualityCount <= grid.elementsPerRow;
            return false;
        }
    }
}

/// <summary>
/// Represents an amount to be displayed to the user, and possibly edited.
/// </summary>
/// <param name="Value">The initial value to be displayed to the user.</param>
/// <param name="Unit">The <see cref="UnitOfMeasure"/> to be used when formatting <paramref name="Value"/> for display and when parsing user input.</param>
public record DisplayAmount(float Value, UnitOfMeasure Unit = UnitOfMeasure.None) {
    /// <summary>
    /// Gets or sets the value. This is either the value to be displayed or the value after modification by the user.
    /// </summary>
    public float Value { get; set; } = Value;

    /// <summary>
    /// Creates a new <see cref="DisplayAmount"/> for basic numeric display. <see cref="Unit"/> will be set to <see cref="UnitOfMeasure.None"/>.
    /// </summary>
    /// <param name="value">The initial value to be displayed to the user.</param>
    public static implicit operator DisplayAmount(float value) => new(value);
}

/// <summary>
/// Encapsulates the options for drawing an object selection list. 
/// </summary>
/// <typeparam name="T">The type of <see cref="FactorioObject"/> to be selected</typeparam>
/// <param name="Header">The header text to display</param>
/// <param name="Ordering">The sort order to use when displaying the items. Defaults to <see cref="DataUtils.DefaultOrdering"/>.</param>
/// <param name="MaxCount">Maximum number of elements in the list. If there are more, another popup can be opened by the user to show the full list.</param>
/// <param name="Multiple">If <see langword="true"/>, this popup (and its subsequent <see cref="SelectObjectPanel{T}"/>, if applicable) allow selection of multiple items.
/// Not used (treated as <see langword="false"/>) when selecting with a 'None' item.</param>
/// <param name="Checkmark">If not <see langword="null"/>, this will be called to determine if a checkmark should be drawn on the item.
/// Not used when selecting with a 'None' item or when <paramref name="Multiple"/> is <see langword="false"/>.</param>
/// <param name="YellowMark">If Checkmark is not set, draw a less distinct checkmark instead.</param>
/// <param name="ExtraText">If not <see langword="null"/>, this will be called to get extra text to be displayed right-justified after the item's name.</param>
public sealed record ObjectSelectOptions<T>(string? Header, [AllowNull] IComparer<T> Ordering = null, int MaxCount = 6, bool Multiple = false, Predicate<T>? Checkmark = null,
    Predicate<T>? YellowMark = null, Func<T, string>? ExtraText = null) where T : IFactorioObjectWrapper {

    public IComparer<T> Ordering { get; init; } = Ordering ?? (IComparer<T>)DataUtils.DefaultOrdering;
}
