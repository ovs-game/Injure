// SPDX-License-Identifier: MIT

using System.Numerics;

using Injure.Input;
using Injure.Timing;

namespace Injure.UI;

public readonly record struct UIHoverEvent(
	MonoTick Tick,
	Vector2 Position,
	bool Hovered
);

public readonly record struct UIPointerMoveEvent(
	MonoTick Tick,
	Vector2 Position,
	Vector2 Delta
);

public readonly record struct UIPointerButtonEvent(
	MonoTick Tick,
	Vector2 Position,
	PointerButton Button,
	EdgeType Edge,
	int Clicks
);

public readonly record struct UIScrollEvent(
	MonoTick Tick,
	Vector2 Position,
	Vector2 Amount,
	Vector2Int IntegerAmount
);

public readonly record struct UIFocusEvent(
	bool Focused
);

public readonly record struct UITextInputEvent(
	MonoTick Tick,
	string Text
);

public ref struct UIEventContext {
	public UIRoot Root { get; private set; }
	public UIWidget Target { get; private set; }

	public bool Handled { get; set; }

	private UIEventContext(UIRoot root, UIWidget target) {
		Root = root;
		Target = target;
		Handled = false;
	}

	public static UIEventContext Create(UIRoot root, UIWidget target) => new(root, target);
	/*
	public void Focus(UIWidget? widget) => ...
	public void CapturePointer() => ...
	public void ReleasePointerCapture() => ...
	*/
}

public interface IUIHoverSink {
	void OnHover(ref UIEventContext ctx, in UIHoverEvent ev);
}

public interface IUIPointerMoveSink {
	void OnPointerMove(ref UIEventContext ctx, in UIPointerMoveEvent ev);
}

public interface IUIPointerButtonSink {
	void OnPointerButton(ref UIEventContext ctx, in UIPointerButtonEvent ev);
}

public interface IUIScrollSink {
	void OnScroll(ref UIEventContext ctx, in UIScrollEvent ev);
}

public interface IUIFocusSink {
	void OnFocus(ref UIEventContext ctx, in UIFocusEvent ev);
}

public interface IUITextInputSink {
	void OnTextInput(ref UIEventContext ctx, in UITextInputEvent ev);
}
