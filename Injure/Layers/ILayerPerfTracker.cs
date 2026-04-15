// SPDX-License-Identifier: MIT

using Injure.Timing;

namespace Injure.Layers;

public interface ILayerPerfTracker {
	T Track<T>(T obj) where T : class, IPerfUpdateReceiver;
}
