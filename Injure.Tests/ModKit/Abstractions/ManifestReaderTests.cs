// SPDX-License-Identifier: MIT

using System.Text.Json;

using Injure.ModKit.Abstractions;

namespace Injure.Tests.ModKit.Abstractions;

public sealed class ManifestReaderTests {
	private static ModManifest parse(string json) {
		using JsonDocument doc = JsonDocument.Parse(json);
		return ManifestReader.Parse(doc.RootElement, "test-manifest");
	}

	private static void assertDep(ModManifest manifest, string ownerId, Semver version) {
		Assert.True(manifest.Dependencies.TryGetValue(ownerId, out Semver actual), $"missing dependency '{ownerId}'");
		Assert.Equal(version, actual);
	}

	[Fact]
	public void ParsesContentManifest() {
		ContentModManifest manifest = Assert.IsType<ContentModManifest>(parse("""
		{
			"schema": 0,
			"type": "content",
			"id": "jdoe.my-awesome-texture-pack",
			"version": "1.0.0",
			"dependencies": {},
			"order": {
				"after": [],
				"before": []
			},
			"assets": {
				"management": "tracked",
				"root": "Assets"
			}
		}
		"""));
		Assert.Equal("jdoe.my-awesome-texture-pack", manifest.OwnerID);
		Assert.Equal(new Semver(1, 0, 0), manifest.Version);
		Assert.Equal(ModAssetManagementKind.Tracked, manifest.Assets.ManagementKind);
		Assert.Equal("Assets", manifest.Assets.Root);
		Assert.Empty(manifest.Dependencies);
		Assert.Empty(manifest.Order.After);
		Assert.Empty(manifest.Order.Before);
	}

	[Fact]
	public void ParsesCodeManifest() {
		CodeModManifest manifest = Assert.IsType<CodeModManifest>(parse("""
		{
			"schema": 0,
			"type": "code",
			"id": "jdoe.my-awesome-mod",
			"version": "1.2.3",
			"entry-assembly": "MyAwesomeMod.dll",
			"code-hot-reload": "live",
			"patching": "monomod",
			"dependencies": {},
			"order": {
				"after": [],
				"before": []
			},
			"assets": {
				"management": "none"
			}
		}
		"""));
		Assert.Equal("jdoe.my-awesome-mod", manifest.OwnerID);
		Assert.Equal(new Semver(1, 2, 3), manifest.Version);
		Assert.Null(manifest.DisplayName);
		Assert.Equal("MyAwesomeMod.dll", manifest.EntryAssembly);
		Assert.Equal(ModCodeHotReloadLevel.Live, manifest.CodeHotReload);
		Assert.Equal(ModPatchingBackend.MonoMod, manifest.Patching);
		Assert.Equal(ModAssetManagementKind.None, manifest.Assets.ManagementKind);
		Assert.Null(manifest.Assets.Root);
		Assert.Empty(manifest.Dependencies);
		Assert.Empty(manifest.Order.After);
		Assert.Empty(manifest.Order.Before);
	}

	[Fact]
	public void ParsesFancierCodeManifest() {
		CodeModManifest manifest = Assert.IsType<CodeModManifest>(parse("""
		{
			"schema": 0,
			"type": "code",
			"id": "jdoe.my-awesome-mod",
			"display-name": "My Awesome Mod",
			"version": "2.3.4-beta.1+build.7",
			"entry-assembly": "MyAwesomeMod.dll",
			"code-hot-reload": "restartless",
			"patching": "external",
			"dependencies": {
				"somegame.modapi": "1.0.0",
				"jdoe.my-awesome-content-pack": "2.1.0"
			},
			"order": {
				"after": [ "somegame.base" ],
				"before": [ "jdoe.my-awesome-mod-two" ]
			},
			"assets": {
				"management": "tracked",
				"root": "Assets"
			}
		}
		"""));
		Assert.Equal("jdoe.my-awesome-mod", manifest.OwnerID);
		Assert.Equal("My Awesome Mod", manifest.DisplayName);
		Assert.Equal(new Semver(2, 3, 4, "beta.1", "build.7"), manifest.Version);
		Assert.Equal("MyAwesomeMod.dll", manifest.EntryAssembly);
		Assert.Equal(ModCodeHotReloadLevel.Restartless, manifest.CodeHotReload);
		Assert.Equal(ModPatchingBackend.External, manifest.Patching);
		Assert.Equal(ModAssetManagementKind.Tracked, manifest.Assets.ManagementKind);
		Assert.Equal("Assets", manifest.Assets.Root);
		assertDep(manifest, "somegame.modapi", new Semver(1, 0, 0));
		assertDep(manifest, "jdoe.my-awesome-content-pack", new Semver(2, 1, 0));
		Assert.Equal(["somegame.base"], manifest.Order.After);
		Assert.Equal(["jdoe.my-awesome-mod-two"], manifest.Order.Before);
	}
}
