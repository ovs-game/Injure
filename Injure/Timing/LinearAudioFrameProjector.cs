// SPDX-License-Identifier: MIT

using System;

using Injure.Audio;

namespace Injure.Timing;

public sealed class LinearAudioFrameProjector(ICurrentSampleable<AudioFrame> source, int sampleRate) : IMonoProjector<AudioFrame>, ITickTimestampReceiver {
	private readonly ICurrentSampleable<AudioFrame> source = source ?? throw new ArgumentNullException(nameof(source));
	private readonly int sampleRate = sampleRate;

	private MonoTick refTick;
	private AudioFrame refFrame;
	private bool inited = false;

	public void Update(MonoTick now) {
		refTick = now;
		refFrame = source.SampleCurrent();
		inited = true;
	}

	public AudioFrame GetAt(MonoTick now) {
		if (!inited)
			throw new InvalidOperationException("Update() must be called at least once before GetAt()");
		double deltaSeconds = (double)((long)now.Value - (long)refTick.Value) / (double)MonoTick.Frequency;
		return refFrame + (AudioFrame)(long)Math.Round(deltaSeconds * sampleRate);
	}
}
