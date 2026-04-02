// SPDX-License-Identifier: MIT

using System;

using Injure.Audio;
using Injure.SDL;

namespace Injure.Timing;

public sealed class SmoothedAudioClock {
	// basic alpha-beta filter over sparse "latest audio frame" observations, hopefully close enough to a fixed timestep for rendering
	//
	// x = estimated audio playhead pos in pcm frames
	// v = estimated audio playhead speed in pcm frames
	//
	// prediction (dt in seconds):
	// 	x = x + v * dt
	//
	// correction, given a new known latest pcm frame `n` and sample rate `sr`:
	// 	err = n - x
	// 	x = x + alpha * err
	// 	unless dt < epsilon, v = v + (beta / dt) * err
	// the guard is to prevent (beta / dt) from becoming unreasonably large on tiny dt values
	//
	// so, in short, alpha corrects position error and beta corrects speed error
	private double x;
	private double v;
	private readonly double alpha;
	private readonly double beta;
	private const double epsilon = 1e-6;

	// if |err| >= resetThreshold, treat that as a discontinuity and snap instead of smoothing
	private readonly double resetThreshold;

	// clamp v to some range around the samplerate to make sure it stays a sane value
	private readonly double vMin;
	private readonly double vMax;

	private readonly int sampleRate;
	private bool inited = false;
	private bool paused = false;
	private PerfTick lastPerf;

	// tune these default values until you figure out something decent
	public SmoothedAudioClock(int sampleRate, double alpha = 0.08, double beta = 0.002, double resetThresholdMs = 100.0, double maxVErrorPercent = 0.02) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		this.sampleRate = sampleRate;
		this.alpha = alpha;
		this.beta = beta;
		resetThreshold = sampleRate * (resetThresholdMs / 1000.0);
		double r = sampleRate * maxVErrorPercent;
		vMin = sampleRate - r;
		vMax = sampleRate + r;
	}

	public void Reset(AudioFrame nowFrame, PerfTick nowPerf) {
		inited = true;
		paused = false;
		x = (double)nowFrame;
		v = sampleRate;
		lastPerf = nowPerf;
	}

	public void Pause() {
		paused = true;
	}

	public void Resume(AudioFrame nowFrame, PerfTick nowPerf) {
		Reset(nowFrame, nowPerf);
	}

	public void UpdateLatestFrame(AudioFrame nowFrame, PerfTick nowPerf) {
		if (!inited) {
			Reset(nowFrame, nowPerf);
			return;
		}
		if (paused)
			return;

		double dt = (double)(nowPerf - lastPerf) / (double)PerfTick.Frequency;
		if (dt <= 0.0)
			return;
		lastPerf = nowPerf;

		// predict
		x += v * dt;

		// correct
		double err = (double)nowFrame - x;
		if (Math.Abs(err) >= resetThreshold) {
			x = (double)nowFrame;
			v = sampleRate;
			return;
		}
		x += alpha * err;
		if (dt < epsilon)
			return;
		v += beta / dt * err;
		v = Math.Clamp(v, vMin, vMax);
	}

	private double getX(PerfTick nowPerf) {
		if (!inited)
			return 0.0;
		if (!paused) {
			double dt = (double)(nowPerf - lastPerf) / (double)PerfTick.Frequency;
			if (dt > 0.0) {
				lastPerf = nowPerf;
				x += v * dt;
			}
		}
		return x;
	}

	public double GetSeconds(PerfTick nowPerf) {
		return getX(nowPerf) / sampleRate;
	}

	public AudioFrame GetAudioFrame(PerfTick nowPerf) {
		return (AudioFrame)Math.Floor(getX(nowPerf));
	}
}
