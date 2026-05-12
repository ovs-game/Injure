// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Numerics;

using Injure.Analyzers.Attributes;

namespace Injure.UI;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UIAnchor {
	public enum Case {
		TopLeft = 1,
		Top,
		TopRight,
		Left,
		Center,
		Right,
		BottomLeft,
		Bottom,
		BottomRight
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UISizingMode {
	public enum Case {
		Auto = 1,
		Explicit,
		Fill
	}
}

public readonly record struct UIPlacement(
	UIAnchor Anchor,
	Vector2 Offset,
	UISizingMode WidthMode,
	UISizingMode HeightMode,
	float Width,
	float Height,
	UIThickness Margin = default
) {
	public static UIPlacement Center(SizeF size, Vector2 offset = default) =>
		new(UIAnchor.Center, offset, UISizingMode.Explicit, UISizingMode.Explicit, size.Width, size.Height);

	public static UIPlacement CenterAuto(Vector2 offset = default, UIThickness margin = default) =>
		new(UIAnchor.Center, offset, UISizingMode.Auto, UISizingMode.Auto, 0f, 0f, margin);

	public static UIPlacement AnchorAt(UIAnchor anchor, Vector2 offset, SizeF size, UIThickness margin = default) =>
		new(anchor, offset, UISizingMode.Explicit, UISizingMode.Explicit, size.Width, size.Height, margin);

	public static UIPlacement AnchorAtAuto(UIAnchor anchor, Vector2 offset = default, UIThickness margin = default) =>
		new(anchor, offset, UISizingMode.Auto, UISizingMode.Auto, 0f, 0f, margin);

	public static UIPlacement Fill(UIThickness margin = default) =>
		new(UIAnchor.TopLeft, default, UISizingMode.Fill, UISizingMode.Fill, 0f, 0f, margin);
}

public static class UIPlacementUtil {
	public static RectF ResolveChildRect(RectF parent, SizeF desired, UIPlacement p) {
		RectF area = parent.Deflate(p.Margin);
		float w = p.WidthMode.Tag switch {
			UISizingMode.Case.Auto => desired.Width,
			UISizingMode.Case.Explicit => p.Width,
			UISizingMode.Case.Fill => area.Width,
			_ => throw new UnreachableException(),
		};
		float h = p.HeightMode.Tag switch {
			UISizingMode.Case.Auto => desired.Height,
			UISizingMode.Case.Explicit => p.Height,
			UISizingMode.Case.Fill => area.Height,
			_ => throw new UnreachableException(),
		};

		w = MathF.Max(0f, w);
		h = MathF.Max(0f, h);

		Vector2 anchorPoint = getAnchorPoint(area, p.Anchor);
		Vector2 originOffset = getOriginOffset(new SizeF(w, h), p.Anchor);
		Vector2 pos = anchorPoint + p.Offset - originOffset;
		return new RectF(pos.X, pos.Y, w, h);
	}

	public static UISizeConstraint GetChildMeasureConstraint(
		in UISizeConstraint parentConstraint,
		in UIPlacement placement
	) {
		float maxW = getAxisMeasureConstraint(
			parentConstraint.IsWidthBounded,
			parentConstraint.MaxWidth,
			placement.Margin.Horizontal,
			placement.WidthMode,
			placement.Width
		);
		float maxH = getAxisMeasureConstraint(
			parentConstraint.IsHeightBounded,
			parentConstraint.MaxHeight,
			placement.Margin.Vertical,
			placement.HeightMode,
			placement.Height
		);
		return new UISizeConstraint(maxW, maxH);
	}

	public static SizeF GetSurfaceDesiredSize(
		in UISizeConstraint constraint,
		in UIPlacement placement,
		SizeF childDesired
	) {
		float w = constraint.IsWidthBounded ? constraint.MaxWidth : getUnboundedSurfaceAxisSize(
			placement.WidthMode,
			placement.Width,
			placement.Margin.Horizontal,
			childDesired.Width
		);
		float h = constraint.IsHeightBounded ? constraint.MaxHeight : getUnboundedSurfaceAxisSize(
			placement.HeightMode,
			placement.Height,
			placement.Margin.Vertical,
			childDesired.Height
		);
		return new SizeF(w, h);
	}

	private static Vector2 getAnchorPoint(RectF r, UIAnchor anchor) {
		return anchor.Tag switch {
			UIAnchor.Case.TopLeft => new Vector2(r.Left, r.Top),
			UIAnchor.Case.Top => new Vector2(r.CenterX, r.Top),
			UIAnchor.Case.TopRight => new Vector2(r.Right, r.Top),
			UIAnchor.Case.Left => new Vector2(r.Left, r.CenterY),
			UIAnchor.Case.Center => r.Center,
			UIAnchor.Case.Right => new Vector2(r.Right, r.CenterY),
			UIAnchor.Case.BottomLeft => new Vector2(r.Left, r.Bottom),
			UIAnchor.Case.Bottom => new Vector2(r.CenterX, r.Bottom),
			UIAnchor.Case.BottomRight => new Vector2(r.Right, r.Bottom),
			_ => throw new UnreachableException(),
		};
	}

	private static Vector2 getOriginOffset(SizeF s, UIAnchor anchor) {
		return anchor.Tag switch {
			UIAnchor.Case.TopLeft => Vector2.Zero,
			UIAnchor.Case.Top => new Vector2(s.Width * 0.5f, 0f),
			UIAnchor.Case.TopRight => new Vector2(s.Width, 0f),
			UIAnchor.Case.Left => new Vector2(0f, s.Height * 0.5f),
			UIAnchor.Case.Center => new Vector2(s.Width * 0.5f, s.Height * 0.5f),
			UIAnchor.Case.Right => new Vector2(s.Width, s.Height * 0.5f),
			UIAnchor.Case.BottomLeft => new Vector2(0f, s.Height),
			UIAnchor.Case.Bottom => new Vector2(s.Width * 0.5f, s.Height),
			UIAnchor.Case.BottomRight => new Vector2(s.Width, s.Height),
			_ => throw new UnreachableException(),
		};
	}

	private static float getAxisMeasureConstraint(
		bool parentBounded,
		float parentMax,
		float margin,
		UISizingMode mode,
		float explicitSize
	) {
		return mode.Tag switch {
			UISizingMode.Case.Explicit => MathF.Max(0f, explicitSize),
			UISizingMode.Case.Auto or UISizingMode.Case.Fill =>
				parentBounded ? MathF.Max(0f, parentMax - margin) : float.PositiveInfinity,
			_ => throw new UnreachableException(),
		};
	}

	private static float getUnboundedSurfaceAxisSize(
		UISizingMode mode,
		float explicitSize,
		float margin,
		float childDesired
	) {
		float content = mode.Tag switch {
			UISizingMode.Case.Explicit => explicitSize,
			UISizingMode.Case.Auto or UISizingMode.Case.Fill => childDesired,
			_ => throw new UnreachableException(),
		};
		return MathF.Max(0f, content + margin);
	}
}
