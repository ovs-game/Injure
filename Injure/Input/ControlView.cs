// SPDX-License-Identifier: MIT

using System;

namespace Injure.Input;

public readonly ref struct ControlView(ActionStateView actions, ReadOnlySpan<ControlEvent> events, PointerState pointer) {
	public ActionStateView Actions { get; } = actions;
	public ReadOnlySpan<ControlEvent> Events { get; } = events;
	public PointerState Pointer { get; } = pointer;
}
