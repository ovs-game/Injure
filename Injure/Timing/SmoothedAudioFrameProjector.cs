// SPDX-License-Identifier: MIT

using System;

using Injure.Audio;

namespace Injure.Timing;

public sealed class SmoothedAudioFrameProjector : IMonoProjector<AudioFrame>, ITickTimestampReceiver {
	// basic alpha-beta filter over "latest audio frame" observations, hopefully close enough to a fixed timestep for rendering
	//
	// x = estimated audio playhead pos in pcm frames
	// v = estimated audio playhead speed in pcm frames
	//
	// prediction (dt in seconds):
	// 	x = x + v * dt
	//
	// correction, given a new known latest pcm frame `n`:
	// 	err = n - x
	// 	if |err| < resetThreshold:
	// 		x = x + alpha * err
	// 		unless dt < epsilon, v = clamp(v + (beta / dt) * err, vMin, vMax)
	// 	else: # treat as a discontinuity and snap
	// 		x = n
	// so, in short, alpha corrects position error and beta corrects speed error
	// the epsilon guard is to prevent (beta / dt) from becoming unreasonably large on tiny dt values
	// the vMin/vMax guards are mostly there "just in case" to make sure it stays a sane value
	private double x;
	private double v;
	private readonly double alpha;
	private readonly double beta;
	private const double epsilon = 1e-6;

	private readonly double resetThreshold;
	private readonly double vMin;
	private readonly double vMax;

	private readonly ICurrentSampleable<AudioFrame> source;
	private readonly int sampleRate;
	private bool inited = false;
	private MonoTick last;

	// tune these default values until you figure out something decent
	public SmoothedAudioFrameProjector(ICurrentSampleable<AudioFrame> source, int sampleRate, double alpha = 0.08, double beta = 0.002, double resetThresholdMs = 50.0, double maxVErrorPercent = 0.02) {
		ArgumentNullException.ThrowIfNull(source);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		this.source = source;
		this.sampleRate = sampleRate;
		this.alpha = alpha;
		this.beta = beta;
		resetThreshold = sampleRate * (resetThresholdMs / 1000.0);
		double r = sampleRate * maxVErrorPercent;
		vMin = sampleRate - r;
		vMax = sampleRate + r;
	}

	public void Update(MonoTick now) {
		AudioFrame f = source.SampleCurrent();
		if (!inited) {
			inited = true;
			x = (double)f;
			v = sampleRate;
			last = now;
			return;
		}

		double dt = advanceTo(now);
		if (dt <= 0.0)
			return;

		double err = (double)f - x;
		if (Math.Abs(err) >= resetThreshold) {
			x = (double)f;
			v = sampleRate;
			return;
		}
		x += alpha * err;
		if (dt >= epsilon)
			v = Math.Clamp(v + (beta / dt) * err, vMin, vMax);
	}

	private double advanceTo(MonoTick now) {
		if (!inited)
			return 0.0;
		double dt = (double)(now - last) / (double)MonoTick.Frequency;
		if (dt > 0.0) {
			last = now;
			x += v * dt;
		}
		return x;
	}

	public AudioFrame GetAt(MonoTick now) {
		advanceTo(now);
		return (AudioFrame)(long)Math.Floor(x);
	}

	public double GetSecondsAt(MonoTick now) {
		advanceTo(now);
		return x / sampleRate;
	}
}
