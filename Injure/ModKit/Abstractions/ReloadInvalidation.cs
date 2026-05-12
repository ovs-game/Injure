// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
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

public interface IReloadInvalidatable {
	void Invalidate(ReloadInvalidationContext context);
}

public interface IReloadGenerationBound {
	ReloadGeneration Generation { get; }
	bool IsInvalidated { get; }
}

public sealed class ReloadInvalidationContext {
	public required string OwnerID { get; init; }
	public required ReloadGeneration OldGeneration { get; init; }
	public required ReloadInvalidationReason Reason { get; init; }
}

public sealed class ReloadGenerationExpiredException(ReloadGeneration gen) : InvalidOperationException($"object belongs to expired reload generation {gen}") {
	public ReloadGeneration Generation { get; } = gen;
}

public sealed class ReloadGenerationScope(ReloadGeneration gen) : IAsyncDisposable {
	private readonly Lock @lock = new();
	private readonly List<object> items = [];
	private bool invalidated;

	public ReloadGeneration Generation { get; } = gen;

	public void Track(IReloadInvalidatable item) => add(item);
	public void Track(IDisposable item) => add(item);
	public void Track(IAsyncDisposable item) => add(item);

	private void add(object item) {
		lock (@lock) {
			if (invalidated)
				throw new ReloadGenerationExpiredException(Generation);
			items.Add(item);
		}
	}

	public async ValueTask InvalidateAsync(ReloadInvalidationReason reason, CancellationToken ct) {
		object[] items;
		lock (@lock) {
			if (invalidated)
				return;
			invalidated = true;
			items = this.items.AsEnumerable().Reverse().ToArray();
			this.items.Clear();
		}

		ReloadInvalidationContext context = new() {
			OwnerID = Generation.OwnerID,
			OldGeneration = Generation,
			Reason = reason
		};

		// invalidate first, then dispose
		foreach (object item in items) {
			ct.ThrowIfCancellationRequested();
			switch (item) {
			case IReloadInvalidatable inv:
				inv.Invalidate(context);
				break;
			}
		}

		foreach (object item in items) {
			ct.ThrowIfCancellationRequested();
			switch (item) {
			case IAsyncDisposable ad:
				await ad.DisposeAsync();
				break;
			case IDisposable d:
				d.Dispose();
				break;
			}
		}
	}

	public ValueTask DisposeAsync() => InvalidateAsync(ReloadInvalidationReason.Shutdown, CancellationToken.None);
}
