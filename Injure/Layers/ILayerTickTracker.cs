// SPDX-License-Identifier: MIT

using Injure.Timing;

namespace Injure.Layers;

public interface ILayerTickTracker {
	T Track<T>(T obj) where T : class, ITickTimestampReceiver;
}
