// SPDX-License-Identifier: MIT

namespace Injure.Timing;

public interface IPerfProjector<out T> {
	T GetAt(PerfTick now);
}
