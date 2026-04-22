// SPDX-License-Identifier: MIT

using Injure.Rendering;

namespace Injure.Core;

public readonly record struct ServiceConfig(
	bool Assets,
	bool Audio,
	bool Text
);

public readonly record struct WindowConfig(
	WindowSettings Settings,
	bool AllowHighDPI = true
);

public readonly record struct RenderConfig(
	RenderSettings Settings,
	PowerPreference PowerPreference,
	BackendType Backend = default
) {
	public RenderConfig(RenderSettings Settings) : this(Settings, PowerPreference.HighPerformance) {}
};

public readonly record struct TimingConfig(
	TimingSettings Settings
);

public readonly record struct GameConfig(
	ServiceConfig Service,
	WindowConfig Window,
	RenderConfig Render,
	TimingConfig Timing
);
