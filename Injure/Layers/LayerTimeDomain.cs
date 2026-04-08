// SPDX-License-Identifier: MIT

namespace Injure.Layers;

public sealed class LayerTimeDomain {
	public double TimeScale { get; set; } = 1.0;
	public bool Paused { get; set; } = false;

	public double Time { get; private set; } = 0.0;
	public double RawTime { get; private set; } = 0.0;
	public ulong TickNum { get; private set; } = 0;

	public double Transform(double rawDt) => Paused ? 0.0 : rawDt * TimeScale;
	public void Advance(double dt, double rawDt) {
		Time += dt;
		RawTime += rawDt;
		TickNum++;
	}
}
