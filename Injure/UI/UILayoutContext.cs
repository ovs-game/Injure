// SPDX-License-Identifier: MIT

namespace Injure.UI;

public readonly ref struct UILayoutContext {
	public UIRoot Root { get; }
	public UICanvasTransform CanvasTransform { get; }
	public float TextScale => CanvasTransform.TextScale;

	internal UILayoutContext(UIRoot root, UICanvasTransform canvasTransform) {
		Root = root;
		CanvasTransform = canvasTransform;
	}
}
