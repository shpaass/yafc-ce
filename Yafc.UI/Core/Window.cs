using System;
using System.Numerics;
using SDL2;
using Serilog;

namespace Yafc.UI;

public abstract class Window : IDisposable {
    private static readonly ILogger logger = Logging.GetLogger<Window>();

    public readonly ImGui rootGui;
    internal IntPtr window;
    /// <summary>Window icon, singleton so it is reused for all windows</summary>
    internal static IntPtr icon;
    internal Vector2 contentSize;
    internal uint id;
    internal bool repaintRequired = true;
    internal bool visible;
    internal bool closed;
    internal long nextRepaintTime = long.MaxValue;
    internal float pixelsPerUnit;
    public virtual SchemeColor backgroundColor => SchemeColor.Background;

    private Tooltip? tooltip;
    private SimpleTooltip? simpleTooltip;
    protected DropDownPanel? commonDropDown;
    private SimpleDropDown? simpleDropDown;
    protected DropDownPanel? pagesDropDown;
    private SimpleDropDown? pagesSimpleDropDown;
    private ImGui.DragOverlay? draggingOverlay;
    private bool disposedValue;

    public bool PagesSimpleDropPinned => pagesSimpleDropDown?.pinnedMode ?? false;

    public DrawingSurface? surface { get; protected set; }

    public int displayIndex => SDL.SDL_GetWindowDisplayIndex(window);
    public int repaintCount { get; private set; }

    public Vector2 size => contentSize;

    public virtual bool preventQuit => false;
    internal Window(Padding padding) => rootGui = new ImGui(Build, padding);

    public event OnFocusLost? onFocusLost;

    public Window? ChildWindow { get; set; }

    internal void Create() {
        if (surface is null) {
            throw new InvalidOperationException($"surface must be set by a derived class before calling {nameof(Create)}.");
        }

        SDL.SDL_SetWindowIcon(window, GetIcon());

        _ = SDL.SDL_SetRenderDrawBlendMode(surface.renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
        id = SDL.SDL_GetWindowID(window);
        Ui.CloseWindowOfType(GetType());
        Ui.RegisterWindow(id, this);
        visible = true;
    }

    /// <summary>Load, if needed, the window icon and return its SDL_Surface pointer, or IntPtr.Zero if not loaded due to errors</summary>
    internal static IntPtr GetIcon() {
        if (icon == IntPtr.Zero) {
            icon = SDL_image.IMG_Load("image.ico");

            if (icon == IntPtr.Zero) {
                string error = SDL.SDL_GetError();
                logger.Warning("Failed to load application icon: {error}", error);
            }
        }

        return icon;
    }

    internal static int CalculateUnitsToPixels(int display) {
        _ = SDL.SDL_GetDisplayDPI(display, out float dpi, out _, out _);
        _ = SDL.SDL_GetDisplayBounds(display, out var rect);
        // 82x60 is the minimum screen size in units, plus some for borders
        // DPI bellow 96 is more likely to be incorrectly reported value than desired,
        //     see discussion in https://github.com/shpaass/yafc-ce/issues/255#issuecomment-2508884418
        //     => we treat is as "unknown" and revert to default 100% scaling
        int desiredUnitsToPixels = dpi < 96 ? 13 : MathUtils.Round(dpi / 6.8f);

        if (desiredUnitsToPixels * 82f >= rect.w) {
            desiredUnitsToPixels = MathUtils.Floor(rect.w / 82f);
        }

        if (desiredUnitsToPixels * 65f >= rect.h) {
            desiredUnitsToPixels = MathUtils.Floor(rect.h / 65f);
        }

        return desiredUnitsToPixels;
    }

    protected internal virtual void WindowResize() {
        rootGui.MarkEverythingForRebuild();
        rootGui.Rebuild();
    }

    internal void WindowMoved() {
        if (surface is null) { throw new InvalidOperationException($"surface must be set by a derived class before calling {nameof(WindowMoved)}."); }

        int index = SDL.SDL_GetWindowDisplayIndex(window);
        int u2p = CalculateUnitsToPixels(index);

        if (u2p != pixelsPerUnit) {
            pixelsPerUnit = u2p;
            surface.pixelsPerUnit = pixelsPerUnit;
            repaintRequired = true;
            rootGui.MarkEverythingForRebuild();
            WindowResize();
        }
    }

    protected virtual void OnRepaint() { }

    internal void Render() {
        if (surface is null) {
            throw new InvalidOperationException($"surface must be set by a derived class before calling {nameof(Render)}.");
        }

        if (!repaintRequired && nextRepaintTime > Ui.time) {
            return;
        }

        if (nextRepaintTime <= Ui.time) {
            nextRepaintTime = long.MaxValue;
        }

        OnRepaint();
        repaintRequired = false;

        if (rootGui.IsRebuildRequired()) {
            _ = rootGui.CalculateState(size.X, pixelsPerUnit);
        }

        MainRender();
        surface.Present();
    }

    protected virtual void MainRender() {
        if (surface is null) {
            throw new InvalidOperationException($"surface must be set by a derived class before calling {nameof(MainRender)}.");
        }

        var bgColor = backgroundColor.ToSdlColor();
        _ = SDL.SDL_SetRenderDrawColor(surface.renderer, bgColor.r, bgColor.g, bgColor.b, bgColor.a);
        Rect fullRect = new Rect(default, contentSize);
        repaintCount++;
        surface.Clear(rootGui.ToSdlRect(fullRect));
        rootGui.InternalPresent(surface, fullRect, fullRect);
    }

    public IPanel HitTest(Vector2 position) => rootGui.HitTest(position);

    public void Rebuild() => rootGui.Rebuild();

    public void Repaint() {
        if (closed) {
            return;
        }

        if (!Ui.IsMainThread()) {
            throw new NotSupportedException("This should be called from the main thread");
        }

        repaintRequired = true;
    }

    protected internal virtual void Close() {
        visible = false;
        closed = true;
        ChildWindow?.Close();
        surface?.Dispose();
        SDL.SDL_DestroyWindow(window);
        Dispose();
        window = IntPtr.Zero;
        Ui.UnregisterWindow(this);
    }

    private void Focus() {
        if (window != IntPtr.Zero) {
            SDL.SDL_RaiseWindow(window);
            SDL.SDL_RestoreWindow(window);
            _ = SDL.SDL_SetWindowInputFocus(window);
        }
    }

    public virtual void FocusLost() => onFocusLost?.Invoke();

    internal void FocusGained() => ChildWindow?.Focus();

    public virtual void Minimized() { }

    public void SetNextRepaint(long nextRepaintTime) {
        if (this.nextRepaintTime > nextRepaintTime) {
            this.nextRepaintTime = nextRepaintTime;
        }
    }

    internal virtual void DarkModeChanged() { }

    public void ShowTooltip(Tooltip tooltip) {
        this.tooltip = tooltip;
        Rebuild();
    }

    public void HideTooltip() {
        tooltip = null;
        Rebuild();
    }

    public void ShowTooltip(ImGui targetGui, Rect target, GuiBuilder builder, float width = 20f) {
        simpleTooltip ??= new SimpleTooltip();
        simpleTooltip.Show(builder, targetGui, target, width);
        ShowTooltip(simpleTooltip);
    }

    public void ShowDropDown(DropDownPanel dropDown) {
        this.commonDropDown = dropDown;
        Rebuild();
    }

    public void ShowDropDown(ImGui targetGui, Rect target, GuiBuilder builder, Padding padding, float width = 20f) {
        simpleDropDown ??= new SimpleDropDown();
        simpleDropDown.SetPadding(padding);
        simpleDropDown.SetFocus(targetGui, target, builder, width);
        ShowDropDown(simpleDropDown);
    }

    public void ShowPagesListDropDown(ImGui targetGui, Rect target, GuiBuilder builder, Padding padding, float width = 20f, bool pinned = false) {

        if (simpleDropDown?.active == true) {
            simpleDropDown.Close();   
        }
        
        pagesSimpleDropDown ??= new SimpleDropDown();

        pagesSimpleDropDown.SetPadding(padding);
        pagesSimpleDropDown.SetFocus(targetGui, target, builder, width, pinned);

        this.pagesDropDown = pagesSimpleDropDown;
        this.
        Rebuild();
    }

    public bool ClosePagesListDropDown(ImGui targetGui, Rect target) {
        pagesSimpleDropDown ??= new SimpleDropDown();
        // If a pinned dropdown is already open for this source/rect and caller asked to open pinned, close it.
        if (pagesSimpleDropDown.active && pagesSimpleDropDown.pinnedMode && pagesSimpleDropDown.MatchesSource(targetGui, target)) {
            pagesSimpleDropDown.pinnedMode = false;
            pagesSimpleDropDown.Close();
            // Ensure top-level reference is cleared so Build won't try to render it next frame
            if (pagesDropDown == pagesSimpleDropDown) {
                pagesDropDown = null;
            }
            Rebuild();

            return true;
        }

        return false;
    }

    private void Build(ImGui gui) {
        if (closed) {
            return;
        }

        BuildContents(gui);
        if (commonDropDown != null) {
            commonDropDown.Build(gui);
            if (!commonDropDown.active) {
                commonDropDown = null;
            }
        }

        if (pagesDropDown != null) {
            pagesDropDown.Build(gui);
            if (!pagesDropDown.active) {
                pagesDropDown = null;
            }
        }

        draggingOverlay?.Build(gui);

        if (tooltip != null) {
            tooltip.Build(gui);
            if (!tooltip.active) {
                tooltip = null;
            }
        }
    }

    protected abstract void BuildContents(ImGui gui);

    internal ImGui.DragOverlay GetDragOverlay() => draggingOverlay ??= new ImGui.DragOverlay();
    protected internal virtual void WindowMaximized() { }
    protected internal virtual void WindowRestored() { }

    protected virtual void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                rootGui.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public delegate void OnFocusLost();
