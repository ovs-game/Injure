// SPDX-License-Identifier: MIT

using System;
using System.Text;
using System.Runtime.InteropServices;
using Miniaudio;
using static Miniaudio.ma_pan_mode;
using static Miniaudio.ma_sound_flags;

using Injure.Timing;
using static Injure.Audio.MAException;

namespace Injure.Audio;

public enum SoundPanMode {
	Balance = ma_pan_mode_balance,
	Pan = ma_pan_mode_pan
}

public sealed unsafe class Sound : IDisposable {
	internal ma_sound *ma_sound { get; private set; }

	private readonly AudioEngine engine;

	private PerfTick refpointPerfTick;
	private AudioFrame refpointFrame;

	public bool Looping {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_is_looping(ma_sound) != 0;
		}
		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_set_looping(ma_sound, (uint)(value ? 1 : 0));
		}
	}
	public float Volume {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_get_volume(ma_sound);
		}
		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_set_volume(ma_sound, value);
		}
	}
	public float Pan {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_get_pan(ma_sound);
		}
		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_set_pan(ma_sound, value);
		}
	}
	public SoundPanMode PanMode {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return (SoundPanMode)ma.sound_get_pan_mode(ma_sound);
		}
		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_set_pan_mode(ma_sound, (ma_pan_mode)value);
		}
	}
	public float Pitch {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_get_pitch(ma_sound);
		}
		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_set_pitch(ma_sound, value);
		}
	}
	public bool Playing {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_is_playing(ma_sound) != 0;
		}
	}
	public AudioFrame CurrentFrame {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return (AudioFrame)(long)ma.sound_get_time_in_pcm_frames(ma_sound);
		}
	}

	private bool disposed = false;

	private static sbyte[] MakeCString(string s) {
		byte[] u8 = Encoding.UTF8.GetBytes(s);
		sbyte[] buf = new sbyte[u8.Length + 1];
		Buffer.BlockCopy(u8, 0, buf, 0, u8.Length);
		// last byte of buf should already be 0
		return buf;
	}

	public Sound(AudioEngine engine, AudioBus bus, string path, bool loop = false, float volume = 1f, ma_sound_flags ma_flags = MA_SOUND_FLAG_DECODE | MA_SOUND_FLAG_NO_SPATIALIZATION) {
		this.engine = engine;
		ma_sound = (ma_sound *)NativeMemory.Alloc((UIntPtr)sizeof(ma_sound));
		sbyte[] pathcs = MakeCString(path);
		fixed (sbyte *s = pathcs)
			Check(ma.sound_init_from_file(engine.ma_engine, s, (uint)ma_flags, bus.ma_sndgrp, null, ma_sound));
		Looping = loop;
		Volume = volume;
	}

	public void Start() {
		ObjectDisposedException.ThrowIf(disposed, this);
		Check(ma.sound_start(ma_sound));
	}

	public void Stop() {
		ObjectDisposedException.ThrowIf(disposed, this);
		Check(ma.sound_stop(ma_sound));
	}

	public void StartAfter(AudioFrame frames) {
		ulong now = ma.engine_get_time_in_pcm_frames(engine.ma_engine);
		ma.sound_set_start_time_in_pcm_frames(ma_sound, now + (ulong)frames.Value);
		Check(ma.sound_start(ma_sound));
	}

	public void StopAfter(AudioFrame frames) {
		ulong now = ma.engine_get_time_in_pcm_frames(engine.ma_engine);
		ma.sound_set_stop_time_in_pcm_frames(ma_sound, now + (ulong)frames.Value);
	}

	public void StopWithFade(AudioFrame duration) {
		ObjectDisposedException.ThrowIf(disposed, this);
		Check(ma.sound_stop_with_fade_in_pcm_frames(ma_sound, (ulong)duration.Value));
	}

	public void StopAfterWithFade(AudioFrame frames, AudioFrame fadeDuration) {
		ulong now = ma.engine_get_time_in_pcm_frames(engine.ma_engine);
		ma.sound_set_stop_time_with_fade_in_pcm_frames(ma_sound, now + (ulong)frames.Value, (ulong)fadeDuration.Value);
	}

	public void Seek(AudioFrame frame) {
		ObjectDisposedException.ThrowIf(disposed, this);
		Check(ma.sound_seek_to_pcm_frame(ma_sound, (ulong)frame.Value));
	}

	public void SeekToSecond(float seconds) {
		ObjectDisposedException.ThrowIf(disposed, this);
		Check(ma.sound_seek_to_second(ma_sound, seconds));
	}

	public void UpdateConversionRefpoint() {
		refpointPerfTick = PerfTick.GetCurrent();
		refpointFrame = CurrentFrame;
	}

	public AudioFrame PerfToAudioFrame(PerfTick perfTick) {
		double deltaSeconds = (double)((long)perfTick.Value - (long)refpointPerfTick.Value) / (double)PerfTick.Frequency;
		return refpointFrame + (AudioFrame)(long)Math.Round(deltaSeconds * engine.Spec.SampleRate);
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		ma.sound_stop(ma_sound);
		ma.sound_uninit(ma_sound);
		NativeMemory.Free(ma_sound);
	}
}
