// SPDX-License-Identifier: MIT

using System.Text.Json;

using Injure.ModKit.Abstractions;

namespace Injure.Tests.ModKit.Abstractions;

public sealed class ManifestReaderTests {
	private static ModManifest parse(string json) {
		using JsonDocument doc = JsonDocument.Parse(json);
		return ManifestReader.Parse(doc.RootElement, "test-manifest");
	}

	[Fact]
	public void ParsesContentManifest() {
		ContentModManifest manifest = Assert.IsType<ContentModManifest>(parse("""
		{
			"schema": 0,
			"type": "content",
			"id": "jdoe.my-awesome-texture-pack",
			"version": "1.0.0",
			"relationships": [],
			"assets": {
				"management": "tracked",
				"root": "Assets"
			}
		}
		"""));
		Assert.Equal("jdoe.my-awesome-texture-pack", manifest.OwnerID);
		Assert.Equal(new Semver(1, 0, 0), manifest.Version);
		Assert.Equal(ModAssetManagementKind.Tracked, manifest.Assets.ManagementKind);
		Assert.Empty(manifest.Relationships);
		Assert.Equal("Assets", manifest.Assets.Root);
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
			"relationships": [],
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
		Assert.Empty(manifest.Relationships);
		Assert.Equal(ModAssetManagementKind.None, manifest.Assets.ManagementKind);
		Assert.Null(manifest.Assets.Root);
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
			"relationships": [
				{
					"id": "somegame.modapi",
					"kind": "requires-self-after",
					"version": "1.0.0"
				},
				{
					"id": "somegame.something",
					"kind": "if-present-self-after"
				},
				{
					"id": "jdoe.my-awesome-content-pack",
					"kind": "requires-self-after",
					"version": "2.1.0"
				},
				{
					"id": "jdoe.my-awesome-mod-two",
					"kind": "if-present-self-before",
					"version": "1.2.3"
				}
			],
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
		Assert.Contains(new ModRelationshipManifest {
			OwnerID = "somegame.modapi",
			Kind = ModRelationshipKind.RequiresSelfAfter,
			Version = new Semver(1, 0, 0),
		}, manifest.Relationships);
		Assert.Contains(new ModRelationshipManifest {
			OwnerID = "somegame.something",
			Kind = ModRelationshipKind.IfPresentSelfAfter,
			Version = null,
		}, manifest.Relationships);
		Assert.Contains(new ModRelationshipManifest {
			OwnerID = "jdoe.my-awesome-content-pack",
			Kind = ModRelationshipKind.RequiresSelfAfter,
			Version = new Semver(2, 1, 0),
		}, manifest.Relationships);
		Assert.Contains(new ModRelationshipManifest {
			OwnerID = "jdoe.my-awesome-mod-two",
			Kind = ModRelationshipKind.IfPresentSelfBefore,
			Version = new Semver(1, 2, 3),
		}, manifest.Relationships);
		Assert.Equal(ModAssetManagementKind.Tracked, manifest.Assets.ManagementKind);
		Assert.Equal("Assets", manifest.Assets.Root);
	}
}
