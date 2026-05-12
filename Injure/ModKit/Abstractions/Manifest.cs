// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Injure.Analyzers.Attributes;

namespace Injure.ModKit.Abstractions;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModCodeHotReloadLevel {
	public enum Case {
		None = 1,
		Restartless,
		Live,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModAssetManagementKind {
	public enum Case {
		None = 1,
		Tracked,
		Manual,
		Untracked,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModPatchingBackend {
	public enum Case {
		None = 1,
		MonoMod,
		External,
	}
}

public readonly record struct ModOrderManifest {
	public required IReadOnlyList<string> After { get; init; }
	public required IReadOnlyList<string> Before { get; init; }
	public static readonly ModOrderManifest Empty = new() {
		After = Array.Empty<string>(),
		Before = Array.Empty<string>(),
	};
}

public readonly record struct ModAssetsManifest {
	public required ModAssetManagementKind ManagementKind { get; init; }
	public required string? Root { get; init; }
}

public abstract record ModManifest {
	public required string OwnerID { get; init; }
	public required Semver Version { get; init; }
	public required string? DisplayName { get; init; }
	public required IReadOnlyDictionary<string, Semver> Dependencies { get; init; }
	public required ModOrderManifest Order { get; init; }
	public required ModAssetsManifest Assets { get; init; }
}

public sealed record CodeModManifest : ModManifest {
	public required string EntryAssembly { get; init; }
	public required ModCodeHotReloadLevel CodeHotReload { get; init; }
	public required ModPatchingBackend Patching { get; init; }
}

public sealed record ContentModManifest : ModManifest {
}

public static class ManifestReader {
	private enum ModPackageKind {
		Content,
		Code,
	}

	public static async ValueTask<ModManifest> ReadAsync(string path, CancellationToken ct) {
		await using FileStream stream = File.OpenRead(path);
		using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
		return Parse(doc.RootElement, path);
	}

	public static ModManifest Parse(JsonElement root, string sourceName = "<manifest>") {
		int schema = requiredInt(root, "schema", sourceName);
		if (schema != 0)
			throw new ModLoadException($"{sourceName}: unsupported schema {schema}");

		ModPackageKind type = requiredEnum<ModPackageKind>(root, "type", sourceName);
		string id = new(requiredString(root, "id", sourceName));
		Semver version = Semver.Parse(requiredString(root, "version", sourceName));
		string? displayName = optionalString(root, "display-name");
		IReadOnlyDictionary<string, Semver> dependencies = readDependencies(root);
		ModOrderManifest order = readOrder(root);
		ModAssetsManifest assets = readAssets(root, sourceName);
		return type switch {
			ModPackageKind.Content => parseContent(root, id, version, displayName, dependencies, order, assets, sourceName),
			ModPackageKind.Code => parseCode(root, id, version, displayName, dependencies, order, assets, sourceName),
			_ => throw new UnreachableException(),
		};
	}

	private static ContentModManifest parseContent(
		JsonElement root,
		string id,
		Semver version,
		string? displayName,
		IReadOnlyDictionary<string, Semver> dependencies,
		ModOrderManifest order,
		ModAssetsManifest assets,
		string sourceName
	) {
		rejectIfPresent(root, "entry-assembly", sourceName);
		rejectIfPresent(root, "code-hot-reload", sourceName);
		rejectIfPresent(root, "patching", sourceName);
		return new ContentModManifest {
			OwnerID = id,
			Version = version,
			DisplayName = displayName,
			Dependencies = dependencies,
			Order = order,
			Assets = assets,
		};
	}

	private static CodeModManifest parseCode(
		JsonElement root,
		string id,
		Semver version,
		string? displayName,
		IReadOnlyDictionary<string, Semver> dependencies,
		ModOrderManifest order,
		ModAssetsManifest assets,
		string sourceName
	) {
		string entryAssembly = requiredString(root, "entry-assembly", sourceName);
		ModCodeHotReloadLevel codeHotReload = ModCodeHotReloadLevel.Enum.FromTag(requiredEnum<ModCodeHotReloadLevel.Case>(root, "code-hot-reload", sourceName));
		ModPatchingBackend patching = ModPatchingBackend.Enum.FromTag(requiredEnum<ModPatchingBackend.Case>(root, "patching", sourceName));
		return new CodeModManifest {
			OwnerID = id,
			Version = version,
			DisplayName = displayName,
			Dependencies = dependencies,
			Order = order,
			Assets = assets,
			EntryAssembly = entryAssembly,
			CodeHotReload = codeHotReload,
			Patching = patching,
		};
	}

	private static ModAssetsManifest readAssets(JsonElement root, string sourceName) {
		JsonElement assets = requiredObject(root, "assets", sourceName);
		ModAssetManagementKind managementKind = ModAssetManagementKind.Enum.FromTag(requiredEnum<ModAssetManagementKind.Case>(assets, "management", sourceName));
		string? assetRoot = optionalString(assets, "root");

		if ((managementKind.Tag is ModAssetManagementKind.Case.Tracked or ModAssetManagementKind.Case.Manual) && string.IsNullOrWhiteSpace(assetRoot))
			throw new ModLoadException($"{sourceName}: assets.root is required when assets.management is tracked or manual");
		if (managementKind == ModAssetManagementKind.None && assetRoot is not null)
			throw new ModLoadException($"{sourceName}: assets.root must be absent when assets.management is none");

		return new ModAssetsManifest {
			ManagementKind = managementKind,
			Root = assetRoot,
		};
	}

	private static Dictionary<string, Semver> readDependencies(JsonElement root) => readDependencyMap(root, "dependencies");

	private static ModOrderManifest readOrder(JsonElement root) {
		if (!root.TryGetProperty("order", out JsonElement order))
			return ModOrderManifest.Empty;
		return new ModOrderManifest {
			After = readIDArray(order, "after"),
			Before = readIDArray(order, "before"),
		};
	}

	private static Dictionary<string, Semver> readDependencyMap(JsonElement root, string name) {
		Dictionary<string, Semver> map = new();
		if (!root.TryGetProperty(name, out JsonElement obj))
			return map;
		foreach (JsonProperty prop in obj.EnumerateObject())
			map.Add(prop.Name, Semver.Parse(prop.Value.GetString() ?? throw new JsonException("dependency version must be a string")));
		return map;
	}

	private static string[] readIDArray(JsonElement root, string name) {
		if (!root.TryGetProperty(name, out JsonElement arr))
			return Array.Empty<string>();
		return arr.EnumerateArray().Select(static x => x.GetString() ?? throw new JsonException("mod id must be string")).ToArray();
	}

	private static JsonElement requiredObject(JsonElement root, string name, string sourceName) {
		if (!root.TryGetProperty(name, out JsonElement val) || val.ValueKind != JsonValueKind.Object)
			throw new ModLoadException($"{sourceName}: required object field '{name}' missing");
		return val;
	}

	private static string requiredString(JsonElement root, string name, string sourceName) {
		if (!root.TryGetProperty(name, out JsonElement val) || val.ValueKind != JsonValueKind.String)
			throw new ModLoadException($"{sourceName}: required string field '{name}' missing");
		return val.GetString() ?? throw new ModLoadException($"{sourceName}: required string field '{name}' is null");
	}

	private static string? optionalString(JsonElement root, string name) {
		return root.TryGetProperty(name, out JsonElement val) && val.ValueKind == JsonValueKind.String ? val.GetString() : null;
	}

	private static int requiredInt(JsonElement root, string name, string sourceName) {
		if (!root.TryGetProperty(name, out JsonElement val) || !val.TryGetInt32(out int result))
			throw new ModLoadException($"{sourceName}: required int field '{name}' missing");
		return result;
	}

	private static T requiredEnum<T>(JsonElement root, string name, string sourceName) where T : struct, Enum {
		string raw = requiredString(root, name, sourceName);
		string normalized = raw.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
		if (!Enum.TryParse(normalized, ignoreCase: true, out T val))
			throw new ModLoadException($"{sourceName}: invalid {typeof(T).Name} value '{raw}'");
		return val;
	}

	private static void rejectIfPresent(JsonElement root, string name, string sourceName) {
		if (root.TryGetProperty(name, out _))
			throw new ModLoadException($"{sourceName}: field '{name}' is not valid for this mod type");
	}
}
