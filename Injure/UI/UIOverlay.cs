// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Graphics;

namespace Injure.UI;

public sealed class UIOverlay : UIWidget {
	private readonly List<UIWidget> children = new();
	public override IReadOnlyList<UIWidget> Children => children;

	public void Add(UIWidget child) {
		ArgumentNullException.ThrowIfNull(child);
		child.AttachToParent(this);
		children.Add(child);
	}

	protected override SizeF MeasureCore(in UILayoutContext ctx, in UISizeConstraint constraint) {
		float w = constraint.IsWidthBounded ? constraint.MaxWidth : 0f;
		float h = constraint.IsHeightBounded ? constraint.MaxHeight : 0f;

		foreach (UIWidget child in children) {
			SizeF s = child.Measure(in ctx, constraint);
			if (!constraint.IsWidthBounded)
				w = MathF.Max(w, s.Width);
			if (!constraint.IsHeightBounded)
				h = MathF.Max(h, s.Height);
		}

		return new SizeF(w, h);
	}

	protected override void ArrangeCore(in RectF rect) {
		foreach (UIWidget child in children)
			child.Arrange(rect);
	}

	public override void Render(Canvas cv, in UIRenderContext ctx) {
		foreach (UIWidget child in children)
			if (child.Visible)
				child.Render(cv, in ctx);
	}
}
