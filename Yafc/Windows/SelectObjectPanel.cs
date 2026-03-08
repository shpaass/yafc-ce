using System;
using System.Collections.Generic;
using System.Numerics;
using SDL2;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

/// <summary>
/// Represents a panel that can generate a result when the user selects zero or more <see cref="FactorioObject"/>s, or can generate no result, if
/// the user selects a close or cancel button.
/// </summary>
/// <typeparam name="TResult">The type of result the panel can generate.</typeparam>
/// <typeparam name="TDisplay">The type of object (derived from <see cref="FactorioObject"/>) the panel will display.</typeparam>
public abstract class SelectObjectPanel<TResult, TDisplay> : PseudoScreenWithResult<TResult> where TDisplay : FactorioObject {
    private readonly SearchableList<TDisplay?> list;
    private string? header;
    private Rect searchBox;
    private string? noneTooltip;
    protected QualitySelectOptions<TDisplay>? options { get; private set; }

    /// <summary>
    /// If <see langword="true"/> and the object being hovered is not a <see cref="Goods"/>, the <see cref="ObjectTooltip"/> should specify the type of object.
    /// See also <see cref="ObjectTooltipOptions.ShowTypeInHeader"/>.
    /// </summary>
    protected bool showTypeInHeader { get; private set; }

    protected SelectObjectPanel() : base(40f) => list = new SearchableList<TDisplay?>(30, new Vector2(2.5f, 2.5f), ElementDrawer, ElementFilter);

    protected void SelectWithQuality(IEnumerable<TDisplay> list, bool allowNone, QualitySelectOptions<TDisplay> options,
        Action<IObjectWithQuality<TDisplay>?> selectItem, Action<TResult?, Action<TDisplay?>> mapResult, string? noneTooltip) {

        Select(list, allowNone, options, selectQualities, mapResult, noneTooltip);

        void selectQualities(TDisplay? u) {
            if (u == null) {
                selectItem(null);
            }
            else {
                foreach (var result in options.ApplyQualitiesTo(u)) {
                    selectItem(result);
                }
            }
        }
    }

    /// <summary>
    /// Opens a <see cref="SelectObjectPanel{TResult, TDisplay}"/> to allow the user to select zero or more <see cref="FactorioObject"/>s.
    /// </summary>
    /// <param name="list">The items to be displayed in this panel.</param>
    /// <param name="selectItem">An action to be called for each selected item when the panel is closed.
    /// The parameter may be <see langword="null"/> if <paramref name="allowNone"/> is <see langword="true"/>.</param>
    /// <param name="mapResult">An action that should convert the <typeparamref name="TResult"/>? result into zero or more <typeparamref name="TDisplay"/>s, and then call its second
    /// parameter for each <typeparamref name="TDisplay"/>. The first parameter may be <see langword="null"/> if <paramref name="allowNone"/> is <see langword="true"/>.</param>
    /// <param name="allowNone">If <see langword="true"/>, a "none" option will be displayed. Selection of this item will be conveyed by calling <paramref name="mapResult"/>
    /// and <paramref name="selectItem"/> with <see langword="null"/> values for <typeparamref name="TResult"/> and <typeparamref name="TDisplay"/>.</param>
    /// <param name="noneTooltip">If not <see langword="null"/>, this tooltip will be displayed when hovering over the "none" item.</param>
    protected void Select(IEnumerable<TDisplay> list, bool allowNone, ObjectSelectOptions<TDisplay> options, Action<TDisplay?> selectItem,
        Action<TResult?, Action<TDisplay?>> mapResult, string? noneTooltip = null) {

        _ = MainScreen.Instance.ShowPseudoScreen(this);
        this.options = options as QualitySelectOptions<TDisplay>;
        this.noneTooltip = noneTooltip;
        header = options.Header;
        showTypeInHeader = typeof(TDisplay) == typeof(FactorioObject);
        List<TDisplay?> data = [.. list];
        data.Sort(options.Ordering!); // null-forgiving: We don't have any nulls in the list yet.
        if (allowNone) {
            data.Insert(0, null);
        }

        this.list.filter = default;
        this.list.data = data;
        Rebuild();
        completionCallback = (hasResult, result) => {
            if (hasResult) {
                mapResult(result, obj => {
                    if (obj is TDisplay u) {
                        if (options.Ordering is DataUtils.FavoritesComparer<TDisplay> favoritesComparer) {
                            favoritesComparer.AddToFavorite(u);
                        }

                        selectItem(u);
                    }
                    else if (allowNone) {
                        selectItem(null);
                    }
                });
            }
        };
    }

    private void ElementDrawer(ImGui gui, TDisplay? element, int index) {
        if (element == null) {
            ButtonEvent evt = gui.BuildRedButton(Icon.Close);
            if (noneTooltip != null) {
                _ = evt.WithTooltip(gui, noneTooltip);
            }
            if (evt) {
                CloseWithResult(default);
            }
        }
        else {
            NonNullElementDrawer(gui, element);
        }
    }

    /// <summary>
    /// Called to draw a <see cref="FactorioObject"/> that should be displayed in this panel, and to handle mouse-over and click events.
    /// <paramref name="element"/> will not be null. If a "none" or "clear" option is present, <see cref="SelectObjectPanel{TResult, TDisplay}"/>
    /// takes care of that option.
    /// </summary>
    protected abstract void NonNullElementDrawer(ImGui gui, TDisplay element);

    private bool ElementFilter(TDisplay? data, SearchQuery query)
        // data.Match would return false (do not display) when data is null, but we want to display the null element instead.
        => data?.Match(query) ?? true;

    public override void Build(ImGui gui) {
        BuildHeader(gui, header);

        if (options != null) {
            _ = gui.BuildQualityList(options, drawCentered: true);
            gui.AllocateSpacing();
        }

        string filterText = list.filter.query;
        if (gui.BuildSearchBox(filterText, out string newFilterText, LSs.TypeForSearchHint, setKeyboardFocus: SetKeyboardFocus.OnFirstPanelDraw)) {
            list.filter = new SearchQuery(newFilterText);
        }

        searchBox = gui.lastRect;
        list.Build(gui);
    }

    public override bool KeyDown(SDL.SDL_Keysym key) {
        contents.SetTextInputFocus(searchBox, list.filter.query);
        return base.KeyDown(key);
    }
}
