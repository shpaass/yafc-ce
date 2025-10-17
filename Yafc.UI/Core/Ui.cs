﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SDL2;
using Serilog;

namespace Yafc.UI;

public static partial class Ui {
    private static readonly ILogger logger = Logging.GetLogger(typeof(Ui));

    public static bool quit { get; private set; }

    private static readonly Dictionary<uint, Window> windows = [];
    internal static void RegisterWindow(uint id, Window window) => windows[id] = window;

    [LibraryImport("SHCore.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwareness(int awareness);
    public static void Start() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            try {
                _ = SetProcessDpiAwareness(2);
            }
            catch (Exception) {
                logger.Information("DPI awareness setup failed"); // On older versions on Windows
            }
        }

        _ = SDL.SDL_Init(SDL.SDL_INIT_VIDEO);
        _ = SDL.SDL_SetHint(SDL.SDL_HINT_RENDER_SCALE_QUALITY, "linear");
        SDL.SDL_EnableScreenSaver();
        _ = SDL_ttf.TTF_Init();
        _ = SDL_image.IMG_Init(SDL_image.IMG_InitFlags.IMG_INIT_PNG | SDL_image.IMG_InitFlags.IMG_INIT_JPG);
        asyncCallbacksAdded = SDL.SDL_RegisterEvents(1);
        SynchronizationContext.SetSynchronizationContext(new UiSynchronizationContext());
        mainThreadId = Environment.CurrentManagedThreadId;
    }

    public static long time { get; private set; }
    private static readonly Stopwatch timeWatch = Stopwatch.StartNew();

    public static bool IsMainThread() => Environment.CurrentManagedThreadId == mainThreadId;

    private static int mainThreadId;
    private static uint asyncCallbacksAdded;
    private static readonly Queue<(SendOrPostCallback, object?)> CallbacksQueued = new();

    public static void VisitLink(string url) => _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void MainLoop() {
        while (!quit) {
            ProcessEvents();
            Render();
            Thread.Sleep(10);
        }
    }

    private static void RebuildTimedOutWindows() {
        foreach (var (_, window) in windows.Where(item => item.Value.nextRepaintTime <= time)) {
            window.Rebuild();
        }
    }

    public static void ProcessEvents() {
        try {
            var inputSystem = InputSystem.Instance;
            long minNextEvent = long.MaxValue - 1;
            time = timeWatch.ElapsedMilliseconds;

            foreach (var (_, window) in windows) {
                minNextEvent = Math.Min(minNextEvent, window.nextRepaintTime);
            }

            long delta = Math.Min(1 + (minNextEvent - timeWatch.ElapsedMilliseconds), int.MaxValue);
            bool hasEvents = (delta <= 0 ? SDL.SDL_PollEvent(out var evt) : SDL.SDL_WaitEventTimeout(out evt, (int)delta)) != 0;
            time = timeWatch.ElapsedMilliseconds;

            while (hasEvents) {
                switch (evt.type) {
                    case SDL.SDL_EventType.SDL_QUIT:
                        if (!quit) {
                            quit = true;

                            foreach (var (_, v) in windows) {
                                if (v.preventQuit) {
                                    quit = false;
                                    break;
                                }
                            }
                        }

                        break;
                    case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
                        inputSystem.MouseUp(evt.button.button);
                        break;
                    case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                        inputSystem.MouseDown(evt.button.button);
                        break;
                    case SDL.SDL_EventType.SDL_MOUSEWHEEL:
                        int y = -evt.wheel.y;

                        if (evt.wheel.direction == (uint)SDL.SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED) {
                            y = -y;
                        }

                        inputSystem.MouseScroll(y);
                        break;
                    case SDL.SDL_EventType.SDL_MOUSEMOTION:
                        inputSystem.MouseMove(evt.motion.x, evt.motion.y);
                        break;
                    case SDL.SDL_EventType.SDL_KEYDOWN:
                        inputSystem.KeyDown(evt.key.keysym);
                        break;
                    case SDL.SDL_EventType.SDL_KEYUP:
                        inputSystem.KeyUp(evt.key.keysym);
                        break;
                    case SDL.SDL_EventType.SDL_TEXTINPUT:
                        unsafe {
                            int term = 0;

                            while (evt.text.text[term] != 0) {
                                ++term;
                            }

                            string inputString = new string((sbyte*)evt.text.text, 0, term, Encoding.UTF8);
                            inputSystem.TextInput(inputString);
                        }

                        break;
                    case SDL.SDL_EventType.SDL_WINDOWEVENT:
                        if (!windows.TryGetValue(evt.window.windowID, out var window)) {
                            break;
                        }

                        switch (evt.window.windowEvent) {
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_ENTER:
                                inputSystem.MouseEnterWindow(window);
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_LEAVE:
                                inputSystem.MouseExitWindow(window);
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_CLOSE:
                                window.Close();
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST:
                                window.FocusLost();
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_GAINED:
                                window.FocusGained();
                                window.Rebuild();
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_MINIMIZED:
                                window.Minimized();
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_MOVED:
                                window.WindowMoved();
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_RESIZED:
                                window.WindowResize();
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_MAXIMIZED:
                                window.WindowMaximized();
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_RESTORED:
                                window.WindowRestored();
                                break;
                            default:
                                logger.Information("Window event of type {event}", evt.window.windowEvent);
                                window.Rebuild(); // might be something like "window exposed", better to paint the UI again
                                break;
                        }

                        break;
                    case SDL.SDL_EventType.SDL_RENDER_TARGETS_RESET:
                        break;
                    case SDL.SDL_EventType.SDL_USEREVENT:
                        if (evt.user.type == asyncCallbacksAdded) {
                            ProcessAsyncCallbackQueue();
                        }

                        break;
                    default:
                        logger.Information("Event of type {event}", evt.type);
                        break;
                }

                hasEvents = SDL.SDL_PollEvent(out evt) != 0;
            }

            time = timeWatch.ElapsedMilliseconds;
            RebuildTimedOutWindows();
            inputSystem.Update();
        }
        catch (Exception ex) {
            ExceptionScreen.ShowException(ex);
        }
    }

    public static void Render() {
        foreach (var (_, window) in windows) {
            try {
                window.Render();
            }
            catch (Exception ex) {
                ExceptionScreen.ShowException(ex);
            }
        }
    }

    public static EnterThreadPoolAwaitable ExitMainThread() => default;

    public static EnterMainThreadAwaitable EnterMainThread() => default;

    public static void Quit() {
        quit = true;
        SDL_ttf.TTF_Quit();
        SDL_image.IMG_Quit();
        SDL.SDL_Quit();
    }

    private static void ProcessAsyncCallbackQueue() {
        bool hasCustomCallbacks = true;

        while (hasCustomCallbacks) {
            (SendOrPostCallback, object?) next;

            lock (CallbacksQueued) {
                if (CallbacksQueued.Count == 0) {
                    break;
                }

                next = CallbacksQueued.Dequeue();
                hasCustomCallbacks = CallbacksQueued.Count > 0;
            }

            try {
                next.Item1(next.Item2);
            }
            catch (Exception ex) {
                ExceptionScreen.ShowException(ex);
            }
        }
    }

    public static void DispatchInMainThread(SendOrPostCallback callback, object? data) {
        bool shouldSendEvent = false;

        lock (CallbacksQueued) {
            if (CallbacksQueued.Count == 0) {
                shouldSendEvent = true;
            }

            CallbacksQueued.Enqueue((callback, data));
        }

        if (shouldSendEvent) {
            SDL.SDL_Event evt = new SDL.SDL_Event {
                type = SDL.SDL_EventType.SDL_USEREVENT,
                user = new SDL.SDL_UserEvent {
                    type = asyncCallbacksAdded
                }
            };
            _ = SDL.SDL_PushEvent(ref evt);
        }
    }

    public static void UnregisterWindow(Window window) {
        _ = windows.Remove(window.id);

        if (windows.Count == 0) {
            Quit();
        }
    }

    public static void CloseWindowOfType(Type type) {
        foreach (var (_, v) in windows) {
            if (v.GetType() == type) {
                v.Close();
                break;
            }
        }
    }

    internal static void ColorSchemeChanged() {
        foreach (Window window in windows.Values) {
            window.DarkModeChanged();
        }
    }
}
