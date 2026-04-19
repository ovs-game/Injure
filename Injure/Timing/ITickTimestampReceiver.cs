// SPDX-License-Identifier: MIT

namespace Injure.Timing;

public interface ITickTimestampReceiver {
	void Update(MonoTick now);
}
