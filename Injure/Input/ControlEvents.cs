// SPDX-License-Identifier: MIT

using System;
using System.Numerics;

using Injure.Analyzers.Attributes;
using Injure.Timing;

namespace Injure.Input;

[ClosedEnum]
public readonly partial struct ButtonActionEventInfoKind {
	public enum Case {
		None,
		Pointer,
	}
}

public readonly record struct PointerButtonActionInfo(
	float X,
	float Y,
	int Clicks
);

public readonly struct ButtonActionEventInfo {
	private readonly PointerButtonActionInfo pointer;

	public ButtonActionEventInfoKind Kind { get; }
	public PointerButtonActionInfo Pointer => Kind == ButtonActionEventInfoKind.Pointer ? pointer :
		throw new InvalidOperationException("this button action doesn't come from a pointer button");

	private ButtonActionEventInfo(ButtonActionEventInfoKind kind, PointerButtonActionInfo pointer) {
		Kind = kind;
		this.pointer = pointer;
	}

	public static readonly ButtonActionEventInfo None = default;
	public static ButtonActionEventInfo FromPointer(float x, float y, int clicks) =>
		new(ButtonActionEventInfoKind.Pointer, new PointerButtonActionInfo(x, y, clicks));
	public bool TryGetPointer(out PointerButtonActionInfo info) {
		if (Kind == ButtonActionEventInfoKind.Pointer) {
			info = pointer;
			return true;
		}
		info = default;
		return false;
	}
}

[ClosedEnum]
public readonly partial struct ImpulseAxisActionEventInfoKind {
	public enum Case {
		None,
		Pointer,
	}
}

public readonly record struct PointerImpulseAxisActionInfo(
	float X,
	float Y,
	int IntegerAmount
);

public readonly struct ImpulseAxisActionEventInfo {
	private readonly PointerImpulseAxisActionInfo pointer;

	public ImpulseAxisActionEventInfoKind Kind { get; }
	public PointerImpulseAxisActionInfo Pointer => Kind == ImpulseAxisActionEventInfoKind.Pointer ? pointer :
		throw new InvalidOperationException("this impulse axis action doesn't come from pointer scroll");

	private ImpulseAxisActionEventInfo(ImpulseAxisActionEventInfoKind kind, PointerImpulseAxisActionInfo pointer) {
		Kind = kind;
		this.pointer = pointer;
	}

	public static readonly ImpulseAxisActionEventInfo None = default;
	public static ImpulseAxisActionEventInfo FromPointer(float x, float y, int clicks) =>
		new(ImpulseAxisActionEventInfoKind.Pointer, new PointerImpulseAxisActionInfo(x, y, clicks));
	public bool TryGetPointer(out PointerImpulseAxisActionInfo info) {
		if (Kind == ImpulseAxisActionEventInfoKind.Pointer) {
			info = pointer;
			return true;
		}
		info = default;
		return false;
	}
}

public abstract record ControlEvent(MonoTick Tick);

public sealed record ButtonActionEvent(
	MonoTick Tick,
	ActionID Action,
	EdgeType Edge,
	ButtonActionEventInfo Info = default
) : ControlEvent(Tick);

public sealed record StateAxisActionEvent(
	MonoTick Tick,
	ActionID Action,
	float Value
) : ControlEvent(Tick);

public sealed record StateAxis2DActionEvent(
	MonoTick Tick,
	ActionID Action,
	Vector2 Value
) : ControlEvent(Tick);

public sealed record ImpulseAxisActionEvent(
	MonoTick Tick,
	ActionID Action,
	float Amount,
	ImpulseAxisActionEventInfo Info = default
) : ControlEvent(Tick);

public sealed record PointerMoveControlEvent(
	MonoTick Tick,
	float X,
	float Y,
	Vector2 Delta
) : ControlEvent(Tick);

public sealed record TextEnteredControlEvent(
	MonoTick Tick,
	string Text
) : ControlEvent(Tick);
