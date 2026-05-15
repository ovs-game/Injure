// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.MonoMod;

public sealed class ILPatternNotFoundException(
	string message,
	string? target = null,
	string? expected = null,
	string? startIndex = null,
	string? searchDirection = null
) : InvalidOperationException(message) {
	public string? Target { get; } = target;
	public string? Expected { get; } = expected;
	public string? StartIndex { get; } = startIndex;
	public string? SearchDirection { get; } = searchDirection;
}
