// SPDX-License-Identifier: MIT

namespace Injure.Core;

public enum WindowMode {
	Normal,
	Minimized,
	Maximized
}

public enum WindowPositioning {
	Undefined,
	Centered,
	Explicit
}

public readonly record struct WindowSettings(
	string Title,
	int Width, int Height,
	WindowMode Mode = WindowMode.Normal,
	WindowPositioning Positioning = WindowPositioning.Undefined,
	int X = 0, int Y = 0,
	bool Visible = true,
	bool Resizable = true,
	bool Borderless = false,
	bool Fullscreen = false
);

public enum PresentMode {
	TearFree,
	Adaptive,
	LowLatency
}

public readonly record struct RenderSettings(
	PresentMode PresentMode = PresentMode.Adaptive
);

public enum RenderTimingMode {
	Capped,
	Uncapped
}

public enum LoopTimingMode {
	Wait,
	NoWait // you probably don't want this unless you really know what you're doing
}

public readonly record struct TimingSettings(
	RenderTimingMode RenderMode, double TargetFPS,
	LoopTimingMode LoopMode = LoopTimingMode.Wait, double TargetLoopHz = 960.0,
	int MaxLoopDeadlineMissByLoopDurations = 4
);
