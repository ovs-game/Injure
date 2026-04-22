// SPDX-License-Identifier: MIT

namespace Injure.Scheduling;

public delegate void TickerCallback(in TickCallbackInfo info);

public interface ITickerRegistry {
	TickerHandle Add(in TickerSpec spec);
	bool Remove(TickerHandle handle);
	bool Retime(TickerHandle handle, in TickerTiming timing, TickerRetimingMode mode);
	bool Subscribe(TickerHandle handle, TickerCallback callback);
	bool Unsubscribe(TickerHandle handle, TickerCallback callback);
}
