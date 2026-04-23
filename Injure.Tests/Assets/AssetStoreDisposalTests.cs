// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreDisposalTests {
	private const string ownerID = "test";

	[Fact]
	public async Task PreparedDataIsDisposedAfterInitialMaterialize() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync().WaitAsync(TimeSpan.FromMilliseconds(100));

		Assert.Equal(1, creator.PreparedDisposeCalls);
	}

	[Fact]
	public async Task PreparedDataIsDisposedAfterSuccessfulReload() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
		await asset.QueueReloadAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
		store.ApplyQueuedReloadsOrThrow();

		Assert.Equal(2, creator.PreparedDisposeCalls);
	}

	[Fact]
	public async Task PreparedDataIsDisposedWhenFinalizeFails() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
		creator.FinalizeException = new InvalidOperationException("finalize failed");
		await asset.QueueReloadAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
		store.ApplyQueuedReloads();

		Assert.Equal(2, creator.PreparedDisposeCalls);
	}

	[Fact]
	public async Task SupersededPendingReloadDisposesPreparedData() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		await asset.WarmAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
		await asset.QueueReloadAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.True(asset.HasQueuedReload);

		await asset.QueueReloadAsync().WaitAsync(TimeSpan.FromMilliseconds(100));
		Assert.True(asset.HasQueuedReload);
		store.ApplyQueuedReloadsOrThrow();

		Assert.Equal(3, creator.PreparedDisposeCalls);
		Assert.Equal(3ul, asset.Borrow().Version);
	}
}
