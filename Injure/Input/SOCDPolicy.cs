// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Input;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct SOCDPolicy {
	public enum Case {
		Last = 1, // most recent press wins
		First,    // oldest still-held wins
		Neutral,  // both held = 0
		Positive, // both held = +1
		Negative, // both held = -1
	}
}
