// SPDX-License-Identifier: MIT

using System;

using Injure.Graphics;

namespace Injure.UI;

public sealed class UIDebugRect : UIWidget {
	public Color32 Fill { get; set; }
	public Color32? Stroke { get; set; }
	public float StrokeWidth { get; set; } = 1f;
	public SizeF PreferredSize { get; set; }

	public UIDebugRect(Color32 fill, SizeF preferredSize = default) {
		Fill = fill;
		PreferredSize = preferredSize;
	}

	protected override SizeF MeasureCore(in UILayoutContext ctx, in UISizeConstraint constraint) {
		float w = PreferredSize.Width;
		float h = PreferredSize.Height;

		if (w <= 0f)
			w = constraint.IsWidthBounded ? constraint.MaxWidth : 64f;
		if (h <= 0f)
			h = constraint.IsHeightBounded ? constraint.MaxHeight : 64f;

		if (constraint.IsWidthBounded)
			w = MathF.Min(w, constraint.MaxWidth);
		if (constraint.IsHeightBounded)
			h = MathF.Min(h, constraint.MaxHeight);

		return new SizeF(MathF.Max(0f, w), MathF.Max(0f, h));
	}

	public override void Render(Canvas cv, in UIRenderContext ctx) {
		if (!Visible)
			return;
		if (Stroke is Color32 stroke && StrokeWidth > 0f)
			cv.Rect(LayoutRect.Inflate(new UIThickness(StrokeWidth)), stroke);
		cv.Rect(LayoutRect, Fill);
	}
}
