// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;

namespace Injure.Graphics.PixelConv;

[ClosedFlags]
public readonly partial struct ConversionFlags {
	[Flags]
	public enum Bits {
		None                       = 0,
		AllowNarrowing             = 1 << 0,
		AllowDroppingAlpha         = 1 << 1,
		AllowDroppingColorChannels = 1 << 2,
		AllowDroppingChannels      = AllowDroppingAlpha | AllowDroppingColorChannels,
	}
}

public readonly record struct PixelConvertOptions(
	ushort Alpha16UNorm = 0xffff,
	bool OverrideAlpha = false,
	ConversionFlags Flags = default
) {
	public PixelConvertOptions() : this(0xffff) {}
	private readonly int dummy = 1;
	internal bool IsDefault => dummy == 0;
}
