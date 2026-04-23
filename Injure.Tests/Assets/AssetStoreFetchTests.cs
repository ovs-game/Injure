// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreFetchTests {
	private const string ownerID = "test";

	[Fact]
	public void OptionalTryFetchReturnsNullForUnhandledAsset() {
		AssetStore store = new();
		DictionarySource source = new();
		AssetID mainID = new(ownerID, "main");
		AssetID optionalID = new(ownerID, "missing");
		OptionalExtraFetchResolver resolver = new(optionalID);
		source.Set(mainID, "main-value");
		store.RegisterSource(ownerID, source, "source");
		store.RegisterResolver(ownerID, resolver, "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(mainID);
		asset.Warm();

		Assert.False(resolver.SawOptionalStream);
	}

	[Fact]
	public void RequiredFetchThrowsForUnhandledAsset() {
		AssetStore store = new();
		DictionarySource source = new();
		AssetID mainID = new(ownerID, "main");
		AssetID extraID = new(ownerID, "missing");
		source.Set(mainID, "main-value");
		store.RegisterSource(ownerID, source, "source");
		store.RegisterResolver(ownerID, new RequiredExtraFetchResolver(extraID), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(mainID);
		Assert.Throws<AssetUnhandledException>(() => asset.Warm());
	}

	[Fact]
	public void NonSeekableSourceStreamIsReplacedAndOriginalIsDisposed() {
		AssetStore store = new();
		NonSeekableSource source = new();
		store.RegisterSource(ownerID, source, "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		asset.Warm();

		Assert.NotNull(source.LastStream);
		Assert.True(source.LastStream.Disposed);
	}
}
