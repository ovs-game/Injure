// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Reflection;

using Injure.Analyzers.Attributes;
using Injure.ModKit.Abstractions;
using Injure.ModKit.Loader;
using Injure.ModKit.MonoMod;

namespace Injure.ModKit.Runtime;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct RuntimePhase {
	public enum Case {
		Empty = 1,
		Discovered,
		Resolved,
		Staged,
		CodeLoaded,
		HooksDiscovered,
		Loaded,
		LoadHooksApplied,
		Linked,
		//LinkHooksApplied,
		Active,
		Faulted,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ReloadRequestKind {
	public enum Case {
		SafeBoundary = 1,
		Live,
	}
}

internal readonly record struct ModSource(string RootDirectory, string ManifestPath);
internal readonly record struct DiscoveredMod(ModSource Source, ModManifest Manifest);
internal readonly record struct ResolvedMod(ModManifest Manifest, ModSource Source);
internal readonly record struct ResolvedModGraph(
	IReadOnlyDictionary<string, ResolvedMod> Mods,
	IReadOnlyDictionary<string, string[]> OutgoingOrderEdges,
	IReadOnlyDictionary<string, string[]> ReloadDependentsByTarget,
	ModWavePlan Waves,
	IReadOnlyList<ResolvedMod> ModsInDeterministicOrder
);
internal readonly record struct ModWavePlan(IReadOnlyList<IReadOnlyList<string>> Waves);
internal readonly record struct StagedMod(ModSource Source, ModManifest Manifest, string StagedRoot, ReloadGeneration Generation, string? MainAssemblyPath);

internal sealed class LoadedContentMod : IStrongRefDroppable {
	public required StagedMod Staged { get; init; }
	public required OwnerScope OwnerScope {
		get => field ?? throw new InternalStateException("mod owner scope strong ref has already been dropped");
		set;
	}
	public required ReloadGenerationScope GenerationScope {
		get => field ?? throw new InternalStateException("mod generation scope strong ref has already been dropped");
		set;
	}

	public void DropStrongReferences() {
		OwnerScope = null!;
		GenerationScope = null!;
	}
}

internal sealed class LoadedCodeMod<TGameApi> : IStrongRefDroppable {
	public required StagedMod Staged { get; init; }
	public required ModAlc AssemblyLoadContext {
		get => field ?? throw new InternalStateException("mod ALC strong ref has already been dropped");
		set;
	}
	public required Assembly Assembly {
		get => field ?? throw new InternalStateException("mod assembly strong ref has already been dropped");
		set;
	}
	public required IModEntrypoint<TGameApi> Entrypoint {
		get => field ?? throw new InternalStateException("mod entrypoint strong ref has already been dropped");
		set;
	}
	public required OwnerScope OwnerScope {
		get => field ?? throw new InternalStateException("mod owner scope strong ref has already been dropped");
		set;
	}
	public required ReloadGenerationScope GenerationScope {
		get => field ?? throw new InternalStateException("mod generation scope strong ref has already been dropped");
		set;
	}
	private GenerationPatchSet? loadHooksBacking;
	public required GenerationPatchSet LoadHooks {
		get => loadHooksBacking ?? throw new InternalStateException("mod patch declaration set strong ref has already been dropped");
		set => loadHooksBacking = value;
	}
	public bool Active { get; set; }

	public void DropStrongReferences() {
		AssemblyLoadContext = null!;
		Assembly = null!;
		Entrypoint = null!;
		OwnerScope = null!;
		GenerationScope = null!;
		loadHooksBacking?.DropStrongReferences();
		loadHooksBacking = null;
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
internal readonly partial struct ReloadResultKind {
	public enum Case {
		Succeeded = 1,
		RollbackSucceeded,
	}
}

internal sealed class PendingAlcUnload(string ownerID, ModAlc alc) : IStrongRefDroppable {
	public string OwnerID { get; } = ownerID;
	public ModAlc Alc {
		get => field ?? throw new InternalStateException("mod ALC strong ref has already been dropped");
		private set;
	} = alc;

	public void DropStrongReferences() {
		Alc = null!;
	}
}

internal readonly record struct ReloadResult(ReloadResultKind Kind, IReadOnlySet<string> ReloadSet, ExceptionSnapshot? Failure, IReadOnlyList<PendingAlcUnload> PendingUnloads) {
	public static ReloadResult Succeeded(IReadOnlySet<string> reloadSet) =>
		new(ReloadResultKind.Succeeded, reloadSet, null, []);
	public static ReloadResult RollbackSucceeded(IReadOnlySet<string> reloadSet, ExceptionSnapshot failure, IReadOnlyList<PendingAlcUnload> pendingUnloads) =>
		new(ReloadResultKind.RollbackSucceeded, reloadSet, failure, pendingUnloads);
}
