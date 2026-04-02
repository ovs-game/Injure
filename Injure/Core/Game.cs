// SPDX-License-Identifier: MIT

using Injure.Graphics;

namespace Injure.Core;

public enum LoadingPhase {
	Start,
	Tick,
	Finish
}

public readonly struct LoadingContext(LoadingPhase phase, double elapsed = 0.0, bool redrawRequested = false) {
	public readonly LoadingPhase Phase = phase;
	public readonly double Elapsed = elapsed;
	public readonly bool RedrawRequested = redrawRequested;
}

public enum HostEventKind {
	Resized,
	Minimized,
	Maximized,
	Restored,
	FocusGained,
	FocusLost
}

public readonly struct HostEvent(HostEventKind kind, uint width = 0, uint height = 0) {
	public readonly HostEventKind Kind = kind;
	public readonly uint Width = width; // only meaningful on Resized
	public readonly uint Height = height; // only meaningful on Resized
}

public interface IGame {
	void Loading(in LoadingContext ctx);

	void Init(GameServices sv);
	void OnHostEvent(in HostEvent ev);
	void Render(Canvas cv);
	void Shutdown();
}
