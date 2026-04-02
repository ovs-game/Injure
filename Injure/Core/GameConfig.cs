// SPDX-License-Identifier: MIT

namespace Injure.Core;

public readonly record struct GameServicesConfig(
	bool Assets,
	bool Audio,
	bool Text
);

public enum WindowMode {
	Windowed,
	BorderlessFullscreen,
	ExclusiveFullscreen
}

public enum WindowState {
	Normal,
	Minimized,
	Maximized
}

public enum WindowPositioning {
	Undefined,
	Centered,
	Explicit
}

public readonly record struct GameWindowConfig(
	string Title,
	int Width, int Height,
	WindowMode Mode = WindowMode.Windowed,
	bool StartVisible = true,
	bool Resizable = true,
	bool Borderless = false,
	bool AllowHighDPI = true,
	WindowState StartState = WindowState.Normal,
	WindowPositioning StartPositioning = WindowPositioning.Centered,
	int StartX = 0, int StartY = 0
);

public enum PresentMode {
	TearFree,
	Adaptive,
	LowLatency
}

public readonly record struct GameRenderConfig(
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

public readonly record struct GameTimingConfig(
	RenderTimingMode RenderMode, double TargetFPS,
	LoopTimingMode LoopMode = LoopTimingMode.Wait, double TargetLoopHz = 960.0,
	int MaxLoopDeadlineMissByFrames = 4
);

public readonly record struct GameConfig(
	GameServicesConfig Services,
	GameWindowConfig Window,
	GameRenderConfig Render,
	GameTimingConfig Timing
);
