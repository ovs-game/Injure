// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Injure.Analyzers.Attributes;

namespace Injure.ModKit.Abstractions;

public readonly record struct ReloadGeneration(string OwnerID, ulong Value) {
	public override string ToString() => $"{OwnerID}@{Value}";
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ReloadInvalidationReason {
	public enum Case {
		Reload = 1,
		Disable,
		Shutdown,
		FailureRollback,
		PartialReload
	}
}

public sealed class ReloadInvalidationContext {
	public required string OwnerID { get; init; }
	public required ReloadGeneration OldGeneration { get; init; }
	public required ReloadInvalidationReason Reason { get; init; }
}

public interface IReloadInvalidatable {
	void Invalidate(ReloadInvalidationContext ctx);
}

public abstract class ReloadBoundObject : IReloadInvalidatable {
	private readonly ReloadGeneration? generation;
	private int invalidated = 0;
	public bool IsInvalidated => Volatile.Read(ref invalidated) != 0;

	protected ReloadBoundObject() {
	}

	protected ReloadBoundObject(ReloadGeneration generation) {
		this.generation = generation;
	}

	public void Invalidate(ReloadInvalidationContext ctx) {
		if (Interlocked.Exchange(ref invalidated, 1) != 0)
			return;
		OnInvalidated(ctx);
	}

	protected void ThrowIfInvalidated() {
		if (!IsInvalidated)
			return;
		throw new ReloadGenerationExpiredException(generation);
	}

	protected virtual void OnInvalidated(ReloadInvalidationContext ctx) {
	}
}

public sealed class ReloadGenerationExpiredException(ReloadGeneration? gen) : InvalidOperationException($"object belongs to expired reload generation {gen?.ToString() ?? "<unknown>"}") {
	public ReloadGeneration? Generation { get; } = gen;
}

public readonly record struct ReloadWeakReferenceSnapshot(
	ReloadGeneration Generation,
	string Category,
	string Description,
	bool IsAlive,
	string TargetTypeName
);

public readonly record struct ReloadGenerationInvalidationFailure(
	ReloadGeneration Generation,
	int Index,
	string Operation,
	string ExceptionType,
	string Message,
	string Details
) {
	public static ReloadGenerationInvalidationFailure FromException(ReloadGeneration generation, int index, string operation, Exception ex) =>
		new(generation, index, operation, ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.ToString());
}

public sealed class ReloadGenerationInvalidationException(
	ReloadGeneration generation,
	ReloadInvalidationReason reason,
	IReadOnlyList<ReloadGenerationInvalidationFailure> failures
) : Exception($"reload generation invalidation failed for '{generation}' with {failures.Count} failure(s)") {
	public ReloadGeneration Generation { get; } = generation;
	public ReloadInvalidationReason Reason { get; } = reason;
	public IReadOnlyList<ReloadGenerationInvalidationFailure> Failures { get; } = failures;
}

public sealed class ReloadGenerationScope(ReloadGeneration generation) : IAsyncDisposable {
	private readonly record struct TrackedWeakReference(WeakReference Reference, string Category, string Description);

	private readonly Lock @lock = new();
	private List<object>? items = new();
	private readonly List<TrackedWeakReference> weakRefs = new();
	private readonly CancellationTokenSource stoppingCts = new();
	private bool invalidated = false;

	public ReloadGeneration Generation { get; } = generation;
	public CancellationToken Stopping => stoppingCts.Token;
	public bool IsInvalidated {
		get {
			lock (@lock)
				return invalidated;
		}
	}

	public void Track(IReloadInvalidatable item) => add(item);
	public void Track(IDisposable item) => add(item);
	public void Track(IAsyncDisposable item) => add(item);
	private void add(object item) {
		ArgumentNullException.ThrowIfNull(item);
		lock (@lock) {
			if (invalidated || items is null)
				throw new ReloadGenerationExpiredException(Generation);
			items.Add(item);
		}
	}

	public void TrackWeak(object item, string category, string description = "") {
		ArgumentNullException.ThrowIfNull(item);
		if (string.IsNullOrWhiteSpace(category))
			throw new ArgumentException("category cannot be null/empty/whitespace", nameof(category));
		lock (@lock) {
			if (invalidated)
				throw new ReloadGenerationExpiredException(Generation);
			weakRefs.Add(new TrackedWeakReference(new WeakReference(item), category, description));
		}
	}

	public IReadOnlyList<ReloadWeakReferenceSnapshot> SnapshotWeakReferences() {
		TrackedWeakReference[] snapshot;
		lock (@lock)
			snapshot = weakRefs.ToArray();

		ReloadWeakReferenceSnapshot[] result = new ReloadWeakReferenceSnapshot[snapshot.Length];
		for (int i = 0; i < snapshot.Length; i++) {
			object? target = snapshot[i].Reference.Target;
			result[i] = new ReloadWeakReferenceSnapshot(
				Generation,
				snapshot[i].Category,
				snapshot[i].Description,
				target is not null,
				target?.GetType().FullName ?? "<collected>"
			);
		}
		return result;
	}

	public async ValueTask InvalidateAsync(ReloadInvalidationReason reason, CancellationToken ct) {
		object[] snapshot;
		lock (@lock) {
			if (invalidated || items is null)
				return;
			ct.ThrowIfCancellationRequested();
			invalidated = true;
			snapshot = new object[items.Count];
			for (int i = 0; i < items.Count; i++)
				snapshot[i] = items[items.Count - i - 1];
			items.Clear();
			items = null;
		}

		List<ReloadGenerationInvalidationFailure> failures = new();
		try {
			try {
				stoppingCts.Cancel();
			} catch (Exception ex) {
				failures.Add(ReloadGenerationInvalidationFailure.FromException(Generation, -1, "cancel generation stopping token", ex));
			}

			ReloadInvalidationContext ctx = new() {
				OwnerID = Generation.OwnerID,
				OldGeneration = Generation,
				Reason = reason,
			};

			for (int i = 0; i < snapshot.Length; i++) {
				try {
					if (snapshot[i] is IReloadInvalidatable inv)
						inv.Invalidate(ctx);
					else if (snapshot[i] is IAsyncDisposable ad)
						await ad.DisposeAsync().ConfigureAwait(false);
					else if (snapshot[i] is IDisposable d)
						d.Dispose();
				} catch (Exception ex) {
					failures.Add(ReloadGenerationInvalidationFailure.FromException(Generation, i, "invalidate/dispose item", ex));
				}
			}
		} finally {
			Array.Clear(snapshot); // clear strong refs to alc in case the async state machine makes the array live longer than expected
		}

		if (failures.Count > 0)
			throw new ReloadGenerationInvalidationException(Generation, reason, failures);
	}

	public ValueTask DisposeAsync() => InvalidateAsync(ReloadInvalidationReason.Shutdown, CancellationToken.None);
}
