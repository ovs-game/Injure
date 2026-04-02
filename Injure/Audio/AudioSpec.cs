// SPDX-License-Identifier: MIT

namespace Injure.Audio;

public readonly struct AudioSpec(int sampleRate, int channels) {
	public readonly int SampleRate = sampleRate;
	public readonly int Channels = channels;
}
