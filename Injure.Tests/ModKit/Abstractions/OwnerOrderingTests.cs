// SPDX-License-Identifier: MIT

using System;

using Injure.ModKit.Abstractions;

namespace Injure.Tests.ModKit.Abstractions;

public class OwnerOrderingTests {
	[Fact]
	public void LocalPriorityWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("third",  "owner", "b", 0),
			new("first",  "owner", "a", 2),
			new("second", "owner", "c", 1),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second", "third"], result);
	}

	[Fact]
	public void TiebreakingWithLocalIDWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("B", "owner", "b", 0),
			new("A", "owner", "a", 0),
			new("C", "owner", "c", 0),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["A", "B", "C"], result);
	}

	[Fact]
	public void BeforeOwnersWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("second", "ownerA", "a"),
			new("first",  "ownerB", "b", beforeOwners: ["ownerA"]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void AfterOwnersWorks() {
		OwnerOrderedEntry<string>[] entries = [
			new("second", "ownerA", "a", afterOwners: ["ownerB"]),
			new("first",  "ownerB", "b"),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void UnknownOwnerReferenceIsIgnored() {
		OwnerOrderedEntry<string>[] entries = [
			new("first",  "ownerA", "a", beforeOwners: ["missing"]),
			new("second", "ownerB", "b"),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["first", "second"], result);
	}

	[Fact]
	public void DuplicateLocalIDWithinOwnerThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("x1", "owner", "dup"),
			new("x2", "owner", "dup"),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("duplicate LocalID", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void SelfReferenceThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("self-ref", "ownerA", "a", beforeOwners: ["ownerA"]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("self-reference", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void SimpleCycleThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("cycle 1", "ownerA", "a", beforeOwners: ["ownerB"]),
			new("cycle 2", "ownerB", "b", beforeOwners: ["ownerA"]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("unsatisfiable", ex.Message, StringComparison.Ordinal);
		Assert.Contains("ownerA -> ownerB -> ownerA", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void LongerCycleThrows() {
		OwnerOrderedEntry<string>[] entries = [
			new("cycle 1", "ownerA", "a", beforeOwners: ["ownerB"]),
			new("cycle 2", "ownerB", "b", beforeOwners: ["ownerC"]),
			new("cycle 3", "ownerC", "c", beforeOwners: ["ownerD"]),
			new("cycle 4", "ownerD", "d", beforeOwners: ["ownerA"]),
		];
		OwnerOrderingException ex = Assert.Throws<OwnerOrderingException>(() => OwnerOrderedSorter.Sort(entries));
		Assert.Contains("unsatisfiable", ex.Message, StringComparison.Ordinal);
		Assert.Contains("ownerA -> ownerB -> ownerC -> ownerD -> ownerA", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void OwnerOrderingThenLocalPriority() {
		OwnerOrderedEntry<string>[] entries = [
			new("ownerA second", "ownerA", "a", 0),
			new("ownerA first",  "ownerA", "z", 1),
			new("ownerB first",  "ownerB", "b", 0, beforeOwners: ["ownerA"]),
		];
		string[] result = OwnerOrderedSorter.Sort(entries);
		Assert.Equal(["ownerB first", "ownerA first", "ownerA second"], result);
	}
}
