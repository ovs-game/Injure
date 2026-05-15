// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Runtime;

internal static class ModRelationshipResolver {
	public static ResolvedModGraph Resolve(IReadOnlyList<DiscoveredMod> discovered) {
		Dictionary<string, ResolvedMod> mods = new();
		foreach (DiscoveredMod mod in discovered) {
			if (mods.ContainsKey(mod.Manifest.OwnerID))
				throw new ModLoadException(mod.Manifest.OwnerID, "duplicate owner id");
			mods.Add(mod.Manifest.OwnerID, new ResolvedMod(mod.Manifest, mod.Source));
		}

		Dictionary<string, HashSet<string>> outgoing = createEmptyEdgeMap(mods.Keys);
		Dictionary<string, HashSet<string>> reloadDependents = createEmptyEdgeMap(mods.Keys);

		foreach (ResolvedMod declarer in mods.Values) {
			foreach (ModRelationshipManifest relationship in declarer.Manifest.Relationships) {
				if (relationship.Kind == ModRelationshipKind.Conflicts) {
					if (mods.ContainsKey(relationship.OwnerID))
						throw new ModLoadException(declarer.Manifest.OwnerID, $"conflicts with present owner '{relationship.OwnerID}'");
					continue;
				}

				bool targetPresent = mods.TryGetValue(relationship.OwnerID, out ResolvedMod target);
				if (!targetPresent) {
					if (relationship.Kind.Tag is ModRelationshipKind.Case.RequiresSelfAfter or ModRelationshipKind.Case.RequiresSelfBefore)
						throw new ModLoadException(declarer.Manifest.OwnerID, $"required owner '{relationship.OwnerID}' is not present");
					continue;
				}

				if (relationship.Version is Semver required && !target.Manifest.Version.CompatibleWithMinimum(required)) {
					throw new ModLoadException(
						declarer.Manifest.OwnerID,
						$"owner '{relationship.OwnerID}' version '{target.Manifest.Version}' is not compatible with required minimum '{required}'"
					);
				}

				reloadDependents[relationship.OwnerID].Add(declarer.Manifest.OwnerID);

				switch (relationship.Kind.Tag) {
				case ModRelationshipKind.Case.RequiresSelfAfter:
				case ModRelationshipKind.Case.IfPresentSelfAfter:
					addEdge(outgoing, relationship.OwnerID, declarer.Manifest.OwnerID);
					break;
				case ModRelationshipKind.Case.RequiresSelfBefore:
				case ModRelationshipKind.Case.IfPresentSelfBefore:
					addEdge(outgoing, declarer.Manifest.OwnerID, relationship.OwnerID);
					break;
				}
			}
		}

		ModWavePlan waves = buildWaves(mods.Keys, outgoing);
		ResolvedMod[] order = flattenWaves(waves, mods);

		return new ResolvedModGraph(
			mods,
			freeze(outgoing),
			freeze(reloadDependents),
			waves,
			order
		);
	}

	private static Dictionary<string, HashSet<string>> createEmptyEdgeMap(IEnumerable<string> ids) {
		Dictionary<string, HashSet<string>> map = new();
		foreach (string id in ids)
			map.Add(id, new HashSet<string>());
		return map;
	}

	private static void addEdge(Dictionary<string, HashSet<string>> outgoing, string from, string to) {
		if (from.Equals(to))
			throw new ModLoadException(from, "relationship creates a self-order edge");
		outgoing[from].Add(to);
	}

	private static ModWavePlan buildWaves(IEnumerable<string> ids, Dictionary<string, HashSet<string>> outgoing) {
		Dictionary<string, int> inDegree = new();
		foreach (string id in ids)
			inDegree.Add(id, 0);
		foreach (KeyValuePair<string, HashSet<string>> pair in outgoing)
			foreach (string target in pair.Value)
				inDegree[target]++;

		SortedSet<string> ready = new(StringComparer.Ordinal);
		foreach (string id in inDegree.Keys)
			if (inDegree[id] == 0)
				ready.Add(id);

		List<string[]> waves = new();
		int emitted = 0;
		while (ready.Count != 0) {
			string[] wave = ready.ToArray();
			ready.Clear();
			Array.Sort(wave, static (a, b) => StringComparer.Ordinal.Compare(a, b));
			waves.Add(wave);
			emitted += wave.Length;

			foreach (string id in wave) {
				foreach (string next in outgoing[id].OrderBy(static x => x, StringComparer.Ordinal)) {
					inDegree[next]--;
					if (inDegree[next] == 0)
						ready.Add(next);
				}
			}
		}

		if (emitted != inDegree.Count)
			throw new ModLoadException("relationship ordering graph contains a cycle");
		return new ModWavePlan(waves.ToArray());
	}

	private static ResolvedMod[] flattenWaves(ModWavePlan waves, Dictionary<string, ResolvedMod> mods) {
		List<ResolvedMod> result = new();
		foreach (IReadOnlyList<string> wave in waves.Waves)
			foreach (string id in wave)
				result.Add(mods[id]);
		return result.ToArray();
	}

	private static FrozenDictionary<string, string[]> freeze(Dictionary<string, HashSet<string>> map) {
		Dictionary<string, string[]> dict = new(StringComparer.Ordinal);
		foreach (KeyValuePair<string, HashSet<string>> pair in map) {
			string[] values = pair.Value.ToArray();
			Array.Sort(values, static (a, b) => StringComparer.Ordinal.Compare(a, b));
			dict.Add(pair.Key, values);
		}
		return dict.ToFrozenDictionary(StringComparer.Ordinal);
	}
}
