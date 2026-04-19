// SPDX-License-Identifier: MIT

namespace Injure.Timing;

public interface IMonoProjector<out T> {
	T GetAt(MonoTick now);
}
