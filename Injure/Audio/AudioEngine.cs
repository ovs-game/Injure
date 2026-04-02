// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using Miniaudio;

using static Injure.Audio.MAException;

namespace Injure.Audio;

public sealed unsafe class AudioEngine : IDisposable {
	internal ma_engine *ma_engine { get; private set; }

	public readonly AudioSpec Spec;

	public AudioBus Master { get; private set; }
	public AudioBus Music { get; private set; }
	public AudioBus SFX { get; private set; }

	public AudioFrame CurrentFrame {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return (AudioFrame)(long)ma.engine_get_time_in_pcm_frames(ma_engine);
		}
	}

	private bool disposed = false;

	public AudioEngine(int? wantSampleRate = null, int? wantChannels = null) {
		ma_engine_config config = ma.engine_config_init();
		if (wantSampleRate is not null)
			config.sampleRate = (uint)wantSampleRate;
		if (wantChannels is not null)
			config.channels = (uint)wantChannels;

		ma_engine = (ma_engine *)NativeMemory.Alloc((UIntPtr)sizeof(ma_engine));
		Check(ma.engine_init(&config, ma_engine));

		int rate = (int)ma.engine_get_sample_rate(ma_engine);
		int channels = (int)ma.engine_get_channels(ma_engine);
		Spec = new AudioSpec(rate, channels);

		Master = new AudioBus(this, parent: null, name: "Master");
		Music  = new AudioBus(this, parent: Master, name: "Music");
		SFX    = new AudioBus(this, parent: Master, name: "SFX");
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;

		Master.Dispose();
		Music.Dispose();
		SFX.Dispose();

		ma.engine_uninit(ma_engine);
		NativeMemory.Free(ma_engine);
		ma_engine = null;
	}
}
