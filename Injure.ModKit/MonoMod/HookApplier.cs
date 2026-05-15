// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Injure.ModKit.Abstractions;
using Injure.ModKit.Runtime;

namespace Injure.ModKit.MonoMod;

internal static class HookApplier<TGameApi> {
	public static async ValueTask ApplyLoadHooksAsync(
		IReadOnlyCollection<LoadedCodeMod<TGameApi>> mods,
		int maxParallelDomains,
		CancellationToken ct
	) {
		List<PatchDeclaration> patches = new();
		foreach (LoadedCodeMod<TGameApi> mod in mods)
			patches.AddRange(mod.LoadHooks.Snapshot());
		Dictionary<string, OwnerScope> scopes = mods.ToDictionary(
			static m => m.Staged.Manifest.OwnerID,
			static m => m.OwnerScope,
			StringComparer.Ordinal
		);
		await applyAsync(patches, scopes, maxParallelDomains, ct).ConfigureAwait(false);
	}

	// a failed parallel can retain the delegate body, thrown exception, and captured declarations,
	// so this can't be inlined
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async ValueTask applyAsync(
		List<PatchDeclaration> patches,
		IReadOnlyDictionary<string, OwnerScope> scopes,
		int maxParallelDomains,
		CancellationToken ct
	) {
		Dictionary<string, List<PatchDeclaration>> byDomain = new(StringComparer.Ordinal);
		foreach (PatchDeclaration patch in patches) {
			if (!byDomain.TryGetValue(patch.Order.OrderDomain, out List<PatchDeclaration>? list)) {
				list = new List<PatchDeclaration>();
				byDomain.Add(patch.Order.OrderDomain, list);
			}
			list.Add(patch);
		}
		await Parallel.ForEachAsync(
			byDomain.Values,
			new ParallelOptions {
				MaxDegreeOfParallelism = maxParallelDomains,
				CancellationToken = ct,
			},
			(domainDeclarations, ct) => {
				applyDomainSerially(domainDeclarations, scopes, ct);
				return ValueTask.CompletedTask;
			}
		).ConfigureAwait(false);
	}

	private static void applyDomainSerially(
		List<PatchDeclaration> patches,
		IReadOnlyDictionary<string, OwnerScope> ownerScopes,
		CancellationToken ct
	) {
		patches.Sort(compare);
		foreach (PatchDeclaration patch in patches) {
			ct.ThrowIfCancellationRequested();
			if (!ownerScopes.TryGetValue(patch.OwnerID, out OwnerScope? ownerScope))
				throw new InternalStateException($"owner '{patch.OwnerID}' has no owner scope");
			patch.Commit(ownerScope);
		}
	}

	private static int compare(PatchDeclaration a, PatchDeclaration b) {
		int cmp = StringComparer.Ordinal.Compare(a.OwnerID, b.OwnerID);
		if (cmp != 0)
			return cmp;
		cmp = b.Order.LocalPriority.CompareTo(a.Order.LocalPriority);
		if (cmp != 0)
			return cmp;
		return StringComparer.Ordinal.Compare(a.Order.LocalID, b.Order.LocalID);
	}
}
