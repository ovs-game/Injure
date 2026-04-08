// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Injure.DataStructures;

public interface IReadOnlyTwoWayMap<TLeft, TRight> : IReadOnlyCollection<(TLeft Left, TRight Right)> where TLeft : notnull where TRight : notnull {
	bool ContainsLeft(TLeft left);
	bool ContainsRight(TRight right);
	bool TryGetByLeft(TLeft left, [NotNullWhen(true)] out TRight? right);
	bool TryGetByRight(TRight right, [NotNullWhen(true)] out TLeft? left);
	TRight GetByLeft(TLeft left);
	TLeft GetByRight(TRight right);
}
