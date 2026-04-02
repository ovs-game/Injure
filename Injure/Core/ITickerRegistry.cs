// SPDX-License-Identifier: MIT

namespace Injure.Core;

public delegate void TickerCallback(in TickCallbackInfo info);

public interface ITickerRegistry {
	TickerHandle Add(in TickerSpec spec);
	bool Remove(TickerHandle handle);
	bool Retime(TickerHandle handle, in TickerTiming timing, TickerRetimingMode mode = TickerRetimingMode.KeepPhase);
	bool Subscribe(TickerHandle handle, TickerCallback callback);
	bool Unsubscribe(TickerHandle handle, TickerCallback callback);
}
