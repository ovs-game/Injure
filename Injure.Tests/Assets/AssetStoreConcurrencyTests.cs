// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreConcurrencyTests {
	private const string ownerID = "test";

	[Fact]
	public async Task ColdConcurrentBorrowsMaterializeOnce() {
		AssetStore store = new AssetStore();
		TestCreator creator = new TestCreator();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		Assert.False(asset.IsLoaded);

		ulong[] ids = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => Task.Run(() => asset.Borrow().Value.ID))).WaitAsync(TimeSpan.FromMilliseconds(100));
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
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		Assert.False(asset.IsLoaded);

		await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => asset.WarmAsync())).WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.Equal(1, creator.PrepareCalls);
		Assert.Equal(1, creator.FinalizeCalls);
		Assert.True(asset.TryPassiveBorrow(out AssetLease<TestAsset> lease));
		Assert.Equal(1ul, lease.Version);
	}

	[Fact]
	public async Task ConcurrentQueueReloadsWork() {
		AssetStore store = new AssetStore();
		TestCreator creator = new TestCreator();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync().WaitAsync(TimeSpan.FromMilliseconds(100));

		await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => asset.QueueReloadAsync())).WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.True(asset.HasQueuedReload);
		int published = store.ApplyQueuedReloadsOrThrow();
		Assert.Equal(1, published);

		Assert.InRange(creator.PrepareCalls, 2, 16);
		Assert.Equal(2, creator.FinalizeCalls);
		Assert.Equal(16ul, asset.Borrow().Version);
	}

	[Fact]
	public async Task ConcurrentQueueReloadsFromThreadPoolWork() {
		AssetStore store = new AssetStore();
		TestCreator creator = new TestCreator();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync().WaitAsync(TimeSpan.FromMilliseconds(100));

		await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => Task.Run(() => asset.QueueReloadAsync()))).WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.True(asset.HasQueuedReload);
		int published = store.ApplyQueuedReloadsOrThrow();
		Assert.Equal(1, published);

		Assert.InRange(creator.PrepareCalls, 2, 16);
		Assert.Equal(2, creator.FinalizeCalls);
		Assert.Equal(16ul, asset.Borrow().Version);
	}

	[Fact]
	public async Task ConcurrentWatcherEventsWork() {
		AssetStore store = new AssetStore();
		TestCreator creator = new TestCreator();
		TestDependencyWatcher watcher = new TestDependencyWatcher();
		store.RegisterSource(ownerID, new TestSource(new TestDependency("dep")), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");
		store.RegisterDependencyWatcher(ownerID, watcher, "watcher");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync().WaitAsync(TimeSpan.FromMilliseconds(100));

		await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => Task.Run(() => watcher.Raise(new TestDependency("dep"))))).WaitAsync(TimeSpan.FromMilliseconds(100));
		await AssetTestWait.ForQueuedReloadAsync(asset);
		int published = store.ApplyQueuedReloadsOrThrow();
		Assert.Equal(1, published);

		Assert.InRange(creator.PrepareCalls, 2, 16);
		Assert.Equal(2, creator.FinalizeCalls);
		Assert.Equal(16ul, asset.Borrow().Version);
	}

	[Fact]
	public async Task ParallelAssetPrepWorks() {
		AssetID a = new AssetID(ownerID, "a");
		AssetID b = new AssetID(ownerID, "b");

		AssetStore store = new AssetStore();
		CountingTaskCheckpoint ckp = new CountingTaskCheckpoint(target: 2);
		DictionarySource source = new DictionarySource();
		source.Set(a, "A");
		source.Set(b, "B");
		TestCreator creator = new TestCreator(onPrepareAsync: (_, ct) => ckp.WaitAsync(ct));
		store.RegisterSource(ownerID, source, "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

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
		TaskCheckpoint ckp = new TaskCheckpoint();
		TestCreator creator = new TestCreator(onPrepareAsync: (_, ct) => ckp.WaitAsync(ct));
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		using CancellationTokenSource cts1 = new CancellationTokenSource();
		using CancellationTokenSource cts2 = new CancellationTokenSource();
		using CancellationTokenSource cts3 = new CancellationTokenSource();
		Task warm1 = asset.WarmAsync(cts1.Token);
		Task warm2 = asset.WarmAsync(cts2.Token);
		Task warm3 = asset.WarmAsync(cts3.Token);

		await ckp.Entered.WaitAsync(TimeSpan.FromMilliseconds(100));
		cts2.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => warm2.WaitAsync(TimeSpan.FromMilliseconds(100)));

		ckp.Proceed();
		await Task.WhenAll(warm1, warm3).WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.Equal(1, creator.PrepareCalls);
		Assert.Equal(1, creator.FinalizeCalls);
		Assert.True(asset.TryPassiveBorrow(out AssetLease<TestAsset> lease));
		Assert.Equal($"{ownerID}::asset", lease.Value.Val);
	}

	[Fact]
	public async Task CancelledQueueReloadDoesntCancelSharedReloadWork() {
		AssetStore store = new AssetStore();
		BlockingOnNthPrepare block = new BlockingOnNthPrepare(2);
		TestCreator creator = new TestCreator(onPrepareAsync: block.OnPrepareAsync);
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync().WaitAsync(TimeSpan.FromMilliseconds(100));

		using CancellationTokenSource cts1 = new CancellationTokenSource();
		using CancellationTokenSource cts2 = new CancellationTokenSource();
		Task reload1 = asset.QueueReloadAsync(cts1.Token);
		await block.Checkpoint.Entered.WaitAsync(TimeSpan.FromMilliseconds(100));
		Task reload2 = asset.QueueReloadAsync(cts2.Token);

		cts1.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reload1.WaitAsync(TimeSpan.FromMilliseconds(100)));

		block.Checkpoint.Proceed();
		await reload2.WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.Equal(1, store.ApplyQueuedReloadsOrThrow());
		Assert.Equal(3ul, asset.Borrow().Version);
		Assert.Null(asset.LastReloadFailure);
	}
}
