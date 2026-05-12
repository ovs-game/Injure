// SPDX-License-Identifier: MIT

using System;
using System.Numerics;

namespace Injure.UI;

public readonly ref struct UIRenderContext {
	public UIRoot Root { get; }
	public UICanvasTransform CanvasTransform { get; }
	public float TextScale => CanvasTransform.TextScale;

	internal UIRenderContext(UIRoot root, UICanvasTransform canvasTransform) {
		Root = root;
		CanvasTransform = canvasTransform;
	}

	public Vector2 LogicalToTarget(Vector2 p) => UICanvasLayout.LogicalToScreen(CanvasTransform, p);

	public RectI LogicalToScissor(RectF r) => UICanvasLayout.LogicalToScissor(CanvasTransform, r);

	public SizeI LogicalSizeToTargetPixels(SizeF size) {
		int w = Math.Max(1, (int)MathF.Ceiling(size.Width * CanvasTransform.Scale.X));
		int h = Math.Max(1, (int)MathF.Ceiling(size.Height * CanvasTransform.Scale.Y));
		return new SizeI(w, h);
	}
}
