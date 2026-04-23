// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Hexa.NET.SDL3;

using Injure.Rendering;
using Injure.Timing;
using static Injure.Core.SDLException;

namespace Injure.Core;

public sealed unsafe class SDLWindowController : IWindowController {
	private readonly SDLWindow *window;

	private WindowSettings settings;
	private WindowState state;

	public WindowSettings Settings => settings;
	public WindowState State => state;

	public SDLWindowController(SDLWindow *window, WindowSettings initialSettings) {
		ArgumentNullException.ThrowIfNull(window);
		this.window = window;
		if (!tryNormalize(initialSettings, out WindowSettings normalized, out string? err))
			throw new ArgumentException(err, nameof(initialSettings));
		settings = normalized;
		state = queryState(window, MonoTick.GetCurrent());
	}

	public bool TrySet(in WindowSettings nextSettings, [NotNullWhen(false)] out string? err) {
		if (!tryNormalize(nextSettings, out WindowSettings next, out err))
			return false;
		try {
			applyDiff(settings, next);
			settings = next;
			state = queryState(window, MonoTick.GetCurrent());
			err = null;
			return true;
		} catch (Exception ex) {
			state = queryState(window, MonoTick.GetCurrent());
			err = ex.Message;
			return false;
		}
	}

	private static bool tryNormalize(in WindowSettings s, out WindowSettings normalized, [NotNullWhen(false)] out string? err ) {
		if (s.Title is null) {
			normalized = default;
			err = "window title must not be null";
			return false;
		}
		if (s.Width <= 0) {
			normalized = default;
			err = "window width must be positive";
			return false;
		}
		if (s.Height <= 0) {
			normalized = default;
			err = "window height must be positive";
			return false;
		}
		if (s.Fullscreen && s.Mode != WindowMode.Normal) {
			normalized = default;
			err = "fullscreen windows must use WindowMode.Normal";
			return false;
		}
		normalized = s;
		if (normalized.Positioning != WindowPositioning.Explicit)
			normalized = normalized with { X = 0, Y = 0 };
		err = null;
		return true;
	}

	private void applyDiff(in WindowSettings old, in WindowSettings next) {
		if (old.Title != next.Title)
			Check(SDL.SetWindowTitle(window, next.Title));
		if (old.Resizable != next.Resizable)
			Check(SDL.SetWindowResizable(window, next.Resizable));
		if (old.Borderless != next.Borderless)
			Check(SDL.SetWindowBordered(window, !next.Borderless));

		if (!next.Fullscreen) {
			if (old.Positioning != next.Positioning || old.X != next.X || old.Y != next.Y) {
				switch (next.Positioning.Tag) {
				case WindowPositioning.Case.Undefined:
					Check(SDL.SetWindowPosition(window, unchecked((int)SDL.SDL_WINDOWPOS_UNDEFINED_MASK), unchecked((int)SDL.SDL_WINDOWPOS_UNDEFINED_MASK)));
					break;
				case WindowPositioning.Case.Centered:
					Check(SDL.SetWindowPosition(window, unchecked((int)SDL.SDL_WINDOWPOS_CENTERED_MASK), unchecked((int)SDL.SDL_WINDOWPOS_CENTERED_MASK)));
					break;
				case WindowPositioning.Case.Explicit:
					Check(SDL.SetWindowPosition(window, next.X, next.Y));
					break;
				default:
					throw new UnreachableException();
				}
			}
		}

		if (old.Width != next.Width || old.Height != next.Height)
			Check(SDL.SetWindowSize(window, next.Width, next.Height));

		if (old.Fullscreen != next.Fullscreen)
			Check(SDL.SetWindowFullscreen(window, next.Fullscreen));

		if (old.Mode != next.Mode) {
			switch (next.Mode.Tag) {
			case WindowMode.Case.Normal:
				Check(SDL.RestoreWindow(window));
				break;
			case WindowMode.Case.Minimized:
				Check(SDL.MinimizeWindow(window));
				break;
			case WindowMode.Case.Maximized:
				Check(SDL.MaximizeWindow(window));
				break;
			default:
				throw new InternalStateException("unexpected WindowMode value");
			}
		}

		if (old.Visible != next.Visible) {
			if (next.Visible)
				Check(SDL.ShowWindow(window));
			else
				Check(SDL.HideWindow(window));
		}
	}

	private static WindowState queryState(SDLWindow *window, MonoTick now) {
		int width, height;
		int drawableWidth, drawableHeight;
		int x, y;

		Check(SDL.GetWindowSize(window, &width, &height));
		Check(SDL.GetWindowSizeInPixels(window, &drawableWidth, &drawableHeight));
		Check(SDL.GetWindowPosition(window, &x, &y));

		SDLWindowFlags flags = (SDLWindowFlags)SDL.GetWindowFlags(window);

		// https://wiki.libsdl.org/SDL3/SDL_GetWindowTitle
		// (const char *) Returns the title of the window in UTF-8 format ...
		byte *title = SDL.GetWindowTitle(window);
		byte *end = title;
		while (*end != '\0')
			end++;
		string titlestr = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(title, checked((int)(end - title))));

		return new WindowState {
			Title = titlestr,
			Width = width,
			Height = height,
			DrawableWidth = drawableWidth,
			DrawableHeight = drawableHeight,
			Visible = (flags & SDLWindowFlags.Hidden) == 0,
			Resizable = (flags & SDLWindowFlags.Resizable) != 0,
			Borderless = (flags & SDLWindowFlags.Borderless) != 0,
			Fullscreen = (flags & SDLWindowFlags.Fullscreen) != 0,
			Mode =
				(flags & SDLWindowFlags.Minimized) != 0 ? WindowMode.Minimized :
				(flags & SDLWindowFlags.Maximized) != 0 ? WindowMode.Maximized :
				WindowMode.Normal,
			DisplayScale = SDL.GetWindowDisplayScale(window),
			HasKeyboardFocus = (flags & SDLWindowFlags.InputFocus) != 0,
			HasMouseFocus = (flags & SDLWindowFlags.MouseFocus) != 0,
			X = x,
			Y = y,
			UpdatedAt = now
		};
	}

	public bool TryHandleSDLEvent(in SDLEvent ev, IRenderOutput resizeSink, IGame hostEventSink) {
		MonoTick tick = (MonoTick)ev.Window.Timestamp;
		switch ((SDLEventType)ev.Type) {
		case SDLEventType.WindowShown:
			state.Visible = true;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.Shown);
			return true;
		case SDLEventType.WindowHidden:
			state.Visible = false;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.Hidden);
			return true;
		case SDLEventType.WindowMoved:
			state.X = ev.Window.Data1;
			state.Y = ev.Window.Data2;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.Moved);
			return true;
		case SDLEventType.WindowResized:
			state.Width = ev.Window.Data1;
			state.Height = ev.Window.Data2;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.Resized);
			return true;
		case SDLEventType.WindowPixelSizeChanged:
			state.DrawableWidth = ev.Window.Data1;
			state.DrawableHeight = ev.Window.Data2;
			state.UpdatedAt = tick;
			resizeSink.Resized();
			hostEventSink.OnHostEvent(HostEvent.DrawableSizeChanged);
			return true;
		case SDLEventType.WindowMinimized:
			state.Mode = WindowMode.Minimized;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.Minimized);
			return true;
		case SDLEventType.WindowMaximized:
			state.Mode = WindowMode.Maximized;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.Maximized);
			return true;
		case SDLEventType.WindowRestored:
			state.Mode = WindowMode.Normal;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.Restored);
			return true;
		case SDLEventType.WindowEnterFullscreen:
			state.Fullscreen = true;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.EnteredFullscreen);
			return true;
		case SDLEventType.WindowLeaveFullscreen:
			state.Fullscreen = false;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.LeftFullscreen);
			return true;
		case SDLEventType.WindowFocusGained:
			state.HasKeyboardFocus = true;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.FocusGained);
			return true;
		case SDLEventType.WindowFocusLost:
			state.HasKeyboardFocus = false;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.FocusLost);
			return true;
		case SDLEventType.WindowMouseEnter:
			state.HasMouseFocus = true;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.MouseEnter);
			return true;
		case SDLEventType.WindowMouseLeave:
			state.HasMouseFocus = false;
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.MouseLeave);
			return true;
		case SDLEventType.WindowDisplayScaleChanged:
			state.DisplayScale = SDL.GetWindowDisplayScale(window);
			state.UpdatedAt = tick;
			hostEventSink.OnHostEvent(HostEvent.DisplayScaleChanged);
			return true;
		}
		return false;
	}
}
