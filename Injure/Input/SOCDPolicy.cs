// SPDX-License-Identifier: MIT

namespace Injure.Input;

public enum SOCDPolicy {
	Last,     // most recent press wins
	First,    // oldest still-held wins
	Neutral,  // both held = 0
	Positive, // both held = +1
	Negative  // both held = -1
}
