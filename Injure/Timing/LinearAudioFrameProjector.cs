// SPDX-License-Identifier: MIT

using System;

using Injure.Audio;

namespace Injure.Timing;

public sealed class LinearAudioFrameProjector(ICurrentSampleable<AudioFrame> source, int sampleRate) : IPerfProjector<AudioFrame>, IPerfUpdateReceiver {
	private readonly ICurrentSampleable<AudioFrame> source = source ?? throw new ArgumentNullException(nameof(source));
	private readonly int sampleRate = sampleRate;

	private PerfTick refPerf;
	private AudioFrame refFrame;
	private bool inited = false;

	public void Update(PerfTick now) {
		refPerf = now;
		refFrame = source.SampleCurrent();
		inited = true;
	}

	public AudioFrame GetAt(PerfTick now) {
		if (!inited)
			throw new InvalidOperationException("Update() must be called at least once before GetAt()");
		double deltaSeconds = (double)((long)now.Value - (long)refPerf.Value) / (double)PerfTick.Frequency;
		return refFrame + (AudioFrame)(long)Math.Round(deltaSeconds * sampleRate);
	}
}
