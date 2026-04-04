// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreDependencyTests {
	private const string ownerID = "test";

	[Fact]
	public void ResolverNotHandledDoesntLeakDeps() {
		AssetStore store = new AssetStore();
		store.RegisterSource(ownerID, new TestSource(new TestDependency("dep-a")), "source");
		store.RegisterResolver(ownerID, new FetchThenNotHandledResolver(new TestDependency("dep-b")), "resolver-a", localPriority: 1);
		store.RegisterResolver(ownerID, new TestResolver(new TestDependency("dep-c")), "resolver-b", localPriority: 0);
		store.RegisterCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		AssetLease<TestAsset> lease = asset.Borrow();
		Assert.Equal([new TestDependency("dep-a"), new TestDependency("dep-c")], lease.Dependencies.CastDepsToArray<TestDependency>());
	}

	[Fact]
	public void CreatorNotHandledDoesntLeakDeps() {
		AssetStore store = new AssetStore();
		store.RegisterSource(ownerID, new TestSource(new TestDependency("dep-a")), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, new SteppingCreator(new Step("step1", Handled: false, new TestDependency("dep-b"))), "creator-a", localPriority: 1);
		store.RegisterCreator(ownerID, new SteppingCreator(new Step("step1", Handled: true, new TestDependency("dep-c"))), "creator-b", localPriority: 0);

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		AssetLease<TestAsset> lease = asset.Borrow();
		Assert.Equal([new TestDependency("dep-a"), new TestDependency("dep-c")], lease.Dependencies.CastDepsToArray<TestDependency>());
	}
}
