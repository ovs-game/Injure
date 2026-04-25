// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;

namespace Injure.UI;

public readonly record struct UIThickness(float Left, float Top, float Right, float Bottom) {
	public static readonly UIThickness Zero = default;

	public UIThickness(float uniform) : this(uniform, uniform, uniform, uniform) {
	}

	public float Horizontal => Left + Right;
	public float Vertical => Top + Bottom;
}

public readonly record struct UISizeConstraint(float MaxWidth, float MaxHeight) {
	public static readonly UISizeConstraint Unbounded = new(float.PositiveInfinity, float.PositiveInfinity);

	public UISizeConstraint(SizeF size) : this(size.Width, size.Height) {
	}

	public SizeF Clamp(SizeF s) => new(MathF.Min(s.Width, MaxWidth), MathF.Min(s.Height, MaxHeight));
}

public static class UIGeometryExtensions {
	extension(RectF rect) {
		public RectF Deflate(UIThickness t) {
			float x = rect.X + t.Left;
			float y = rect.Y + t.Top;
			float w = MathF.Max(0f, rect.Width - t.Horizontal);
			float h = MathF.Max(0f, rect.Height - t.Vertical);
			return new RectF(x, y, w, h);
		}

		public RectF Inflate(UIThickness t) => new(rect.X - t.Left, rect.Y - t.Top, rect.Width + t.Horizontal, rect.Height + t.Vertical);
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UIAxis {
	public enum Case {
		Horizontal = 1,
		Vertical
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UIAlign {
	public enum Case {
		Start = 1,
		Center,
		End,
		Stretch
	}
}
