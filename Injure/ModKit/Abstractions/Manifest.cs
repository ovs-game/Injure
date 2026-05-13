// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Injure.Analyzers.Attributes;

namespace Injure.ModKit.Abstractions;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModRelationshipKind {
	public enum Case {
		RequiresSelfAfter = 1,
		RequiresSelfBefore,
		IfPresentSelfAfter,
		IfPresentSelfBefore,
		Conflicts,
	}
}

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

public readonly record struct ModRelationshipManifest {
	public required string OwnerID { get; init; }
	public required ModRelationshipKind Kind { get; init; }
	public required Semver? Version { get; init; }
}

public readonly record struct ModAssetsManifest {
	public required ModAssetManagementKind ManagementKind { get; init; }
	public required string? Root { get; init; }
}

public abstract record ModManifest {
	public required string OwnerID { get; init; }
	public required Semver Version { get; init; }
	public required string? DisplayName { get; init; }
	public required IReadOnlyList<ModRelationshipManifest> Relationships { get; init; }
	public required ModAssetsManifest Assets { get; init; }
}

public sealed record CodeModManifest : ModManifest {
	public required string EntryAssembly { get; init; }
	public required ModCodeHotReloadLevel CodeHotReload { get; init; }
	public required ModPatchingBackend Patching { get; init; }
}

public sealed record ContentModManifest : ModManifest {
}

// TODO this fucking sucks
public static class ManifestReader {
	private enum ModPackageKind {
		Content,
		Code,
	}

	public static async ValueTask<ModManifest> ReadAsync(string path, CancellationToken ct) {
		JsonDocumentOptions opts = new() {
			AllowDuplicateProperties = false,
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
		};
		await using FileStream stream = File.OpenRead(path);
		using JsonDocument doc = await JsonDocument.ParseAsync(stream, opts, cancellationToken: ct);
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
		List<ModRelationshipManifest> relationships = readRelationships(root, sourceName);
		ModAssetsManifest assets = readAssets(root, sourceName);
		return type switch {
			ModPackageKind.Content => parseContent(root, id, version, displayName, relationships, assets, sourceName),
			ModPackageKind.Code => parseCode(root, id, version, displayName, relationships, assets, sourceName),
			_ => throw new UnreachableException(),
		};
	}

	private static ContentModManifest parseContent(
		JsonElement root,
		string id,
		Semver version,
		string? displayName,
		IReadOnlyList<ModRelationshipManifest> relationships,
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
			Relationships = relationships,
			Assets = assets,
		};
	}

	private static CodeModManifest parseCode(
		JsonElement root,
		string id,
		Semver version,
		string? displayName,
		IReadOnlyList<ModRelationshipManifest> relationships,
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
			Relationships = relationships,
			Assets = assets,
			EntryAssembly = entryAssembly,
			CodeHotReload = codeHotReload,
			Patching = patching,
		};
	}

	private static List<ModRelationshipManifest> readRelationships(JsonElement root, string sourceName) {
		JsonElement relationships = requiredArray(root, "relationships", sourceName);
		List<ModRelationshipManifest> list = new();
		HashSet<(string OwnerID, ModRelationshipKind Kind)> seenExact = new();
		foreach (JsonElement rel in relationships.EnumerateArray()) {
			if (rel.ValueKind != JsonValueKind.Object)
				throw new JsonException("relationships array member must be an object");
			string ownerID = requiredString(rel, "id", sourceName);
			ModRelationshipKind kind = ModRelationshipKind.Enum.FromTag(requiredEnum<ModRelationshipKind.Case>(rel, "kind", sourceName));
			Semver? version = null;
			if (kind.Tag is ModRelationshipKind.Case.RequiresSelfAfter or ModRelationshipKind.Case.RequiresSelfBefore) {
				version = Semver.Parse(requiredString(rel, "version", sourceName));
			} else if (kind.Tag is ModRelationshipKind.Case.IfPresentSelfAfter or ModRelationshipKind.Case.IfPresentSelfBefore) {
				string? s = optionalString(rel, "version");
				if (s is not null)
					version = Semver.Parse(s);
			} else {
				rejectIfPresent(rel, "version", sourceName);
			}
			if (!seenExact.Add((ownerID, kind)))
				throw new JsonException($"duplicate relationship '{kind}' for owner '{ownerID}'");
			list.Add(new ModRelationshipManifest {
				OwnerID = ownerID,
				Kind = kind,
				Version = version,
			});
		}
		return list;
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

	private static JsonElement requiredObject(JsonElement root, string name, string sourceName) {
		if (!root.TryGetProperty(name, out JsonElement val) || val.ValueKind != JsonValueKind.Object)
			throw new ModLoadException($"{sourceName}: required object field '{name}' is missing");
		return val;
	}

	private static JsonElement requiredArray(JsonElement root, string name, string sourceName) {
		if (!root.TryGetProperty(name, out JsonElement val) || val.ValueKind != JsonValueKind.Array)
			throw new ModLoadException($"{sourceName}: required array field '{name}' is missing");
		return val;
	}

	private static string requiredString(JsonElement root, string name, string sourceName) {
		if (!root.TryGetProperty(name, out JsonElement val) || val.ValueKind != JsonValueKind.String)
			throw new ModLoadException($"{sourceName}: required string field '{name}' is missing");
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
