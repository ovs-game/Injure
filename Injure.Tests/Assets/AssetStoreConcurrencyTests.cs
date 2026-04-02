// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Injure.Assets;

namespace Injure.Tests.Assets;

public class AssetStoreConcurrencyTests {
	private const string ownerID = "test";

	[Fact]
	public async Task ColdConcurrentBorrowsMaterializeOnce() {
		AssetStore store = new AssetStore();
		TestCreator creator = new TestCreator();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		Assert.False(asset.IsLoaded);

		ulong[] ids = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => asset.Borrow().Value.ID))).WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.Equal(1, creator.PrepareCalls);
		Assert.Equal(1, creator.FinalizeCalls);
		Assert.True(ids.All(id => id == ids[0]));
	}

	[Fact]
	public async Task ColdConcurrentWarmsMaterializeOnce() {
		AssetStore store = new AssetStore();
		TestCreator creator = new TestCreator();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		Assert.False(asset.IsLoaded);

		await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => asset.WarmAsync())).WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.Equal(1, creator.PrepareCalls);
		Assert.Equal(1, creator.FinalizeCalls);
		Assert.True(asset.TryPassiveBorrow(out AssetLease<TestAsset> lease));
		Assert.Equal(1ul, lease.Version);
	}

	[Fact]
	public async Task ConcurrentQueueReloadsAreHarmless() {
		AssetStore store = new AssetStore();
		TestCreator creator = new TestCreator();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync();

		await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => asset.QueueReloadAsync()));
		Assert.True(asset.HasQueuedReload);
		int published = store.ApplyQueuedReloads();
		Assert.Equal(1, published);

		Assert.Equal(2, creator.PrepareCalls);
		Assert.Equal(2, creator.FinalizeCalls);
		Assert.Equal(2ul, asset.Borrow().Version);
	}

	[Fact]
	public async Task ConcurrentWatcherEventsAreHarmless() {
		AssetStore store = new AssetStore();
		TestDependencyWatcher watcher = new TestDependencyWatcher();
		store.RegisterSource(ownerID, new TestSource(new TestDependency("dep")), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, new TestCreator(), "creator");
		store.RegisterDependencyWatcher(ownerID, watcher, "watcher");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync();
		await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => watcher.Raise(new TestDependency("dep")))));
		Assert.True(asset.HasQueuedReload);
	}

	[Fact]
	public async Task ParallelAssetPrepWorks() {
		AssetID a = new AssetID(ownerID, "a");
		AssetID b = new AssetID(ownerID, "b");

		AssetStore store = new AssetStore();
		CountingCheckpoint ckp = new CountingCheckpoint(target: 2);
		DictionarySource source = new DictionarySource();
		source.Set(a, "A");
		source.Set(b, "B");
		TestCreator creator = new TestCreator(onPrepareAsync: (_, ct) => ckp.WaitAsync(ct));
		store.RegisterSource(ownerID, source, "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> assetA = store.GetAsset<TestAsset>(a);
		AssetRef<TestAsset> assetB = store.GetAsset<TestAsset>(b);
		Task warmA = assetA.WarmAsync();
		Task warmB = assetB.WarmAsync();

		await ckp.TargetReached.WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.Equal(2, ckp.EnteredCount);

		ckp.Proceed();
		await Task.WhenAll(warmA, warmB).WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.Equal(2, creator.PrepareCalls);
		Assert.Equal(2, creator.FinalizeCalls);
	}

	[Fact]
	public async Task CancelledWarmDoesntCancelSharedWork() {
		AssetStore store = new AssetStore();
		Checkpoint ckp = new Checkpoint();
		TestCreator creator = new TestCreator(onPrepareAsync: (_, ct) => ckp.WaitAsync(ct));
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		using CancellationTokenSource cts1 = new CancellationTokenSource();
		using CancellationTokenSource cts2 = new CancellationTokenSource();
		using CancellationTokenSource cts3 = new CancellationTokenSource();
		Task warm1 = asset.WarmAsync(cts1.Token);
		Task warm2 = asset.WarmAsync(cts2.Token);
		Task warm3 = asset.WarmAsync(cts3.Token);

		await ckp.Entered.WaitAsync(TimeSpan.FromMilliseconds(100));
		cts2.Cancel();
		await Assert.ThrowsAsync<TaskCanceledException>(() => warm2);

		ckp.Proceed();
		await Task.WhenAll(warm1, warm3).WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.Equal(1, creator.PrepareCalls);
		Assert.Equal(1, creator.FinalizeCalls);
		Assert.True(asset.TryPassiveBorrow(out AssetLease<TestAsset> lease));
		Assert.Equal($"{ownerID}::asset", lease.Value.Val);
	}
}
