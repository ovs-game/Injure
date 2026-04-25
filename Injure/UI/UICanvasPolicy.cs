// SPDX-License-Identifier: MIT

using System.Numerics;

using Injure.Analyzers.Attributes;

namespace Injure.UI;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UICanvasMode {
	public enum Case {
		Fixed = 1,
		FixedHeightExpandWidth,
		FixedWidthExpandHeight
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UICanvasFitMode {
	public enum Case {
		Letterbox = 1,
		Stretch
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UICanvasScaleMode {
	public enum Case {
		Fractional = 1,
		Integer
	}
}

public readonly record struct UICanvasPolicy(
	UICanvasMode Mode,
	SizeF ReferenceSize,
	UICanvasFitMode FitMode,
	UICanvasScaleMode ScaleMode
) {
	public UICanvasPolicy(UICanvasMode Mode, SizeF ReferenceSize) : this(Mode, ReferenceSize, UICanvasFitMode.Letterbox, UICanvasScaleMode.Fractional) {}

	public static UICanvasPolicy Fixed(float width, float height) => new(UICanvasMode.Fixed, new SizeF(width, height));
	public static UICanvasPolicy FixedHeight(float width, float height) => new(UICanvasMode.FixedHeightExpandWidth, new SizeF(width, height));
	public static UICanvasPolicy FixedWidth(float width, float height) => new(UICanvasMode.FixedWidthExpandHeight, new SizeF(width, height));
}

public readonly record struct UICanvasTransform(
	RectF LogicalRect,
	RectI ViewportRect,
	Vector2 Scale
);
