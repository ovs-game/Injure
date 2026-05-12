// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Graphics;

namespace Injure.UI;

public sealed class UIPlaced : UIWidget {
	private readonly UIWidget[] children;

	public UIWidget Child { get; }
	public UIPlacement Placement { get; set; }

	public override IReadOnlyList<UIWidget> Children => children;

	public UIPlaced(UIWidget child, UIPlacement placement) {
		Child = child ?? throw new ArgumentNullException(nameof(child));
		Placement = placement;
		children = [child];
		child.AttachToParent(this);
	}

	protected override SizeF MeasureCore(in UILayoutContext ctx, in UISizeConstraint constraint) {
		UISizeConstraint childConstraint = UIPlacementUtil.GetChildMeasureConstraint(in constraint, Placement);
		SizeF childDesired = Child.Measure(in ctx, in childConstraint);
		return UIPlacementUtil.GetSurfaceDesiredSize(in constraint, Placement, childDesired);
	}

	protected override void ArrangeCore(in RectF rect) {
		RectF childRect = UIPlacementUtil.ResolveChildRect(rect, Child.DesiredSize, Placement);
		Child.Arrange(childRect);
	}

	public override void Render(Canvas cv, in UIRenderContext ctx) {
		if (Child.Visible)
			Child.Render(cv, in ctx);
	}
}
