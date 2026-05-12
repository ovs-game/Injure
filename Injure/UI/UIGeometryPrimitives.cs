// SPDX-License-Identifier: MIT

using System;

using Injure.Analyzers.Attributes;

namespace Injure.UI;

public readonly record struct UIThickness {
	public float Left {
		get;
		init {
			if (!float.IsFinite(value))
				throw new ArgumentOutOfRangeException(nameof(Left), "UI thickness values must be finite");
			field = value;
		}
	}
	public float Top {
		get;
		init {
			if (!float.IsFinite(value))
				throw new ArgumentOutOfRangeException(nameof(Top), "UI thickness values must be finite");
			field = value;
		}
	}
	public float Right {
		get;
		init {
			if (!float.IsFinite(value))
				throw new ArgumentOutOfRangeException(nameof(Right), "UI thickness values must be finite");
			field = value;
		}
	}
	public float Bottom {
		get;
		init {
			if (!float.IsFinite(value))
				throw new ArgumentOutOfRangeException(nameof(Bottom), "UI thickness values must be finite");
			field = value;
		}
	}

	public static readonly UIThickness Zero = default;

	public UIThickness(float uniform) => Left = Top = Right = Bottom = uniform;

	public float Horizontal => Left + Right;
	public float Vertical => Top + Bottom;
}

public readonly record struct UISizeConstraint {
	public float MaxWidth {
		get;
		init {
			if (float.IsNaN(value) || value < 0f)
				throw new ArgumentOutOfRangeException(nameof(MaxWidth), "max width must not be NaN or negative");
			field = value;
		}
	}
	public float MaxHeight {
		get;
		init {
			if (float.IsNaN(value) || value < 0f)
				throw new ArgumentOutOfRangeException(nameof(MaxHeight), "max height must not be NaN or negative");
			field = value;
		}
	}

	public bool IsWidthBounded => !float.IsPositiveInfinity(MaxWidth);
	public bool IsHeightBounded => !float.IsPositiveInfinity(MaxHeight);

	public static readonly UISizeConstraint Unbounded = new UISizeConstraint {
		MaxWidth = float.PositiveInfinity,
		MaxHeight = float.PositiveInfinity,
	};

	public UISizeConstraint(float maxWidth, float maxHeight) {
		MaxWidth = maxWidth;
		MaxHeight = maxHeight;
	}

	public UISizeConstraint(SizeF size) {
		MaxWidth = size.Width;
		MaxHeight = size.Height;
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
