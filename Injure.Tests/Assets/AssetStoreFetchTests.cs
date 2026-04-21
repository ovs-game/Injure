// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreFetchTests {
	private const string ownerID = "test";

	[Fact]
	public void OptionalTryFetchReturnsNullForUnhandledAsset() {
		AssetStore store = new AssetStore();
		DictionarySource source = new DictionarySource();
		AssetID mainID = new AssetID(ownerID, "main");
		AssetID optionalID = new AssetID(ownerID, "missing");
		OptionalExtraFetchResolver resolver = new OptionalExtraFetchResolver(optionalID);
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
		AssetStore store = new AssetStore();
		DictionarySource source = new DictionarySource();
		AssetID mainID = new AssetID(ownerID, "main");
		AssetID extraID = new AssetID(ownerID, "missing");
		source.Set(mainID, "main-value");
		store.RegisterSource(ownerID, source, "source");
		store.RegisterResolver(ownerID, new RequiredExtraFetchResolver(extraID), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(mainID);
		Assert.Throws<AssetUnhandledException>(() => asset.Warm());
	}

	[Fact]
	public void NonSeekableSourceStreamIsReplacedAndOriginalIsDisposed() {
		AssetStore store = new AssetStore();
		NonSeekableSource source = new NonSeekableSource();
		store.RegisterSource(ownerID, source, "source");
		store.RegisterResolver(ownerID, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerID, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetID(ownerID, "asset"));
		asset.Warm();

		Assert.NotNull(source.LastStream);
		Assert.True(source.LastStream.Disposed);
	}
}
