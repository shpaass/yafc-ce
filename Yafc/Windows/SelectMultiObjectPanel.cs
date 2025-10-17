using System;
using System.Collections.Generic;
using System.Numerics;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public static class SelectMultiObjectPanel {
    /// <summary>
    /// Opens a <see cref="SelectObjectPanel{TResult, TDisplay}"/> to allow the user to select one or more <see cref="FactorioObject"/>s.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for each selected item when the panel is closed.</param>
    public static void Select<T>(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T> selectItem) where T : FactorioObject {
        ThrowIfSingle(options);

        Panel<T>.Select(list, options, selectItem);
    }

    /// <summary>
    /// Opens a <see cref="SelectObjectPanel{TResult, TDisplay}"/> to allow the user to select one or more <see cref="FactorioObject"/>s, along
    /// with selecting the quality for those objects.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="options">The <see cref="ObjectSelectOptions{T}"/> controlling the appearance and function of this panel.</param>
    /// <param name="selectItem">An action to be called for each selected item when the panel is closed.</param>
    public static void SelectWithQuality<T>(IEnumerable<T> list, QualitySelectOptions<T> options, Action<IObjectWithQuality<T>> selectItem)
        where T : FactorioObject {
        ThrowIfSingle(options);

        Panel<T>.SelectWithQuality(list, options, selectItem);
    }

    private static void ThrowIfSingle<T>(ObjectSelectOptions<T> options) where T : FactorioObject {
        if (!options.Multiple) {
            throw new ArgumentException($"Cannot open a {nameof(SelectMultiObjectPanel)} with {nameof(options)}.{nameof(ObjectSelectOptions<T>.Multiple)} set to false.", nameof(options));
        }
    }

    private static readonly Predicate<FactorioObject> defaultCheckmark = _ => false;

    /// <summary>
    /// The actual panel that is displayed by <see cref="SelectMultiObjectPanel"/>'s static methods
    /// </summary>
    private class Panel<T>(Predicate<T>? checkMark, Predicate<T>? yellowMark) : SelectObjectPanel<IEnumerable<T>, T> where T : FactorioObject {
        private readonly HashSet<T> results = [];
        private readonly Predicate<T> checkMark = checkMark ?? defaultCheckmark;
        private readonly Predicate<T> yellowMark = yellowMark ?? defaultCheckmark;
        private bool allowAutoClose = true;

        public static void Select(IEnumerable<T> list, ObjectSelectOptions<T> options, Action<T> selectItem) {
            // null-forgiving: selectItem and mapResult will not be called with null(s), because allowNone is false.
            new Panel<T>(options.Checkmark, options.YellowMark).Select(list, false, options, selectItem!, mapResult!);

            static void mapResult(IEnumerable<T> objs, Action<T?> mappedAction) {
                foreach (var obj in objs) { // null-forgiving: mapResult will not be called with null, because allowNone is false.
                    mappedAction(obj);
                }
            }
        }

        public static void SelectWithQuality(IEnumerable<T> list, QualitySelectOptions<T> options, Action<IObjectWithQuality<T>> selectItem) {
            // null-forgiving: selectItem and mapResult will not be called with null(s), because allowNone is false.
            new Panel<T>(options.Checkmark, options.YellowMark).SelectWithQuality(list, false, options, selectItem!, mapResult!, null);

            static void mapResult(IEnumerable<T> objs, Action<T?> mappedAction) {
                foreach (var obj in objs) {
                    mappedAction(obj);
                }
            }
        }

        protected override void NonNullElementDrawer(ImGui gui, T element) {
            SchemeColor bgColor = results.Contains(element) ? SchemeColor.Primary : SchemeColor.None;
            Click click = gui.BuildFactorioObjectButton(element, ButtonDisplayStyle.SelectObjectPanel(bgColor), new() { ShowTypeInHeader = showTypeInHeader });

            if (checkMark(element)) {
                gui.DrawIcon(Rect.SideRect(gui.lastRect.TopLeft + new Vector2(1, 0), gui.lastRect.BottomRight - new Vector2(0, 1)), Icon.Check, SchemeColor.GreenIcon);
            }
            else if (yellowMark(element)) {
                gui.DrawIcon(Rect.SideRect(gui.lastRect.TopLeft + new Vector2(1, 0), gui.lastRect.BottomRight - new Vector2(0, 1)), Icon.Check, SchemeColor.TagColorYellowIcon);
            }

            if (click == Click.Left) {
                if (!results.Add(element)) {
                    _ = results.Remove(element);
                }
                if (!InputSystem.Instance.control && allowAutoClose) {
                    CloseWithResult(results);
                }
                allowAutoClose = false;
            }
        }

        public override void Build(ImGui gui) {
            base.Build(gui);
            using (gui.EnterGroup(default, RectAllocator.Center)) {
                gui.BuildText(LSs.SelectMultipleObjectsHint, TextBlockDisplayStyle.HintText);
                if (gui.BuildButton(LSs.Ok, active: options == null || options.SelectedQuality != null)) {
                    CloseWithResult(results);
                }
            }
        }

        protected override void ReturnPressed() {
            if (options == null || options.SelectedQuality != null) {
                CloseWithResult(results);
            }
        }
    }
}
