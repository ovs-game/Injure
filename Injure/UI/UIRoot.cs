// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Numerics;

using Injure.Core;
using Injure.Graphics;
using Injure.Input;
using Injure.Timing;

namespace Injure.UI;

public sealed class UIRoot(UICanvasPolicy canvasPolicy) {
	public UICanvasPolicy CanvasPolicy { get; set; } = canvasPolicy;
	public UIWidget? RootWidget { get; set; }

	public UIWidget? HoveredWidget { get; private set; }
	public UIWidget? FocusedWidget { get; private set; }
	public UIWidget? CapturedPointerWidget { get; private set; }
	public UICanvasTransform CanvasTransform { get; private set; }

	public void Focus(UIWidget? widget) {
		if (ReferenceEquals(FocusedWidget, widget))
			return;
		UIWidget? old = FocusedWidget;
		FocusedWidget = widget;
		if (old is IUIFocusSink oldSink) {
			UIEventContext ctx = UIEventContext.Create(this, old);
			oldSink.OnFocus(ref ctx, new UIFocusEvent(false));
		}
		if (widget is IUIFocusSink newSink) {
			UIEventContext ctx = UIEventContext.Create(this, widget);
			newSink.OnFocus(ref ctx, new UIFocusEvent(true));
		}
	}

	public void CapturePointer(UIWidget widget) {
		ArgumentNullException.ThrowIfNull(widget);
		CapturedPointerWidget = widget;
	}

	public void ReleasePointerCapture(UIWidget widget) {
		if (ReferenceEquals(CapturedPointerWidget, widget))
			CapturedPointerWidget = null;
	}

	public void Update(in ControlView input, in WindowState window) {
		SizeI drawable = new(window.DrawableWidth, window.DrawableHeight);
		CanvasTransform = UICanvasLayout.Compute(CanvasPolicy, drawable);

		if (RootWidget is null)
			return;
		UILayoutContext ctx = new(this, CanvasTransform);
		RootWidget.Measure(in ctx, new UISizeConstraint(CanvasTransform.LogicalRect.Size));
		RootWidget.Arrange(CanvasTransform.LogicalRect);

		processControlEvents(input);
	}

	public void Render(Canvas cv) {
		if (RootWidget is null || !RootWidget.Visible)
			return;

		using (cv.PushParams(
			Scissor: CanvasScissor.Set(CanvasTransform.ViewportRect),
			Transform: Matrix3x2.CreateScale(CanvasTransform.Scale) * Matrix3x2.CreateTranslation(CanvasTransform.ViewportRect.Position.ToVector2())
		)) {
			UIRenderContext ctx = new(this, CanvasTransform);
			RootWidget.Render(cv, in ctx);
		}
	}

	private void processControlEvents(in ControlView input) {
		for (int i = 0; i < input.Events.Length; i++) {
			switch (input.Events[i]) {
			case PointerMoveControlEvent move:
				handlePointerMove(move);
				break;
			case ButtonActionEvent btn when btn.Info.Kind == ButtonActionEventInfoKind.Pointer:
				handlePointerButton(btn);
				break;
			case ImpulseAxisActionEvent axis when axis.Info.Kind == ImpulseAxisActionEventInfoKind.Pointer:
				handleScroll(axis);
				break;
			case TextEnteredControlEvent text:
				handleText(text);
				break;
			}
		}
	}

	private void handlePointerMove(PointerMoveControlEvent ev) {
		Vector2 logicalPos = UICanvasLayout.ScreenToLogical(CanvasTransform, new Vector2(ev.X, ev.Y));
		Vector2 logicalDelta = ev.Delta / CanvasTransform.Scale;
		UIWidget? target = CapturedPointerWidget ?? HitTest(logicalPos);
		updateHover(logicalPos);
		if (target is IUIPointerMoveSink sink) {
			UIEventContext ctx = UIEventContext.Create(this, target);
			sink.OnPointerMove(ref ctx, new UIPointerMoveEvent(
				Tick: ev.Tick,
				Position: logicalPos,
				Delta: logicalDelta
			));
		}
	}

	private void handlePointerButton(ButtonActionEvent ev) {
		PointerButtonActionInfo p = ev.Info.Pointer;
		Vector2 logicalPos = UICanvasLayout.ScreenToLogical(CanvasTransform, new Vector2(p.X, p.Y));
		UIWidget? target = CapturedPointerWidget ?? HitTest(logicalPos);
		if (target is null)
			return;
		if (target.Focusable)
			Focus(target);
		if (target is IUIPointerButtonSink sink) {
			UIEventContext ctx = UIEventContext.Create(this, target);
			sink.OnPointerButton(ref ctx, new UIPointerButtonEvent(
				Tick: ev.Tick,
				Position: logicalPos,
				Button: PointerButton.Left, // TODO
				Edge: ev.Edge,
				Clicks: p.Clicks
			));
		}
	}

	private void handleScroll(ImpulseAxisActionEvent ev) {
		PointerImpulseAxisActionInfo p = ev.Info.Pointer;
		Vector2 logicalPos = UICanvasLayout.ScreenToLogical(CanvasTransform, new Vector2(p.X, p.Y));

		UIWidget? target = HoveredWidget ?? FocusedWidget;
		if (target is not IUIScrollSink sink)
			return;

		Vector2 amount = new(0f, ev.Amount); // TODO: assumes this is a scroll-y action
		UIEventContext ctx = UIEventContext.Create(this, target);
		sink.OnScroll(ref ctx, new UIScrollEvent(
			Tick: ev.Tick,
			Position: logicalPos,
			Amount: amount,
			IntegerAmount: new Vector2Int(0, p.IntegerAmount)
		));
	}

	private void handleText(TextEnteredControlEvent ev) {
		if (FocusedWidget is not IUITextInputSink sink)
			return;

		UIEventContext ctx = UIEventContext.Create(this, FocusedWidget);
		sink.OnTextInput(ref ctx, new UITextInputEvent(
			Tick: ev.Tick,
			Text: ev.Text
		));
	}

	private void updateHover(Vector2 pos) {
		UIWidget? hit = HitTest(pos);
		if (ReferenceEquals(hit, HoveredWidget))
			return;

		UIWidget? old = HoveredWidget;
		HoveredWidget = hit;

		if (old is IUIHoverSink oldSink) {
			UIEventContext ctx = UIEventContext.Create(this, old);
			oldSink.OnHover(ref ctx, new UIHoverEvent(
				Tick: MonoTick.Zero,
				Position: pos,
				Hovered: false
			));
		}
		if (hit is IUIHoverSink newSink) {
			UIEventContext ctx = UIEventContext.Create(this, hit);
			newSink.OnHover(ref ctx, new UIHoverEvent(
				Tick: MonoTick.Zero,
				Position: pos,
				Hovered: true
			));
		}
	}

	public UIWidget? HitTest(Vector2 pos) {
		if (RootWidget is null || !RootWidget.Visible)
			return null;
		return hitTestRecursive(RootWidget, pos);
	}

	private static UIWidget? hitTestRecursive(UIWidget widget, Vector2 pos) {
		if (!widget.HitTest(pos))
			return null;

		IReadOnlyList<UIWidget> children = widget.Children;
		for (int i = children.Count - 1; i >= 0; i--) {
			UIWidget child = children[i];
			UIWidget? hit = hitTestRecursive(child, pos);
			if (hit is not null)
				return hit;
		}

		return widget;
	}
}
