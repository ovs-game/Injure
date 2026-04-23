// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Injure.Assets;

namespace Injure.Tests.Assets;

public static class Extensions {
	public static byte[] ReadAll(this Stream stream) {
		using MemoryStream ms = new();
		stream.CopyTo(ms);
		return ms.ToArray();
	}

	public static TCast[] CastDepsToArray<TCast>(this ReadOnlySpan<IAssetDependency> span) where TCast : IAssetDependency {
		TCast[] arr = new TCast[span.Length];
		for (int i = 0; i < span.Length; i++)
			arr[i] = (TCast)span[i];
		return arr;
	}
}

public sealed class TestAsset(string val) : IRevokable {
	private static ulong nextID = 0;
	private int revoked = 0;
	public ulong ID { get; } = Interlocked.Increment(ref nextID);
	public string Val {
		get {
			if (Volatile.Read(ref revoked) != 0)
				throw new AssetLeaseExpiredException();
			return field;
		}
	} = val;
	public void Revoke() => Volatile.Write(ref revoked, 1);
}

public sealed record TestDependency(string Name) : IAssetDependency;

public sealed class TestDependencyWatcher : IAssetDependencyWatcher<TestDependency> {
	public HashSet<TestDependency> Watched { get; } = new HashSet<TestDependency>();
	public List<string> Log { get; } = new List<string>();
	public bool Disposed { get; private set; } = false;

	public event Action<TestDependency>? Changed;

	public void Watch(TestDependency dependency) {
		Watched.Add(dependency);
		Log.Add($"watch:{dependency.Name}");
	}

	public void Unwatch(TestDependency dependency) {
		Watched.Remove(dependency);
		Log.Add($"unwatch:{dependency.Name}");
	}

	public void Raise(TestDependency dependency) => Changed?.Invoke(dependency);
	public void Dispose() => Disposed = true;
}

public sealed class TestSource(TestDependency? dep = null) : IAssetSource {
	private readonly TestDependency? dep = dep;
	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		byte[] bytes = Encoding.UTF8.GetBytes(info.AssetID.ToString());
		if (dep is not null)
			coll.Add(dep);
		return ValueTask.FromResult(AssetSourceResult.Success(new MemoryStream(bytes, writable: false)));
	}
}

public sealed class DictionarySource : IAssetSource {
	private readonly ConcurrentDictionary<AssetID, string> dict = new();

	public void Set(AssetID id, string s) => dict[id] = s;
	public void Remove(AssetID id) => dict.TryRemove(id, out _);

	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
		if (!dict.TryGetValue(info.AssetID, out string? s))
			return ValueTask.FromResult(AssetSourceResult.NotHandled());
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		return ValueTask.FromResult(AssetSourceResult.Success(new MemoryStream(bytes, writable: false)));
	}
}

public sealed class NonSeekableMemoryStream(byte[] data) : Stream {
	private readonly MemoryStream inner = new(data, writable: false);
	public bool Disposed { get; private set; }
	public override bool CanRead => inner.CanRead;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override void Flush() => inner.Flush();
	public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	protected override void Dispose(bool disposing) {
		Disposed = true;
		inner.Dispose();
		base.Dispose(disposing);
	}
}

public sealed class NonSeekableSource : IAssetSource {
	public NonSeekableMemoryStream? LastStream { get; private set; }
	public ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
		LastStream = new NonSeekableMemoryStream(Encoding.UTF8.GetBytes(info.AssetID.ToString()));
		return ValueTask.FromResult(AssetSourceResult.Success(LastStream));
	}
}

public sealed class TestAssetData(Stream stream, string debugName) : AssetData(debugName) {
	public Stream Stream { get; } = stream;
}

public sealed class TestResolver(TestDependency? dep = null) : IAssetResolver {
	private readonly TestDependency? dep = dep;
	public async ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
		Stream stream = await info.FetchAsync(info.AssetID, ct);
		if (dep is not null)
			coll.Add(dep);
		return AssetResolveResult.Success(new TestAssetData(stream, info.AssetID.ToString()));
	}
}

public sealed class FetchThenNotHandledResolver(TestDependency? dep = null) : IAssetResolver {
	private readonly TestDependency? dep = dep;
	public async ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
		await using Stream _ = await info.FetchAsync(info.AssetID, ct);
		if (dep is not null)
			coll.Add(dep);
		return AssetResolveResult.NotHandled();
	}
}

public sealed class OptionalExtraFetchResolver(AssetID optionalID) : IAssetResolver {
	private readonly AssetID optionalID = optionalID;
	public bool SawOptionalStream { get; private set; }

	public async ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
		await using Stream main = await info.FetchAsync(info.AssetID, ct);
		Stream? optional = await info.TryFetchAsync(optionalID, ct);
		SawOptionalStream = optional is not null;
		optional?.Dispose();
		return AssetResolveResult.Success(new TestAssetData(new MemoryStream(main.ReadAll(), writable: false), info.AssetID.ToString()));
	}
}

public sealed class RequiredExtraFetchResolver(AssetID extraID) : IAssetResolver {
	private readonly AssetID extraID = extraID;
	public async ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
		await using Stream main = await info.FetchAsync(info.AssetID, ct);
		await using Stream extra = await info.FetchAsync(extraID, ct);
		return AssetResolveResult.Success(new TestAssetData(new MemoryStream(main.ReadAll(), writable: false), info.AssetID.ToString()));
	}
}

public sealed class AssetLoadingResolver(AssetStore store, IReadOnlyDictionary<AssetID, AssetID> map) : IAssetResolver {
	private readonly AssetStore store = store;
	public IReadOnlyDictionary<AssetID, AssetID> Map = map;

	public async ValueTask<AssetResolveResult> TryResolveAsync(AssetResolveInfo info, IAssetDependencyCollector coll, CancellationToken ct) {
		Stream stream = await info.FetchAsync(info.AssetID, ct);
		if (Map.TryGetValue(info.AssetID, out AssetID toLoad)) {
			AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(toLoad);
			asset.Warm(ct);
		}
		return AssetResolveResult.Success(new TestAssetData(stream, $"{info.AssetID} + load {toLoad}"));
	}
}

public sealed class TestCreatorPreparedData(byte[] data) : AssetPreparedData {
	public byte[] Data { get; } = data;
}

public sealed class TestCreator(Func<AssetCreateInfo, CancellationToken, Task>? onPrepareAsync = null) : IAssetStagedCreator<TestAsset, TestCreatorPreparedData> {
	private readonly Func<AssetCreateInfo, CancellationToken, Task>? onPrepareAsync = onPrepareAsync;
	private int _prepareCalls;
	private int _finalizeCalls;
	public int PrepareCalls => _prepareCalls;
	public int FinalizeCalls => _finalizeCalls;

	public async ValueTask<AssetPrepareResult<TestCreatorPreparedData>> TryPrepareAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		Interlocked.Increment(ref _prepareCalls);
		ct.ThrowIfCancellationRequested();

		if (onPrepareAsync is not null)
			await onPrepareAsync(info, ct).ConfigureAwait(false);

		ct.ThrowIfCancellationRequested();
		if (info.Data is not TestAssetData d)
			return AssetPrepareResult<TestCreatorPreparedData>.NotHandled();
		return AssetPrepareResult<TestCreatorPreparedData>.Success(new TestCreatorPreparedData(d.Stream.ReadAll()));
	}

	public TestAsset Finalize(AssetFinalizeInfo<TestCreatorPreparedData> info) {
		Interlocked.Increment(ref _finalizeCalls);
		return new TestAsset(Encoding.UTF8.GetString(info.Prepared.Data));
	}
}

public readonly record struct Step(string Val, bool Handled, params TestDependency[] Dependencies);
public sealed class SteppingCreator(params Step[] steps) : IAssetCreator<TestAsset> {
	private sealed class Prepped(string val) : AssetPreparedData {
		public string Val { get; } = val;
	}

	private readonly Step[] steps = steps;
	private int index = 0;

	public ValueTask<AssetCreateResult<TestAsset>> TryCreateAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		ct.ThrowIfCancellationRequested();
		if (info.Data is not TestAssetData d)
			return ValueTask.FromResult(AssetCreateResult<TestAsset>.NotHandled());
		int i = Interlocked.Increment(ref index) - 1;
		Step step = steps[Math.Min(i, steps.Length - 1)];
		foreach (TestDependency dep in step.Dependencies)
			coll.Add(dep);
		d.Stream.Dispose();
		if (step.Handled)
			return ValueTask.FromResult(AssetCreateResult<TestAsset>.Success(new TestAsset(step.Val)));
		else
			return ValueTask.FromResult(AssetCreateResult<TestAsset>.NotHandled());
	}
}

public sealed class TaskCheckpoint {
	private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<bool> _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Task Entered => _entered.Task;
	public void Proceed() => _continue.TrySetResult(true);
	public async Task WaitAsync(CancellationToken ct = default) {
		_entered.TrySetResult(true);
		await _continue.Task.WaitAsync(ct).ConfigureAwait(false);
	}
}

public sealed class CountingTaskCheckpoint(int target) {
	private readonly int target = target;
	private int count;

	private readonly TaskCompletionSource<bool> _targetReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<bool> _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public int EnteredCount => count;
	public Task TargetReached => _targetReached.Task;
	public void Proceed() => _continue.TrySetResult(true);
	public async Task WaitAsync(CancellationToken ct = default) {
		int n = Interlocked.Increment(ref count);
		if (n >= target)
			_targetReached.TrySetResult(true);
		await _continue.Task.WaitAsync(ct).ConfigureAwait(false);
	}
}

public sealed class BlockingOnNthPrepare(int n) {
	private int count;

	public int N { get; } = n;
	public TaskCheckpoint Checkpoint { get; } = new TaskCheckpoint();

	public Task OnPrepareAsync(AssetCreateInfo info, CancellationToken ct) {
		if (Interlocked.Increment(ref count) == N)
			return Checkpoint.WaitAsync(ct);
		return Task.CompletedTask;
	}
}

public sealed class ThreadCheckpoint {
	private readonly ManualResetEventSlim _entered = new(false);
	private readonly ManualResetEventSlim _continue = new(false);

	public ManualResetEventSlim Entered => _entered;
	public void Proceed() => _continue.Set();
	public void Wait() {
		_entered.Set();
		_continue.Wait();
	}
	public void ForceSet() => _entered.Set();
}

public static class AssetTestWait {
	public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 100) {
		long deadline = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < deadline) {
			if (condition())
				return;
			await Task.Delay(1).ConfigureAwait(false);
		}
		if (condition())
			return;
		throw new TimeoutException("timed out waiting for asset test condition");
	}

	public static Task ForQueuedReloadAsync<T>(AssetRef<T> asset, int timeoutMs = 100) where T : class =>
		UntilAsync(() => asset.HasQueuedReload, timeoutMs);

	public static Task ForReloadFailureAsync<T>(AssetRef<T> asset, int timeoutMs = 100) where T : class =>
		UntilAsync(() => asset.LastReloadFailure is not null, timeoutMs);
}

public sealed class TrackingPreparedData(byte[] data, Action onDispose) : AssetPreparedData {
	private readonly Action onDispose = onDispose;
	private int disposed;

	public byte[] Data { get; } = data;
	public bool IsDisposed => Volatile.Read(ref disposed) != 0;

	public override void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) == 0)
			onDispose();
	}
}

public sealed class ControllableCreator(Func<AssetCreateInfo, CancellationToken, Task>? onPrepareAsync = null) : IAssetStagedCreator<TestAsset, TrackingPreparedData> {
	private readonly Func<AssetCreateInfo, CancellationToken, Task>? onPrepareAsync = onPrepareAsync;
	private int prepareCalls;
	private int finalizeCalls;
	private int preparedDisposeCalls;

	public Exception? PrepareException { get; set; }
	public Exception? FinalizeException { get; set; }
	public string? OverrideValue { get; set; }

	public int PrepareCalls => Volatile.Read(ref prepareCalls);
	public int FinalizeCalls => Volatile.Read(ref finalizeCalls);
	public int PreparedDisposeCalls => Volatile.Read(ref preparedDisposeCalls);

	public async ValueTask<AssetPrepareResult<TrackingPreparedData>> TryPrepareAsync(AssetCreateInfo info, IAssetDependencyCollector coll, CancellationToken ct = default) {
		Interlocked.Increment(ref prepareCalls);
		ct.ThrowIfCancellationRequested();

		if (onPrepareAsync is not null)
			await onPrepareAsync(info, ct).ConfigureAwait(false);

		ct.ThrowIfCancellationRequested();
		if (PrepareException is not null)
			throw PrepareException;
		if (info.Data is not TestAssetData d)
			return AssetPrepareResult<TrackingPreparedData>.NotHandled();

		byte[] data = OverrideValue is null ? d.Stream.ReadAll() : Encoding.UTF8.GetBytes(OverrideValue);

		return AssetPrepareResult<TrackingPreparedData>.Success(new TrackingPreparedData(
			data,
			() => Interlocked.Increment(ref preparedDisposeCalls)
		));
	}

	public TestAsset Finalize(AssetFinalizeInfo<TrackingPreparedData> info) {
		Interlocked.Increment(ref finalizeCalls);
		if (FinalizeException is not null)
			throw FinalizeException;
		return new TestAsset(Encoding.UTF8.GetString(info.Prepared.Data));
	}
}
