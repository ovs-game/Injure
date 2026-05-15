// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Injure.ModKit.Abstractions;

public sealed class OwnerScopeDisposeException(IReadOnlyList<OwnerScopeDisposeFailure> failures) : Exception($"owner scope disposal failed for {failures.Count} registration(s)") {
	public IReadOnlyList<OwnerScopeDisposeFailure> Failures { get; } = failures;
}

public readonly record struct OwnerScopeDisposeFailure(
	int Index,
	bool Ordered,
	string ExceptionType,
	string Message,
	string Details
) {
	public static OwnerScopeDisposeFailure FromException(int index, bool ordered, Exception ex) =>
		new(index, ordered, ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.ToString());
}

public sealed class OwnerScope(string ownerID, int maxParallelDisposals = 8) : IAsyncDisposable {
	private struct OwnedDisposable {
		private IDisposable? disposable;
		private IAsyncDisposable? asyncDisposable;

		public OwnedDisposable(IDisposable disposable) {
			this.disposable = disposable;
			asyncDisposable = null;
		}

		public OwnedDisposable(IAsyncDisposable asyncDisposable) {
			disposable = null;
			this.asyncDisposable = asyncDisposable;
		}

		public ValueTask DisposeAsync() {
			IDisposable? d = disposable;
			IAsyncDisposable? ad = asyncDisposable;

			disposable = null;
			asyncDisposable = null;

			if (ad is not null)
				return ad.DisposeAsync();
			d!.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	private readonly Lock @lock = new();
	private List<OwnedDisposable>? parallel = new();
	private List<OwnedDisposable>? orderedAfter = new();

	private bool disposing;

	public string OwnerID { get; } = ownerID;
	public int MaxParallelDisposals { get; } = maxParallelDisposals;

	public void Add(IDisposable disposable) {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: false);
	}

	public void Add(IAsyncDisposable disposable) {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: false);
	}

	public void AddOrdered(IDisposable disposable) {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: true);
	}

	public void AddOrdered(IAsyncDisposable disposable) {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: true);
	}

	private void add(OwnedDisposable cleanup, bool ordered) {
		lock (@lock) {
			ObjectDisposedException.ThrowIf(disposing || parallel is null || orderedAfter is null, this);
			(ordered ? orderedAfter : parallel).Add(cleanup);
		}
	}

	public async ValueTask DisposeAsync() {
		OwnedDisposable[] parallelSnapshot;
		OwnedDisposable[] orderedSnapshot;

		lock (@lock) {
			if (disposing || parallel is null || orderedAfter is null)
				return;
			disposing = true;

			parallelSnapshot = parallel.ToArray();
			orderedSnapshot = orderedAfter.ToArray();

			parallel.Clear();
			parallel = null;
			orderedAfter.Clear();
			orderedAfter = null;
		}

		List<OwnerScopeDisposeFailure>? failures = null;
		try {
			await disposeParallelAsync(
				parallelSnapshot,
				Math.Max(1, MaxParallelDisposals),
				failure => (failures ??= new()).Add(failure)
			).ConfigureAwait(false);
		} finally {
			Array.Clear(parallelSnapshot); // clear strong refs to alc in case the async state machine makes the array live longer than expected
		}

		try {
			for (int i = orderedSnapshot.Length - 1; i >= 0; i--) {
				try {
					await orderedSnapshot[i].DisposeAsync().ConfigureAwait(false);
				} catch (Exception ex) {
					(failures ??= new()).Add(OwnerScopeDisposeFailure.FromException(i, ordered: true, ex));
				}
			}
		} finally {
			Array.Clear(orderedSnapshot); // clear strong refs to alc in case the async state machine makes the array live longer than expected
		}

		if (failures is { Count: > 0 })
			throw new OwnerScopeDisposeException(failures);
	}

	private static async ValueTask disposeParallelAsync(OwnedDisposable[] disps, int workerCount, Action<OwnerScopeDisposeFailure> addFailure) {
		if (disps.Length == 0)
			return;
		int next = -1;
		Lock failureLock = new();
		Task[] workers = new Task[Math.Min(workerCount, disps.Length)];
		for (int worker = 0; worker < workers.Length; worker++) {
			workers[worker] = Task.Run(async () => {
				for (;;) {
					int i = Interlocked.Increment(ref next);
					if ((uint)i >= (uint)disps.Length)
						return;
					try {
						await disps[i].DisposeAsync().ConfigureAwait(false);
					} catch (Exception ex) {
						OwnerScopeDisposeFailure failure = OwnerScopeDisposeFailure.FromException(i, ordered: false, ex);
						lock (failureLock)
							addFailure(failure);
					} finally {
						disps[i] = default; // same as earlier, clear potential strong refs
					}
				}
			});
		}
		await Task.WhenAll(workers).ConfigureAwait(false);
	}
}
