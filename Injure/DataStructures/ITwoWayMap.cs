// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.DataStructures;

public interface ITwoWayMap<TLeft, TRight> : IReadOnlyTwoWayMap<TLeft, TRight> where TLeft : notnull where TRight : notnull {
	void Clear();
	void Add(TLeft left, TRight right);
	bool TryAdd(TLeft left, TRight right);
	void Set(TLeft left, TRight right);
	bool RemoveByLeft(TLeft left);
	bool RemoveByLeft(TLeft left, [NotNullWhen(true)] out TRight? right);
	bool RemoveByRight(TRight right);
	bool RemoveByRight(TRight right, [NotNullWhen(true)] out TLeft? left);
}
