// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

// open enum since it must be usable in the attribute
public enum AssemblyHotReloadLevel {
	None = 1,
	SafeBoundary,
	Live,
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class HotReloadLevelAttribute(AssemblyHotReloadLevel level) : Attribute {
	public AssemblyHotReloadLevel Level { get; } = level;
}
