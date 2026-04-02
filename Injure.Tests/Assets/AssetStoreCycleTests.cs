// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Assets;

namespace Injure.Tests.Assets;

public class AssetStoreCycleTests {
	private const string ownerID = "test";

	[Fact]
	public void AcyclicChainSucceeds() {
		AssetStore store = new AssetStore();
		AssetLoadingResolver resolver = new AssetLoadingResolver(store, new Dictionary<AssetID, AssetID> {
			[new AssetID(ownerID, "assetA")] = new AssetID(ownerID, "assetB"),
			[new AssetID(ownerID, "assetB")] = new AssetID(ownerID, "assetC")
		});
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, resolver, "resolver");
		store.RegisterCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "assetA"));
		asset.Warm();
	}

	[Fact]
	public void SelfCycleThrows() {
		AssetStore store = new AssetStore();
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, new AssetLoadingResolver(store, new Dictionary<AssetID, AssetID> {
			[new AssetID(ownerID, "assetA")] = new AssetID(ownerID, "assetA")
		}), "resolver");
		store.RegisterCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "assetA"));
		AssetLoadCycleException ex = Assert.Throws<AssetLoadCycleException>(() => asset.Warm());
		Assert.Contains($"{nameof(TestAsset)}({ownerID}::assetA) -> {nameof(TestAsset)}({ownerID}::assetA)", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void LongerCyclesThrow() {
		AssetStore store = new AssetStore();
		AssetLoadingResolver resolver = new AssetLoadingResolver(store, new Dictionary<AssetID, AssetID> {
			[new AssetID(ownerID, "assetA")] = new AssetID(ownerID, "assetB"),
			[new AssetID(ownerID, "assetB")] = new AssetID(ownerID, "assetA")
		});
		store.RegisterSource(ownerID, new TestSource(), "source");
		store.RegisterResolver(ownerID, resolver, "resolver");
		store.RegisterCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "assetA"));
		AssetLoadCycleException ex = Assert.Throws<AssetLoadCycleException>(() => asset.Warm());
		Assert.Contains($"{nameof(TestAsset)}({ownerID}::assetA) -> {nameof(TestAsset)}({ownerID}::assetB) -> {nameof(TestAsset)}({ownerID}::assetA)", ex.Message, StringComparison.Ordinal);

		resolver.Map = new Dictionary<AssetID, AssetID> {
			[new AssetID(ownerID, "assetA")] = new AssetID(ownerID, "assetB"),
			[new AssetID(ownerID, "assetB")] = new AssetID(ownerID, "assetC"),
			[new AssetID(ownerID, "assetC")] = new AssetID(ownerID, "assetD"),
			[new AssetID(ownerID, "assetD")] = new AssetID(ownerID, "assetE"),
			[new AssetID(ownerID, "assetE")] = new AssetID(ownerID, "assetA")
		};

		asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "assetA"));
		ex = Assert.Throws<AssetLoadCycleException>(() => asset.Warm());
		Assert.Contains($"{nameof(TestAsset)}({ownerID}::assetA) -> {nameof(TestAsset)}({ownerID}::assetB) -> {nameof(TestAsset)}({ownerID}::assetC) -> {nameof(TestAsset)}({ownerID}::assetD) -> {nameof(TestAsset)}({ownerID}::assetE) -> {nameof(TestAsset)}({ownerID}::assetA)", ex.Message, StringComparison.Ordinal);
	}
}
