// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using Miniaudio;

using static Injure.Audio.MAException;

namespace Injure.Audio;

// apparently ma_sound_group is just a typedef for ma_sound and that leaks through to here
public sealed unsafe class AudioBus : IDisposable {
	internal ma_sound *ma_sndgrp { get; private set; }

	private readonly AudioEngine engine;

	public readonly string Name;
	public float Volume {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_group_get_volume(ma_sndgrp);
		}

		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_group_set_volume(ma_sndgrp, value);
		}
	}
	public float Pan {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_group_get_pan(ma_sndgrp);
		}
		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_group_set_pan(ma_sndgrp, value);
		}
	}
	public SoundPanMode PanMode {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return (SoundPanMode)ma.sound_group_get_pan_mode(ma_sndgrp);
		}
		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_group_set_pan_mode(ma_sndgrp, (ma_pan_mode)value);
		}
	}
	public float Pitch {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_group_get_pitch(ma_sndgrp);
		}
		set {
			ObjectDisposedException.ThrowIf(disposed, this);
			ma.sound_group_set_pitch(ma_sndgrp, value);
		}
	}
	public bool Playing {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return ma.sound_group_is_playing(ma_sndgrp) != 0;
		}
	}
	public AudioFrame CurrentFrame {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return (AudioFrame)(long)ma.sound_group_get_time_in_pcm_frames(ma_sndgrp);
		}
	}

	private bool disposed = false;

	public AudioBus(AudioEngine engine, AudioBus? parent, string name) {
		this.engine = engine;
		ma_sndgrp = (ma_sound *)NativeMemory.Alloc((UIntPtr)sizeof(ma_sound));
		Check(ma.sound_group_init(engine.ma_engine, 0, parent is not null ? parent.ma_sndgrp : null, ma_sndgrp));
		Name = name;
		Volume = 1f;
	}

	public void Start() {
		ObjectDisposedException.ThrowIf(disposed, this);
		Check(ma.sound_group_start(ma_sndgrp));
	}

	public void Stop() {
		ObjectDisposedException.ThrowIf(disposed, this);
		Check(ma.sound_group_stop(ma_sndgrp));
	}

	public void StartAfter(AudioFrame frames) {
		ulong now = ma.engine_get_time_in_pcm_frames(engine.ma_engine);
		ma.sound_group_set_start_time_in_pcm_frames(ma_sndgrp, now + (ulong)frames.Value);
		Check(ma.sound_group_start(ma_sndgrp));
	}

	public void StopAfter(AudioFrame frames) {
		ulong now = ma.engine_get_time_in_pcm_frames(engine.ma_engine);
		ma.sound_group_set_stop_time_in_pcm_frames(ma_sndgrp, now + (ulong)frames.Value);
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		ma.sound_group_uninit(ma_sndgrp);
		NativeMemory.Free(ma_sndgrp);
		ma_sndgrp = null;
	}
}
