// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Injure.ModKit;

internal sealed class ClearableDisposable<T>(T val) : IDisposable where T : class, IDisposable {
	private T? val = val;

	public void Dispose() {
		T? v = Interlocked.Exchange(ref val, null);
		v?.Dispose();
	}
}
