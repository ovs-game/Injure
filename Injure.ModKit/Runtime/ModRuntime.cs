// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Injure.ModKit.Abstractions;
using Injure.ModKit.Loader;
using Injure.ModKit.MonoMod;

namespace Injure.ModKit.Runtime;

public readonly record struct ModApiFactoryContext(
	string OwnerID,
	OwnerScope OwnerScope
);

public readonly record struct ModRuntimeOptions<TGameApi> {
	public required string ModDirectory { get; init; }
	public required string CacheDirectory { get; init; }
	public required Func<ModApiFactoryContext, TGameApi> ApiFactory { get; init; }
	public required IReadOnlyList<string> SharedAssemblies { get; init; }
	public required IReadOnlyList<Assembly> HookTargetStoreAssemblies { get; init; }
	public required int MaxParallelCodeLoads { get; init; }
}

public sealed class ModLoadLinkContextImpl<TGameApi>(IReadOnlyDictionary<string, LoadedOwnerInfo> loaded) : IModLoadContext<TGameApi>, IModLinkContext<TGameApi> {
	private readonly IReadOnlyDictionary<string, LoadedOwnerInfo> loaded = loaded;

	public required string OwnerID { get; init; }
	public required Semver Version { get; init; }
	public required TGameApi Api { get; init; }
	public required OwnerScope OwnerScope { get; init; }
	public required ReloadGenerationScope GenerationScope { get; init; }
	public required CancellationToken UnloadToken { get; init; }

	public bool TryGetLoadedDependency(string id, out LoadedOwnerInfo info) => loaded.TryGetValue(id, out info);
}

public sealed class ModRuntime<TGameApi>(ModRuntimeOptions<TGameApi> options) {
	private sealed class ReloadTransaction {
		public required HashSet<string> ReloadSet { get; init; }
		public required IReadOnlyList<DiscoveredMod> CandidateDiscovered { get; init; }
		public required ResolvedModGraph CandidateGraph { get; init; }
		public required IReadOnlyList<StagedMod> ReplacementStaged { get; init; }
		public required Dictionary<string, LoadedCodeMod<TGameApi>> OldCode { get; init; }
		public required Dictionary<string, LoadedContentMod> OldContent { get; init; }
		public required Dictionary<string, LoadedCodeMod<TGameApi>> ReplacementCode { get; init; }
		public required Dictionary<string, LoadedContentMod> ReplacementContent { get; init; }

		public void DropReplacementStrongReferences() {
			foreach (LoadedCodeMod<TGameApi> mod in ReplacementCode.Values)
				mod.DropStrongReferences();
			foreach (LoadedContentMod mod in ReplacementContent.Values)
				mod.DropStrongReferences();
			ReplacementCode.Clear();
			ReplacementContent.Clear();
		}

		public void DropContainersOnly() {
			OldCode.Clear();
			OldContent.Clear();
			ReplacementCode.Clear();
			ReplacementContent.Clear();
		}
	}

	private enum OpKind {
		Reload,
	}

	private readonly record struct PendingOp(
		ulong Seq,
		OpKind Kind,
		string OwnerID,
		ReloadRequestKind? ReloadKind
	);

	private enum ReloadBoundaryKind {
		Safe,
		Live,
	}

	public const string ManifestJson = "manifest.json";

	private readonly int maxParallelDomains = options.MaxParallelCodeLoads;
	private readonly string modDir = options.ModDirectory;
	private readonly string cacheDir = options.CacheDirectory;
	private readonly Func<ModApiFactoryContext, TGameApi> apiFactory = options.ApiFactory;
	private readonly IReadOnlyList<string> sharedAssemblies = options.SharedAssemblies;
	private readonly SemaphoreSlim codeLoadSem = new(options.MaxParallelCodeLoads, options.MaxParallelCodeLoads);
	private readonly SemaphoreSlim writeLock = new(1, 1);

	private RuntimePhase phase = RuntimePhase.Empty;
	private IReadOnlyList<DiscoveredMod> discovered = Array.Empty<DiscoveredMod>();
	private ResolvedModGraph activeGraph;
	private IReadOnlyList<StagedMod> staged = Array.Empty<StagedMod>();
	private readonly Dictionary<string, LoadedCodeMod<TGameApi>> activeCode = new(StringComparer.Ordinal);
	private readonly Dictionary<string, LoadedContentMod> activeContent = new(StringComparer.Ordinal);
	private readonly HookTargetResolver hookTargetResolver = new(options.HookTargetStoreAssemblies);
	private ulong nextGeneration = 0;

	private readonly Lock opLock = new();
	private readonly List<PendingOp> pendingOps = new();
	private ulong nextOpSeq = 0;

	public async ValueTask StartAsync(CancellationToken ct) {
		await DiscoverAsync(ct).ConfigureAwait(false);
		await ResolveAsync(ct).ConfigureAwait(false);
		await StageAsync(ct).ConfigureAwait(false);
		await LoadCodeAsync(ct).ConfigureAwait(false);
		await DiscoverHooksAsync(ct).ConfigureAwait(false);
		await LoadAsync(ct).ConfigureAwait(false);
		await ApplyLoadHooksAsync(ct).ConfigureAwait(false);
		await LinkAsync(ct).ConfigureAwait(false);
		await ActivateAsync(ct).ConfigureAwait(false);
	}

	public async ValueTask DiscoverAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Empty, nameof(DiscoverAsync));
		List<DiscoveredMod> result = new();
		foreach (string manifestPath in Directory.EnumerateFiles(modDir, ManifestJson, SearchOption.AllDirectories)) {
			ct.ThrowIfCancellationRequested();
			ModManifest manifest = await ManifestReader.ReadAsync(manifestPath, ct).ConfigureAwait(false);
			string root = Path.GetDirectoryName(manifestPath)!;
			result.Add(new DiscoveredMod(new ModSource(root, manifestPath), manifest));
		}
		discovered = result;
		phase = RuntimePhase.Discovered;
	}

	public ValueTask ResolveAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Discovered, nameof(ResolveAsync));
		ct.ThrowIfCancellationRequested();
		activeGraph = ModRelationshipResolver.Resolve(discovered);
		phase = RuntimePhase.Resolved;
		return ValueTask.CompletedTask;
	}

	public async ValueTask StageAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Resolved, nameof(StageAsync));
		ResolvedModGraph graph = activeGraph;
		List<StagedMod> result = new();
		foreach (ResolvedMod mod in graph.ModsInDeterministicOrder) {
			ct.ThrowIfCancellationRequested();
			result.Add(await stageOneAsync(mod.Source, mod.Manifest, ct).ConfigureAwait(false));
		}
		staged = result;
		phase = RuntimePhase.Staged;
	}

	public async ValueTask LoadCodeAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Staged, nameof(LoadCodeAsync));
		foreach (StagedMod mod in staged) {
			ct.ThrowIfCancellationRequested();
			if (mod.Manifest is CodeModManifest) {
				LoadedCodeMod<TGameApi> loaded = await loadCodeModBoundedAsync(mod, ct).ConfigureAwait(false);
				activeCode.Add(mod.Manifest.OwnerID, loaded);
			} else if (mod.Manifest is ContentModManifest) {
				activeContent.Add(mod.Manifest.OwnerID, createLoadedContentMod(mod));
			}
		}
		phase = RuntimePhase.CodeLoaded;
	}

	public async ValueTask DiscoverHooksAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.CodeLoaded, nameof(DiscoverHooksAsync));
		ct.ThrowIfCancellationRequested();
		foreach (LoadedCodeMod<TGameApi> mod in activeCode.Values)
			HookDiscoverer<TGameApi>.DiscoverLoadHooks(mod, hookTargetResolver);
		phase = RuntimePhase.HooksDiscovered;
	}

	public async ValueTask LoadAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.HooksDiscovered, nameof(LoadAsync));
		ct.ThrowIfCancellationRequested();
		Dictionary<string, LoadedOwnerInfo> owners = buildOwnerInfo(staged);
		await Task.WhenAll(activeCode.Values.Select(mod => runLoadAsync(mod, owners, ct).AsTask())).ConfigureAwait(false);
		phase = RuntimePhase.Loaded;
	}

	public async ValueTask ApplyLoadHooksAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Loaded, nameof(ApplyLoadHooksAsync));
		ct.ThrowIfCancellationRequested();
		await HookApplier<TGameApi>.ApplyLoadHooksAsync(activeCode.Values.ToArray(), maxParallelDomains, ct).ConfigureAwait(false);
		phase = RuntimePhase.LoadHooksApplied;
	}	

	public async ValueTask LinkAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.LoadHooksApplied, nameof(LinkAsync));
		ResolvedModGraph graph = activeGraph;
		Dictionary<string, LoadedOwnerInfo> owners = buildOwnerInfo(staged);
		foreach (IReadOnlyList<string> wave in graph.Waves.Waves) {
			ct.ThrowIfCancellationRequested();
			List<Task> tasks = new();
			foreach (string id in wave)
				if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
					tasks.Add(runLinkAsync(mod, owners, ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
		phase = RuntimePhase.Linked;
	}

	public async ValueTask ActivateAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Linked, nameof(ActivateAsync));
		ct.ThrowIfCancellationRequested();
		ResolvedModGraph graph = activeGraph;
		await activateSetAsync(activeCode.Keys.ToHashSet(), graph, activeCode, ct).ConfigureAwait(false);
		phase = RuntimePhase.Active;
	}

	public void RequestReload(string ownerID, ReloadRequestKind kind) {
		requirePhase(RuntimePhase.Active, nameof(RequestReload));
		lock (opLock)
			pendingOps.Add(new PendingOp(Seq: ++nextOpSeq, Kind: OpKind.Reload, OwnerID: ownerID, ReloadKind: kind));
	}

	public void AtSafeBoundary(CancellationToken ct = default) => block(processBoundaryAsync(ReloadBoundaryKind.Safe, ct));
	public void AtLiveBoundary(CancellationToken ct = default) => block(processBoundaryAsync(ReloadBoundaryKind.Live, ct));
	public ValueTask AtSafeBoundaryAsync(CancellationToken ct = default) => processBoundaryAsync(ReloadBoundaryKind.Safe, ct);
	public ValueTask AtLiveBoundaryAsync(CancellationToken ct = default) => processBoundaryAsync(ReloadBoundaryKind.Live, ct);

	private static void block(ValueTask task) {
		if (task.IsCompletedSuccessfully) {
			task.GetAwaiter().GetResult();
			return;
		}
		task.AsTask().GetAwaiter().GetResult();
	}

	private PendingOp[] takePendingBatch(ReloadBoundaryKind boundary) {
		lock (opLock) {
			List<PendingOp> batch = new();
			for (int i = pendingOps.Count - 1; i >= 0; i--) {
				PendingOp op = pendingOps[i];
				if (!isEligibleAtBoundary(op, boundary))
					continue;
				batch.Add(op);
				pendingOps.RemoveAt(i);
			}
			batch.Sort(static (a, b) => a.Seq.CompareTo(b.Seq));
			return batch.Count == 0 ? Array.Empty<PendingOp>() : batch.ToArray();
		}
	}

	private async ValueTask processBoundaryAsync(ReloadBoundaryKind boundary, CancellationToken ct) {
		await writeLock.WaitAsync(ct).ConfigureAwait(false);
		try {
			for (;;) {
				PendingOp[] batch = takePendingBatch(boundary);
				if (batch.Length == 0)
					return;

				ReloadRequestKind request = ReloadRequestKind.SafeBoundary;
				HashSet<string> roots = new(StringComparer.Ordinal);
				foreach (PendingOp op in batch) {
					if (op.Kind == OpKind.Reload)
						roots.Add(op.OwnerID);
					if (op.ReloadKind == ReloadRequestKind.Live)
						request = ReloadRequestKind.Live;
				}
				if (roots.Count == 0)
					continue;

				ResolvedModGraph oldGraph = activeGraph;
				HashSet<string> reloadSet = ReloadClosure.Compute(roots, oldGraph.ReloadDependentsByTarget);
				validateReloadCapabilities(reloadSet, request);

				ReloadTransaction transaction = await prepareReloadSetAsync(reloadSet, request, ct).ConfigureAwait(false);
				ReloadResult r = await commitReloadAsync(transaction, request, ct).ConfigureAwait(false);
				transaction.DropContainersOnly();
#pragma warning disable IDE0059 // unnecessary assignment to local
				transaction = null!;
#pragma warning restore IDE0059 // unnecessary assignment to local
				foreach (PendingAlcUnload pending in r.PendingUnloads) {
					WeakReference weak = beginUnload(pending);
					if (!probeUnload(weak, maxAttempts: 8))
						Console.Error.WriteLine($"warning: ALC for {pending.OwnerID} is still alive after unload request");
				}
			}
		} finally {
			writeLock.Release();
		}
	}

	private async ValueTask<ReloadTransaction> prepareReloadSetAsync(HashSet<string> reloadSet, ReloadRequestKind request, CancellationToken ct) {
		Dictionary<string, DiscoveredMod> candidateDiscovered = discovered.ToDictionary(static mod => mod.Manifest.OwnerID, StringComparer.Ordinal);
		foreach (string id in reloadSet) {
			DiscoveredMod old = candidateDiscovered[id];
			ModManifest manifest = await ManifestReader.ReadAsync(old.Source.ManifestPath, ct).ConfigureAwait(false);
			candidateDiscovered[id] = new DiscoveredMod(old.Source, manifest);
		}

		ResolvedModGraph candidateGraph = ModRelationshipResolver.Resolve(candidateDiscovered.Values.ToArray());
		validateCandidateReloadCapabilities(candidateGraph, reloadSet, request);

		List<StagedMod> replacementStaged = new();
		foreach (string id in reloadSet) {
			ResolvedMod resolved = candidateGraph.Mods[id];
			replacementStaged.Add(await stageOneAsync(resolved.Source, resolved.Manifest, ct).ConfigureAwait(false));
		}

		Dictionary<string, LoadedCodeMod<TGameApi>> oldCode = activeCode
			.Where(pair => reloadSet.Contains(pair.Key))
			.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
		Dictionary<string, LoadedContentMod> oldContent = activeContent
			.Where(pair => reloadSet.Contains(pair.Key))
			.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

		Dictionary<string, LoadedCodeMod<TGameApi>> replacementCode = new(StringComparer.Ordinal);
		Dictionary<string, LoadedContentMod> replacementContent = new(StringComparer.Ordinal);
		Dictionary<string, LoadedOwnerInfo> candidateOwners = buildOwnerInfo(candidateDiscovered.Values.Select(static mod => mod.Manifest));
		foreach (StagedMod stagedMod in replacementStaged) {
			if (stagedMod.Manifest is CodeModManifest) {
				LoadedCodeMod<TGameApi> loaded = await loadCodeModBoundedAsync(stagedMod, ct).ConfigureAwait(false);
				replacementCode.Add(stagedMod.Manifest.OwnerID, loaded);
			} else if (stagedMod.Manifest is ContentModManifest) {
				replacementContent.Add(stagedMod.Manifest.OwnerID, createLoadedContentMod(stagedMod));
			}
		}

		try {
			await Task.WhenAll(replacementCode.Values.Select(mod => runLoadAsync(mod, candidateOwners, ct).AsTask())).ConfigureAwait(false);
			foreach (IReadOnlyList<string> wave in candidateGraph.Waves.Waves) {
				List<Task> tasks = new();
				foreach (string id in wave)
					if (replacementCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
						tasks.Add(runLinkAsync(mod, candidateOwners, ct).AsTask());
				try {
					await Task.WhenAll(tasks).ConfigureAwait(false);
				} finally {
					tasks.Clear();
				}
			}
		} catch {
			foreach (LoadedCodeMod<TGameApi> mod in replacementCode.Values)
				await destroyPreparedCodeGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
			foreach (LoadedContentMod mod in replacementContent.Values)
				await destroyPreparedContentGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
			throw;
		}

		return new ReloadTransaction {
			ReloadSet = reloadSet,
			CandidateDiscovered = candidateDiscovered.Values.ToArray(),
			CandidateGraph = candidateGraph,
			ReplacementStaged = replacementStaged,
			OldCode = oldCode,
			OldContent = oldContent,
			ReplacementCode = replacementCode,
			ReplacementContent = replacementContent,
		};
	}

	private async ValueTask<ReloadResult> commitReloadAsync(ReloadTransaction transaction, ReloadRequestKind request, CancellationToken ct) {
		Dictionary<string, ModLiveStateBlob> capturedState = new(StringComparer.Ordinal);
		bool destructiveBoundaryCrossed = false;
		ExceptionSnapshot reloadErr;
		try {
			if (request == ReloadRequestKind.Live)
				foreach (string id in transaction.ReloadSet)
					if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? old) && old.Entrypoint is IModLiveReload live)
						capturedState.Add(id, await live.CaptureReloadStateAsync(ct).ConfigureAwait(false));

			try {
				await deactivateSetAsync(transaction.ReloadSet, activeGraph, reverse: true, ct).ConfigureAwait(false);
			} finally {
				destructiveBoundaryCrossed = true;
			}
			await disposeOldOwnerScopesAsync(transaction.ReloadSet).ConfigureAwait(false);

			foreach (LoadedCodeMod<TGameApi> mod in transaction.ReplacementCode.Values)
				HookDiscoverer<TGameApi>.DiscoverLoadHooks(mod, hookTargetResolver);
			await HookApplier<TGameApi>.ApplyLoadHooksAsync(transaction.ReplacementCode.Values.ToArray(), maxParallelDomains, ct).ConfigureAwait(false);

			if (request == ReloadRequestKind.Live)
				foreach (KeyValuePair<string, ModLiveStateBlob> pair in capturedState)
					if (transaction.ReplacementCode.TryGetValue(pair.Key, out LoadedCodeMod<TGameApi>? next) && next.Entrypoint is IModLiveReload live)
						await live.RestoreReloadStateAsync(pair.Value, ct).ConfigureAwait(false);

			await activateSetAsync(transaction.ReloadSet, transaction.CandidateGraph, transaction.ReplacementCode, ct).ConfigureAwait(false);
			publishReload(transaction);
			foreach (LoadedCodeMod<TGameApi> mod in transaction.OldCode.Values)
				await destroyPreparedCodeGenerationAsync(mod, ReloadInvalidationReason.Reload, ct).ConfigureAwait(false);
			foreach (LoadedContentMod mod in transaction.OldContent.Values)
				await destroyPreparedContentGenerationAsync(mod, ReloadInvalidationReason.Reload, ct).ConfigureAwait(false);
			return ReloadResult.Succeeded(transaction.ReloadSet.ToFrozenSet());
		} catch (Exception ex) {
			reloadErr = ExceptionSnapshot.FromException(ex);
			ex = null!;
		}

		if (!destructiveBoundaryCrossed) {
			foreach (LoadedCodeMod<TGameApi> mod in transaction.ReplacementCode.Values)
				await destroyPreparedCodeGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
			foreach (LoadedContentMod mod in transaction.ReplacementContent.Values)
				await destroyPreparedContentGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
			throw reloadErr.ToException();
		}

		List<PendingAlcUnload> replacementUnloads = new();
		List<ExceptionSnapshot> rollbackErrs = new();
		try {
			foreach (LoadedCodeMod<TGameApi> mod in transaction.ReplacementCode.Values) {
				await destroyPreparedCodeGenerationNoUnloadAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
				replacementUnloads.Add(detachForUnload(mod));
			}
			foreach (LoadedContentMod mod in transaction.ReplacementContent.Values)
				await destroyPreparedContentGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
		} catch (Exception ex) {
			rollbackErrs.Add(ExceptionSnapshot.FromException(ex));
		}

		try {
			foreach (LoadedCodeMod<TGameApi> old in transaction.OldCode.Values)
				old.OwnerScope = new OwnerScope(old.Staged.Manifest.OwnerID);
			foreach (LoadedContentMod old in transaction.OldContent.Values)
				old.OwnerScope = new OwnerScope(old.Staged.Manifest.OwnerID);
			await HookApplier<TGameApi>.ApplyLoadHooksAsync(transaction.OldCode.Values.ToArray(), maxParallelDomains, ct).ConfigureAwait(false);
			await activateSetAsync(transaction.ReloadSet, activeGraph, activeCode, ct).ConfigureAwait(false);
		} catch (Exception ex) {
			rollbackErrs.Add(ExceptionSnapshot.FromException(ex));
		}

		if (rollbackErrs.Count > 0)
			throw new AggregateException("reload failed and rollback also failed", rollbackErrs.Select(static e => e.ToException()).Prepend(reloadErr.ToException()));
		ReloadResult r = ReloadResult.RollbackSucceeded(transaction.ReloadSet.ToFrozenSet(), reloadErr, replacementUnloads);
		transaction.DropReplacementStrongReferences();
		return r;
	}

	private async ValueTask<StagedMod> stageOneAsync(ModSource source, ModManifest manifest, CancellationToken ct) {
		ulong generationVal = Interlocked.Increment(ref nextGeneration);
		ReloadGeneration generation = new(manifest.OwnerID, generationVal);
		string target = Path.Combine(cacheDir, manifest.OwnerID, generationVal.ToString("D8"));
		if (Directory.Exists(target))
			Directory.Delete(target, recursive: true);
		Directory.CreateDirectory(target);
		await copyDirectoryAsync(source.RootDirectory, target, ct).ConfigureAwait(false);
		string? assemblyPath = manifest is CodeModManifest code ? Path.Combine(target, code.EntryAssembly) : null;
		return new StagedMod(source, manifest, target, generation, assemblyPath);
	}

	private static LoadedContentMod createLoadedContentMod(StagedMod stagedMod) {
		return new LoadedContentMod {
			Staged = stagedMod,
			OwnerScope = new OwnerScope(stagedMod.Manifest.OwnerID),
			GenerationScope = new ReloadGenerationScope(stagedMod.Generation),
		};
	}

	private async ValueTask<LoadedCodeMod<TGameApi>> loadCodeModBoundedAsync(StagedMod stagedMod, CancellationToken ct) {
		await codeLoadSem.WaitAsync(ct).ConfigureAwait(false);
		try {
			return await Task.Run(() => loadCodeMod(stagedMod), ct).ConfigureAwait(false);
		} finally {
			codeLoadSem.Release();
		}
	}

	private LoadedCodeMod<TGameApi> loadCodeMod(StagedMod stagedMod) {
		CodeModManifest manifest = (CodeModManifest)stagedMod.Manifest;
		if (stagedMod.MainAssemblyPath is null || !File.Exists(stagedMod.MainAssemblyPath))
			throw new ModLoadException(manifest.OwnerID, $"entry assembly '{manifest.EntryAssembly}' not found");

		ModAlc alc = new(stagedMod.MainAssemblyPath, sharedAssemblies, $"mod:{manifest.OwnerID}:{stagedMod.Generation}");
		Assembly assembly = alc.LoadFromAssemblyPath(stagedMod.MainAssemblyPath);
		validateAssemblyHotReloadAttribute(manifest, assembly);
		Type entryType = EntrypointDiscovery.FindEntrypointType(assembly, typeof(TGameApi), stagedMod.MainAssemblyPath);
		IModEntrypoint<TGameApi> entrypoint = (IModEntrypoint<TGameApi>)Activator.CreateInstance(entryType)!;

		return new LoadedCodeMod<TGameApi> {
			Staged = stagedMod,
			AssemblyLoadContext = alc,
			Assembly = assembly,
			Entrypoint = entrypoint,
			OwnerScope = new OwnerScope(manifest.OwnerID),
			GenerationScope = new ReloadGenerationScope(stagedMod.Generation),
			LoadHooks = new(),
		};
	}

	private async ValueTask runLoadAsync(LoadedCodeMod<TGameApi> mod, IReadOnlyDictionary<string, LoadedOwnerInfo> owners, CancellationToken ct) {
		TGameApi api = createApi(mod);
		ModLoadLinkContextImpl<TGameApi> context = createContext(mod, api, owners);
		await mod.Entrypoint.LoadAsync(context, ct).ConfigureAwait(false);
	}

	private async ValueTask runLinkAsync(LoadedCodeMod<TGameApi> mod, IReadOnlyDictionary<string, LoadedOwnerInfo> owners, CancellationToken ct) {
		TGameApi api = createApi(mod);
		ModLoadLinkContextImpl<TGameApi> context = createContext(mod, api, owners);
		await mod.Entrypoint.LinkAsync(context, ct).ConfigureAwait(false);
	}

	private static async ValueTask activateOneAsync(LoadedCodeMod<TGameApi> mod, CancellationToken ct) {
		await mod.Entrypoint.ActivateAsync(ct).ConfigureAwait(false);
		mod.Active = true;
	}

	private async ValueTask deactivateSetAsync(HashSet<string> set, ResolvedModGraph graph, bool reverse, CancellationToken ct) {
		IEnumerable<IReadOnlyList<string>> waves = reverse ? graph.Waves.Waves.Reverse() : graph.Waves.Waves;
		foreach (IReadOnlyList<string> wave in waves) {
			List<Task> tasks = new();
			foreach (string id in wave)
				if (set.Contains(id) && activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
					tasks.Add(mod.Entrypoint.DeactivateAsync(ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
	}

	private static async ValueTask activateSetAsync(HashSet<string> set, ResolvedModGraph graph, Dictionary<string, LoadedCodeMod<TGameApi>> source, CancellationToken ct) {
		foreach (IReadOnlyList<string> wave in graph.Waves.Waves) {
			List<Task> tasks = new();
			foreach (string id in wave)
				if (set.Contains(id) && source.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
					tasks.Add(activateOneAsync(mod, ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
	}

	private async ValueTask disposeOldOwnerScopesAsync(IReadOnlySet<string> set) {
		foreach (string id in set) {
			if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
				await mod.OwnerScope.DisposeAsync().ConfigureAwait(false);
			if (activeContent.TryGetValue(id, out LoadedContentMod? content))
				await content.OwnerScope.DisposeAsync().ConfigureAwait(false);
		}
	}

	private void publishReload(ReloadTransaction transaction) {
		discovered = transaction.CandidateDiscovered;
		activeGraph = transaction.CandidateGraph;
		foreach (KeyValuePair<string, LoadedCodeMod<TGameApi>> pair in transaction.ReplacementCode)
			activeCode[pair.Key] = pair.Value;
		staged = staged.Where(mod => !transaction.ReloadSet.Contains(mod.Manifest.OwnerID)).Concat(transaction.ReplacementStaged).ToArray();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async ValueTask destroyPreparedCodeGenerationNoUnloadAsync(LoadedCodeMod<TGameApi> mod, ReloadInvalidationReason reason, CancellationToken ct) {
		try {
			await mod.OwnerScope.DisposeAsync().ConfigureAwait(false);
		} finally {
			try {
				await mod.Entrypoint.UnloadAsync(ct).ConfigureAwait(false);
			} finally {
				await mod.GenerationScope.InvalidateAsync(reason, ct).ConfigureAwait(false);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async ValueTask destroyPreparedCodeGenerationAsync(LoadedCodeMod<TGameApi> mod, ReloadInvalidationReason reason, CancellationToken ct) {
		await destroyPreparedCodeGenerationNoUnloadAsync(mod, reason, ct);
		WeakReference weak = beginUnload(detachForUnload(mod));
		if (!probeUnload(weak, maxAttempts: 8))
			Console.Error.WriteLine($"warning: ALC for {mod.Staged.Manifest.OwnerID} is still alive after unload request");
	}

	private static async ValueTask destroyPreparedContentGenerationAsync(LoadedContentMod mod, ReloadInvalidationReason reason, CancellationToken ct) {
		try {
			await mod.OwnerScope.DisposeAsync().ConfigureAwait(false);
		} finally {
			await mod.GenerationScope.InvalidateAsync(reason, ct).ConfigureAwait(false);
		}
	}

	private void validateReloadCapabilities(IReadOnlySet<string> reloadSet, ReloadRequestKind request) {
		foreach (string id in reloadSet) {
			ModManifest manifest = activeGraph.Mods[id].Manifest;
			if (manifest is not CodeModManifest code)
				continue;
			if (!canSatisfyReload(code.CodeHotReload, request))
				throw new ModLoadException(id, $"code-hot-reload '{code.CodeHotReload}' cannot satisfy reload request '{request}'");
			if (request == ReloadRequestKind.Live && activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod) && mod.Entrypoint is not IModLiveReload)
				throw new ModLoadException(id, "code-hot-reload 'live' requires entrypoint to also implement IModLiveReload");
		}
	}

	private static void validateCandidateReloadCapabilities(ResolvedModGraph candidateGraph, IReadOnlySet<string> reloadSet, ReloadRequestKind request) {
		foreach (string id in reloadSet) {
			ModManifest manifest = candidateGraph.Mods[id].Manifest;
			if (manifest is not CodeModManifest code)
				continue;
			if (!canSatisfyReload(code.CodeHotReload, request))
				throw new ModLoadException(id, $"new code-hot-reload '{code.CodeHotReload}' cannot satisfy reload request '{request}'");
		}
	}

	private static bool canSatisfyReload(ModCodeHotReloadLevel level, ReloadRequestKind request) {
		return request.Tag switch {
			ReloadRequestKind.Case.SafeBoundary => level.Tag is ModCodeHotReloadLevel.Case.SafeBoundary or ModCodeHotReloadLevel.Case.Live,
			ReloadRequestKind.Case.Live => level == ModCodeHotReloadLevel.Live,
			_ => false,
		};
	}

	private static bool isEligibleAtBoundary(in PendingOp op, ReloadBoundaryKind boundary) {
		return op.Kind switch {
			OpKind.Reload => op.ReloadKind is ReloadRequestKind request && boundaryAllows(boundary, request),
			_ => false,
		};
	}

	private static bool boundaryAllows(ReloadBoundaryKind boundary, ReloadRequestKind request) {
		return request.Tag switch {
			ReloadRequestKind.Case.SafeBoundary => boundary is ReloadBoundaryKind.Safe or ReloadBoundaryKind.Live,
			ReloadRequestKind.Case.Live => boundary == ReloadBoundaryKind.Live,
			_ => false,
		};
	}

	private static void validateAssemblyHotReloadAttribute(CodeModManifest manifest, Assembly assembly) {
		HotReloadLevelAttribute attribute = assembly.GetCustomAttribute<HotReloadLevelAttribute>() ??
			throw new ModLoadException(manifest.OwnerID, "entry assembly is missing HotReloadLevel attribute");
		if (attribute.Level != (AssemblyHotReloadLevel)manifest.CodeHotReload)
			throw new ModLoadException(manifest.OwnerID, $"manifest code-hot-reload '{manifest.CodeHotReload}' does not match assembly attribute '{attribute.Level}'");
	}

	private TGameApi createApi(LoadedCodeMod<TGameApi> mod) {
		return apiFactory(new ModApiFactoryContext(mod.Staged.Manifest.OwnerID, mod.OwnerScope));
	}

	private static ModLoadLinkContextImpl<TGameApi> createContext(LoadedCodeMod<TGameApi> mod, TGameApi api, IReadOnlyDictionary<string, LoadedOwnerInfo> owners) {
		return new ModLoadLinkContextImpl<TGameApi>(owners) {
			OwnerID = mod.Staged.Manifest.OwnerID,
			Version = mod.Staged.Manifest.Version,
			Api = api,
			OwnerScope = mod.OwnerScope,
			GenerationScope = mod.GenerationScope,
			UnloadToken = mod.GenerationScope.Stopping,
		};
	}

	private static Dictionary<string, LoadedOwnerInfo> buildOwnerInfo(IEnumerable<StagedMod> stagedMods) {
		return buildOwnerInfo(stagedMods.Select(static mod => mod.Manifest));
	}

	private static Dictionary<string, LoadedOwnerInfo> buildOwnerInfo(IEnumerable<ModManifest> manifests) {
		return manifests.ToDictionary(
			static manifest => manifest.OwnerID,
			static manifest => new LoadedOwnerInfo(manifest.OwnerID, manifest.Version),
			StringComparer.Ordinal
		);
	}

	private void requirePhase(RuntimePhase expected, string operation) {
		if (phase != expected)
			throw new InvalidOperationException($"{operation} requires phase {expected}, but current phase is {phase}");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PendingAlcUnload detachForUnload(LoadedCodeMod<TGameApi> mod) {
		string ownerID = mod.Staged.Manifest.OwnerID;
		ModAlc alc = mod.AssemblyLoadContext;
		mod.DropStrongReferences();
		return new PendingAlcUnload(ownerID, alc);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference beginUnload(PendingAlcUnload pending) {
		ModAlc alc = pending.Alc;
		foreach (Assembly asm in alc.Assemblies)
			AssemblyScrubber.ScrubStaticReferenceFields(asm);
		pending.DropStrongReferences();
		WeakReference weak = new(alc, trackResurrection: false);
		alc.Unload();
		return weak;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool probeUnload(WeakReference weak, int maxAttempts) {
		for (int i = 0; i < maxAttempts && weak.IsAlive; i++) {
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}
		return !weak.IsAlive;
	}

	private static async ValueTask copyDirectoryAsync(string source, string target, CancellationToken ct) {
		foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));

		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			ct.ThrowIfCancellationRequested();
			string relative = Path.GetRelativePath(source, file);
			string destination = Path.Combine(target, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			await using FileStream input = File.OpenRead(file);
			await using FileStream output = File.Create(destination);
			await input.CopyToAsync(output, ct).ConfigureAwait(false);
		}
	}
}
