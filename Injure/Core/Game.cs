// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;
using Injure.Graphics;

namespace Injure.Core;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct LoadingPhase {
	public enum Case {
		Start = 1,
		Tick,
		Finish,
	}
}

public readonly struct LoadingContext(LoadingPhase phase, double elapsed = 0.0, bool redrawRequested = false) {
	public readonly LoadingPhase Phase = phase;
	public readonly double Elapsed = elapsed;
	public readonly bool RedrawRequested = redrawRequested;
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct HostEvent {
	public enum Case {
		Shown = 1,
		Hidden,
		Moved,
		Resized,
		DrawableSizeChanged,
		Minimized,
		Maximized,
		Restored,
		EnteredFullscreen,
		LeftFullscreen,
		FocusGained,
		FocusLost,
		MouseEnter,
		MouseLeave,
		DisplayScaleChanged,
	}
}

public interface IGame {
	void Init(GameServices sv);
	void Render(Canvas cv);
	void Shutdown();

	void Loading(in LoadingContext ctx) {}
	void OnHostEvent(HostEvent ev) {}
	void BetweenSchedulerTicks() {}
}
