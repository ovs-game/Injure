// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public class AssetStoreBasicTests {
	private const string ownerID = "test";

	[Fact]
	public void BasicFunctionality() {
		AssetStore store = new AssetStore();
		TestDependencyWatcher watcher = new TestDependencyWatcher();
		store.RegisterSource(ownerID, new TestSource(new TestDependency("dep")), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		AssetCreatorHandle ch = store.RegisterCreator(ownerID, new TestCreator(), "creator");
		AssetDependencyWatcherHandle wh = store.RegisterDependencyWatcher(ownerID, watcher, "watcher");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		Assert.False(asset.IsLoaded);
		Assert.False(asset.TryPassiveBorrow(out _));

		asset.Warm();
		Assert.True(asset.IsLoaded);
		Assert.True(asset.TryPassiveBorrow(out AssetLease<TestAsset> lease));
		Assert.Equal(1ul, lease.Version);
		Assert.Equal($"{ownerID}::asset", lease.Value.Val);
		Assert.Equal([new TestDependency("dep")], lease.Dependencies.CastDepsToArray<TestDependency>());
		Assert.Equal(["watch:dep"], watcher.Log);

		watcher.Raise(new TestDependency("dep"));
		Assert.True(asset.HasQueuedReload);

		store.UnregisterCreator(ch);
		AssetRef<TestAsset> asset2 = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset2"));
		Assert.Throws<AssetUnhandledException>(() => _ = asset2.Borrow());

		store.UnregisterDependencyWatcher(wh);
		Assert.True(watcher.Disposed);
	}

	[Fact]
	public void ReloadingWorks() {
		AssetStore store = new AssetStore();
		store.RegisterSource(ownerID, new TestSource(new TestDependency("dep-source")), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, new SteppingCreator(
			new Step("step1", Handled: true, new TestDependency("dep-creator-1")),
			new Step("step2", Handled: true, new TestDependency("dep-creator-2"))
		), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		AssetLease<TestAsset> lease = asset.Borrow();
		Assert.Equal(1ul, lease.Version);
		Assert.Equal("step1", lease.Value.Val);
		Assert.Equal([new TestDependency("dep-source"), new TestDependency("dep-creator-1")], lease.Dependencies.CastDepsToArray<TestDependency>());

		asset.QueueReload();
		Assert.True(asset.HasQueuedReload);
		int published = store.ApplyQueuedReloads();
		Assert.Equal(1, published);

		lease = asset.Borrow();
		Assert.Equal(2ul, lease.Version);
		Assert.Equal("step2", lease.Value.Val);
		Assert.Equal([new TestDependency("dep-source"), new TestDependency("dep-creator-2")], lease.Dependencies.CastDepsToArray<TestDependency>());
	}

	[Fact]
	public void RevocationWorks() {
		AssetStore store = new AssetStore();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		TestAsset v = asset.Borrow().Value;
		Assert.Equal($"{ownerID}::asset", v.Val);

		asset.QueueReload();
		int published = store.ApplyQueuedReloads();
		Assert.Equal(1, published);

		Assert.Throws<AssetLeaseExpiredException>(() => _ = v.Val);
	}

	[Fact]
	public void DepsAreDeduplicated() {
		AssetStore store = new AssetStore();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, new SteppingCreator(
			new Step("step", Handled: true, new TestDependency("dep-duplicate"), new TestDependency("dep-duplicate"))
		), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		AssetLease<TestAsset> lease = asset.Borrow();
		TestDependency[] deps = lease.Dependencies.CastDepsToArray<TestDependency>();
		Assert.Single(deps);
		Assert.Equal([new TestDependency("dep-duplicate")], deps);
	}
}
