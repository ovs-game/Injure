// SPDX-License-Identifier: MIT

using System.Threading.Tasks;

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreWatcherTests {
	private const string ownerID = "test";

	[Fact]
	public void WatchersRegisteredBeforeDependencyPublicationAllWatchIt() {
		AssetStore store = new AssetStore();
		TestDependency dep = new TestDependency("dep");
		TestDependencyWatcher watcherA = new TestDependencyWatcher();
		TestDependencyWatcher watcherB = new TestDependencyWatcher();
		store.RegisterSource(ownerID, new TestSource(dep), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");
		store.RegisterDependencyWatcher(ownerID, watcherA, "watcher-a");
		store.RegisterDependencyWatcher(ownerID, watcherB, "watcher-b");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		asset.Warm();

		Assert.Equal(["watch:dep"], watcherA.Log);
		Assert.Equal(["watch:dep"], watcherB.Log);
		Assert.Contains(dep, watcherA.Watched);
		Assert.Contains(dep, watcherB.Watched);
	}

	[Fact]
	public void WatcherRegisteredAfterDependencyPublicationWatchesExistingDependency() {
		AssetStore store = new AssetStore();
		TestDependency dep = new TestDependency("dep");
		TestDependencyWatcher watcherA = new TestDependencyWatcher();
		TestDependencyWatcher watcherB = new TestDependencyWatcher();
		store.RegisterSource(ownerID, new TestSource(dep), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");
		store.RegisterDependencyWatcher(ownerID, watcherA, "watcher-a");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		asset.Warm();

		store.RegisterDependencyWatcher(ownerID, watcherB, "watcher-b");

		Assert.Equal(["watch:dep"], watcherB.Log);
		Assert.Contains(dep, watcherB.Watched);
	}

	[Fact]
	public async Task SecondWatcherOfSameTypeCanTriggerReload() {
		AssetStore store = new AssetStore();
		TestDependency dep = new TestDependency("dep");
		TestDependencyWatcher watcherA = new TestDependencyWatcher();
		TestDependencyWatcher watcherB = new TestDependencyWatcher();
		store.RegisterSource(ownerID, new TestSource(dep), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");
		store.RegisterDependencyWatcher(ownerID, watcherA, "watcher-a");
		store.RegisterDependencyWatcher(ownerID, watcherB, "watcher-b");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync();
		watcherB.Raise(dep);
		await AssetTestWait.ForQueuedReloadAsync(asset);

		Assert.Equal(1, store.ApplyQueuedReloadsOrThrow());
		Assert.Equal(2ul, asset.Borrow().Version);
	}

	[Fact]
	public void DependencyReplacementUnwatchesOldDependencyAndWatchesNewDependency() {
		AssetStore store = new AssetStore();
		TestDependency depA = new TestDependency("dep-a");
		TestDependency depB = new TestDependency("dep-b");
		TestDependencyWatcher watcher = new TestDependencyWatcher();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, new SteppingCreator(
			new Step("step-a", Handled: true, depA),
			new Step("step-b", Handled: true, depB)
		), "creator");
		store.RegisterDependencyWatcher(ownerID, watcher, "watcher");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		asset.Warm();
		asset.QueueReload();
		store.ApplyQueuedReloadsOrThrow();

		Assert.Equal(["watch:dep-a", "unwatch:dep-a", "watch:dep-b"], watcher.Log);
		Assert.DoesNotContain(depA, watcher.Watched);
		Assert.Contains(depB, watcher.Watched);
	}

	[Fact]
	public void UnregisteredWatcherNoLongerTriggersReloads() {
		AssetStore store = new AssetStore();
		TestDependency dep = new TestDependency("dep");
		TestDependencyWatcher watcher = new TestDependencyWatcher();
		store.RegisterSource(ownerID, new TestSource(dep), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");
		AssetDependencyWatcherHandle handle = store.RegisterDependencyWatcher(ownerID, watcher, "watcher");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		asset.Warm();
		store.UnregisterDependencyWatcher(handle);

		watcher.Raise(dep);
		Assert.False(asset.HasQueuedReload);
		Assert.True(watcher.Disposed);
	}
}
