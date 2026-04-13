// SPDX-License-Identifier: MIT

using System;

namespace Injure.Graphics.PixelConv;

[Flags]
public enum ConversionFlags : byte {
	None                       = 0,
	AllowNarrowing             = 1 << 0,
	AllowDroppingAlpha         = 1 << 1,
	AllowDroppingColorChannels = 1 << 2,
	AllowDroppingChannels      = AllowDroppingAlpha | AllowDroppingColorChannels
}

public readonly record struct PixelConvertOptions(
	ushort Alpha16UNorm = 0xffff,
	bool OverrideAlpha = false,
	ConversionFlags Flags = ConversionFlags.None
) {
	public PixelConvertOptions() : this(0xffff) {}
	private readonly int dummy = 1;
	internal bool IsDefault => dummy == 0;
}
