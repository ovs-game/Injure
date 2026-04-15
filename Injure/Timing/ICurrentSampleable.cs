// SPDX-License-Identifier: MIT

namespace Injure.Timing;

public interface ICurrentSampleable<out T> {
	T SampleCurrent();
}
