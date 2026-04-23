// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

namespace Injure.ModUtils;

public sealed class OwnerOrderingException(string message) : Exception(message) {
}

public sealed class OwnerOrderedEntry<T> {
	private readonly string[] beforeOwners;
	private readonly string[] afterOwners;

	public T Item { get; }
	public string OwnerID { get; }
	public string LocalID { get; }
	public int LocalPriority { get; }
	public IReadOnlyList<string>? BeforeOwners => beforeOwners;
	public IReadOnlyList<string>? AfterOwners => afterOwners;

	public OwnerOrderedEntry(T item, string ownerID, string localID,
		int localPriority = 0, IEnumerable<string>? beforeOwners = null, IEnumerable<string>? afterOwners = null) {
		ArgumentNullException.ThrowIfNull(item);
		if (string.IsNullOrWhiteSpace(ownerID))
			throw new ArgumentException("owner ID cannot be null/empty/whitespace", nameof(ownerID));
		if (string.IsNullOrWhiteSpace(localID))
			throw new ArgumentException("local ID cannot be null/empty/whitespace", nameof(localID));
		Item = item;
		OwnerID = ownerID;
		LocalID = localID;
		LocalPriority = localPriority;
		this.beforeOwners = fold(beforeOwners, nameof(beforeOwners));
		this.afterOwners = fold(afterOwners, nameof(afterOwners));
	}

	public OwnerOrderedEntry<TOut> Convert<TOut>(Func<T, TOut> conv) {
		ArgumentNullException.ThrowIfNull(conv);
		return new OwnerOrderedEntry<TOut>(conv(Item), OwnerID, LocalID, LocalPriority, BeforeOwners, AfterOwners);
	}

	private static string[] fold(IEnumerable<string>? owners, string paramName) {
		if (owners is null)
			return Array.Empty<string>();
		HashSet<string> seen = new(StringComparer.Ordinal);
		List<string> list = new();
		foreach (string owner in owners) {
			if (string.IsNullOrWhiteSpace(owner))
				throw new ArgumentException("owner list cannot contain null/empty/whitespace strings", paramName);
			if (!seen.Add(owner))
				throw new ArgumentException("owner list cannot contain duplicates", paramName);
			list.Add(owner);
		}
		return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
	}
}

public static class OwnerOrderedSorter {
	private sealed class Node<T>(string ownerID) {
		public readonly string OwnerID = ownerID;
		public readonly HashSet<string> LocalIDs = new(StringComparer.Ordinal);
		public readonly HashSet<string> Outgoing = new(StringComparer.Ordinal);
		public readonly List<OwnerOrderedEntry<T>> Items = new();
		public int InDegree;
	}

	private enum VisitState {
		Visiting,
		Done
	}

	public static T[] Sort<T>(IReadOnlyList<OwnerOrderedEntry<T>> entries) {
		Dictionary<string, Node<T>> nodes = new(StringComparer.Ordinal);
		foreach (OwnerOrderedEntry<T> entry in entries) {
			if (!nodes.TryGetValue(entry.OwnerID, out Node<T>? node)) {
				node = new Node<T>(entry.OwnerID);
				nodes.Add(entry.OwnerID, node);
			}
			if (!node.LocalIDs.Add(entry.LocalID))
				throw new OwnerOrderingException($"duplicate LocalID '{entry.LocalID}' for owner '{entry.OwnerID}'");
			node.Items.Add(entry);
		}

		foreach (Node<T> node in nodes.Values) {
			foreach (OwnerOrderedEntry<T> entry in node.Items) {
				if (entry.BeforeOwners is not null)
					foreach (string before in entry.BeforeOwners)
						if (nodes.ContainsKey(before)) // don't throw if a referenced owner isn't known yet
							addEdge(nodes, node.OwnerID, before);
				if (entry.AfterOwners is not null)
					foreach (string after in entry.AfterOwners)
						if (nodes.ContainsKey(after)) // don't throw if a referenced owner isn't known yet
							addEdge(nodes, after, node.OwnerID);
			}
		}

		SortedSet<string> ready = new(StringComparer.Ordinal);
		foreach (Node<T> node in nodes.Values)
			if (node.InDegree == 0)
				ready.Add(node.OwnerID);

		List<Node<T>> ordered = new(nodes.Count);
		while (ready.Count > 0) {
			string ownerID = ready.Min!;
			ready.Remove(ownerID);
			Node<T> node = nodes[ownerID];
			ordered.Add(node);
			foreach (string nextID in node.Outgoing) {
				Node<T> next = nodes[nextID];
				if (--next.InDegree == 0)
					ready.Add(nextID);
			}
		}
		if (ordered.Count != nodes.Count) {
			List<string>? cycle = findCycle(nodes);
			string msg = "ordering constraints are unsatisfiable";
			if (cycle is not null)
				msg += ": " + string.Join(" -> ", cycle) + " -> " + cycle[0];
			throw new OwnerOrderingException(msg);
		}

		// topo sort done, do a second pass sorting by local priority
		T[] result = new T[entries.Count];
		int resultidx = 0;
		foreach (Node<T> node in ordered) {
			node.Items.Sort(static (OwnerOrderedEntry<T> a, OwnerOrderedEntry<T> b) => {
				int n = b.LocalPriority.CompareTo(a.LocalPriority);
				if (n != 0)
					return n;
				return StringComparer.Ordinal.Compare(a.LocalID, b.LocalID);
			});
			foreach (OwnerOrderedEntry<T> ent in node.Items)
				result[resultidx++] = ent.Item;
		}
		return result;
	}

	private static void addEdge<T>(Dictionary<string, Node<T>> nodes, string fromOwnerID, string toOwnerID) {
		if (string.IsNullOrWhiteSpace(fromOwnerID) || string.IsNullOrWhiteSpace(toOwnerID))
			throw new OwnerOrderingException("null/empty/whitespace references are not allowed");
		if (StringComparer.Ordinal.Equals(fromOwnerID, toOwnerID))
			throw new OwnerOrderingException($"owner '{fromOwnerID}' has a self-reference");
		if (!nodes.TryGetValue(fromOwnerID, out Node<T>? fromNode))
			throw new OwnerOrderingException($"unknown source owner '{fromOwnerID}'");
		if (!nodes.TryGetValue(toOwnerID, out Node<T>? toNode)) {
			throw new OwnerOrderingException($"unknown target owner '{toOwnerID}'");
		}
		if (fromNode.Outgoing.Add(toOwnerID))
			toNode.InDegree++;
	}

	private static List<string>? findCycle<T>(Dictionary<string, Node<T>> nodes) {
		HashSet<string> remaining = new(
			nodes.Where(static (kvp) => kvp.Value.InDegree > 0).Select(static (kvp) => kvp.Key),
			StringComparer.Ordinal
		);
		Dictionary<string, VisitState> state = new(StringComparer.Ordinal);
		// not a Stack<string> so we can find a node already on the stack easily
		List<string> stack = new();
		Dictionary<string, int> stackIndex = new(StringComparer.Ordinal);
		List<string>? cycle = null;

		// iterate by Ordinal order instead of the nondeterministic hashset iter order
		foreach (string id in remaining.OrderBy(x => x, StringComparer.Ordinal)) {
			if (state.ContainsKey(id))
				continue;
			dfs(id);
			if (cycle is not null)
				return cycle;
		}
		return null;

		void dfs(string id) {
			state[id] = VisitState.Visiting;
			stackIndex[id] = stack.Count;
			stack.Add(id);
			foreach (string nextID in nodes[id].Outgoing.Where(remaining.Contains).OrderBy(x => x, StringComparer.Ordinal)) {
				if (!state.TryGetValue(nextID, out VisitState nextState)) {
					dfs(nextID);
					if (cycle is not null)
						return;
				} else if (nextState == VisitState.Visiting) {
					int start = stackIndex[nextID];
					cycle = stack.GetRange(start, stack.Count - start);
					return;
				}
			}
			stack.RemoveAt(stack.Count - 1);
			stackIndex.Remove(id);
			state[id] = VisitState.Done;
		}
	}
}

// TODO: document better that the model is single-writer multiple-reader and that
// registry/unregistry needs to be externally mutexed
public sealed class OwnerOrderedRegistry<T> {
	private readonly Dictionary<ulong, OwnerOrderedEntry<T>> entries = new();
	private readonly Dictionary<string, HashSet<string>> localIDsByOwner =
		new(StringComparer.Ordinal);
	private ulong nextID = 0; // first ID will be 1 since this gets incremented upfront
	private T[] snapshot = Array.Empty<T>();

	public ulong Register(OwnerOrderedEntry<T> entry) {
		ArgumentNullException.ThrowIfNull(entry);
		bool createdLocalIDSet = false;
		if (!localIDsByOwner.TryGetValue(entry.OwnerID, out HashSet<string>? localIDs)) {
			localIDs = new HashSet<string>(StringComparer.Ordinal);
			localIDsByOwner.Add(entry.OwnerID, localIDs);
			createdLocalIDSet = true;
		}
		if (!localIDs.Add(entry.LocalID))
			throw new OwnerOrderingException($"duplicate LocalID '{entry.LocalID}' for owner '{entry.OwnerID}'");
		ulong id = nextID + 1;
		entries.Add(id, entry);
		try {
			T[] s = OwnerOrderedSorter.Sort(entries.Values.ToArray());
			Volatile.Write(ref snapshot, s);
		} catch {
			entries.Remove(id);
			localIDs.Remove(entry.LocalID);
			if (createdLocalIDSet)
				localIDsByOwner.Remove(entry.OwnerID);
			throw;
		}
		nextID = id; // do you understand yet why this isn't thread safe
		return id;
	}

	public bool Unregister(ulong id, [NotNullWhen(true)] out OwnerOrderedEntry<T>? removed) {
		if (!entries.Remove(id, out removed))
			return false;
		HashSet<string> localIDs = localIDsByOwner[removed.OwnerID];
		localIDs.Remove(removed.LocalID);
		if (localIDs.Count == 0)
			localIDsByOwner.Remove(removed.OwnerID);
		T[] s = OwnerOrderedSorter.Sort(entries.Values.ToArray());
		Volatile.Write(ref snapshot, s);
		return true;
	}

	public IReadOnlyList<T> ReadSnapshot() => Volatile.Read(ref snapshot);
}
