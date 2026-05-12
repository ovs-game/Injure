// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

public interface IOwnerScope : IAsyncDisposable {
	string OwnerID { get; }
	void Add(IDisposable disp);
	void Add(IAsyncDisposable disp);
}
