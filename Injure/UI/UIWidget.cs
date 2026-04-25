// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Numerics;

using Injure.Graphics;

namespace Injure.UI;

public abstract class UIWidget {
	public UIWidget? Parent { get; private set; }
	public RectF LayoutRect { get; private set; }
	public SizeF DesiredSize { get; private set; }

	public bool Visible { get; set; } = true;
	public bool Enabled { get; set; } = true;
	public bool HitTestVisible { get; set; } = true;
	public bool Focusable { get; set; } = false;

	public UIThickness Margin { get; set; }
	public UIThickness Padding { get; set; }

	public virtual IReadOnlyList<UIWidget> Children => Array.Empty<UIWidget>();

	internal void AttachToParent(UIWidget? p) {
		Parent = p;
	}

	public SizeF Measure(in UISizeConstraint constraint) {
		DesiredSize = Visible ? MeasureCore(constraint) : SizeF.Zero;
		return DesiredSize;
	}
	protected abstract SizeF MeasureCore(in UISizeConstraint constraint);

	public void Arrange(in RectF rect) {
		LayoutRect = rect;
		if (Visible)
			ArrangeCore(rect);
	}
	protected virtual void ArrangeCore(in RectF rect) {
	}

	public virtual void Render(Canvas cv) {
	}

	public virtual bool HitTest(Vector2 pos) => Visible && HitTestVisible && LayoutRect.Contains(pos);
}
