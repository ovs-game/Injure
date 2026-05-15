// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Injure.ModKit.Runtime;

internal static class ReloadClosure {
	public static HashSet<string> Compute(IReadOnlyCollection<string> roots, IReadOnlyDictionary<string, string[]> reloadDependentsByTarget) {
		HashSet<string> result = new(StringComparer.Ordinal);
		Queue<string> queue = new();

		foreach (string root in roots)
			if (result.Add(root))
				queue.Enqueue(root);

		while (queue.Count != 0) {
			string current = queue.Dequeue();
			if (!reloadDependentsByTarget.TryGetValue(current, out string[]? dependents))
				continue;
			foreach (string dependent in dependents)
				if (result.Add(dependent))
					queue.Enqueue(dependent);
		}

		return result;
	}
}
