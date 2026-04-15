// SPDX-License-Identifier: MIT

namespace Injure.Timing;

public interface IPerfUpdateReceiver {
	void Update(PerfTick now);
}
