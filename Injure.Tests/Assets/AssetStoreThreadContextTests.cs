// SPDX-License-Identifier: MIT

using System;
using System.Threading;

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreThreadContextTests {
	private const string ownerID = "test";

	[Fact]
	public void SameThreadCanAttachToMultipleStores() {
		AssetStore a = new();
		AssetStore b = new();
		AssetStore c = new();
		using AssetThreadContext ctxA = a.AttachCurrentThread();
		using AssetThreadContext ctxB = b.AttachCurrentThread();
		using AssetThreadContext ctxC = c.AttachCurrentThread();
		ctxA.AtSafeBoundary();
		ctxB.AtSafeBoundary();
		ctxC.AtSafeBoundary();
	}

	[Fact]
	public void RetiredVerIsReclaimedOnlyAfterSafeBoundary() {
		AssetStore store = new();
		using AssetThreadContext mainCtx = store.AttachCurrentThread();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		TestAsset v = asset.Borrow().Value;

		ThreadCheckpoint first = new();
		ThreadCheckpoint second = new();
		Exception? ex = null;
		Thread thread = new(() => {
			try {
				using AssetThreadContext ctx = store.AttachCurrentThread();
				first.Wait();
				ctx.AtSafeBoundary();
				second.Wait();
				// dispose happens here from `using`
			} catch (Exception caught) {
				ex = caught;
				first.ForceSet();
				second.ForceSet();
			}
		});
		thread.Start();

		Assert.True(first.Entered.Wait(TimeSpan.FromMilliseconds(100)));
		if (ex is not null)
			throw ex;
		asset.QueueReload();
		store.AtSafeBoundary();
		int published = store.ApplyQueuedReloadsOrThrow();
		Assert.Equal(1, published);
		Assert.Equal($"{ownerID}::asset", v.Val);

		store.AtSafeBoundary();
		first.Proceed();
		Assert.True(second.Entered.Wait(TimeSpan.FromMilliseconds(100)));
		if (ex is not null)
			throw ex;
		Assert.Throws<AssetLeaseExpiredException>(() => _ = v.Val);

		second.Proceed();
		Assert.True(thread.Join(TimeSpan.FromMilliseconds(100)));
		if (ex is not null)
			throw ex;
	}

	[Fact]
	public void DisposingContextAllowsReclamation() {
		AssetStore store = new();
		using AssetThreadContext mainCtx = store.AttachCurrentThread();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		TestAsset v = asset.Borrow().Value;

		ThreadCheckpoint ckp = new();
		Exception? ex = null;
		Thread thread = new(() => {
			try {
				using AssetThreadContext ctx = store.AttachCurrentThread();
				ckp.Wait();
				// dispose happens here from `using`
			} catch (Exception caught) {
				ex = caught;
				ckp.ForceSet();
			}
		});
		thread.Start();

		Assert.True(ckp.Entered.Wait(TimeSpan.FromMilliseconds(100)));
		if (ex is not null)
			throw ex;
		asset.QueueReload();
		store.AtSafeBoundary();
		int published = store.ApplyQueuedReloadsOrThrow();
		Assert.Equal(1, published);
		Assert.Equal($"{ownerID}::asset", v.Val);

		store.AtSafeBoundary();
		ckp.Proceed();
		Assert.True(thread.Join(TimeSpan.FromMilliseconds(100)));
		if (ex is not null)
			throw ex;
		Assert.Throws<AssetLeaseExpiredException>(() => _ = v.Val);
	}
}
