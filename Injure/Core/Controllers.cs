// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

using Injure.Timing;

namespace Injure.Core;

public struct WindowState {
	public string Title { get; internal set; }
	public int Width { get; internal set; }
	public int Height { get; internal set; }
	public int DrawableWidth { get; internal set; }
	public int DrawableHeight { get; internal set; }
	public bool Visible { get; internal set; }
	public bool Resizable { get; internal set; }
	public bool Borderless { get; internal set; }
	public bool Fullscreen { get; internal set; }
	public WindowMode Mode { get; internal set; }
	public float DisplayScale { get; internal set; }
	public bool HasKeyboardFocus { get; internal set; }
	public bool HasMouseFocus { get; internal set; }
	public int X { get; internal set; }
	public int Y { get; internal set; }
	public MonoTick UpdatedAt { get; internal set; }
}

public interface IWindowController {
	WindowSettings Settings { get; }
	WindowMode State { get; }
	bool TrySet(in WindowSettings settings, [NotNullWhen(false)] out string? err);
}

public interface IRenderController {
	RenderSettings Settings { get; }
	bool TrySet(in RenderSettings settings, [NotNullWhen(false)] out string? err);
}

public interface ITimingController {
	TimingSettings Settings { get; }
	bool TrySet(in TimingSettings settings, [NotNullWhen(false)] out string? err);
}
