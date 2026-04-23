// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Injure.Coroutines;

public sealed class CoroutineScope {
	private readonly CoroutineScheduler scheduler;
	private readonly CoroutineScope? parent;
	private readonly HashSet<CoroutineHandle> members = new();
	private readonly List<CoroutineScope> children = new();
	private CoroCancellationReason? cancellationReason = null;

	public CoroutineScheduler Scheduler => scheduler;
	public CoroutineScope? Parent => parent;
	public string Name { get; }
	public bool Cancelled => cancellationReason is not null;

	private CoroutineScope(CoroutineScheduler scheduler, CoroutineScope? parent, string name) {
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(name);
		this.scheduler = scheduler;
		this.parent = parent;
		Name = name;
		if (parent is not null) {
			if (!ReferenceEquals(parent.scheduler, scheduler))
				throw new ArgumentException("parent scope belongs to a different scheduler", nameof(parent));
			parent.children.Add(this);
			if (parent.TryGetCancellationReason(out CoroCancellationReason reason))
				cancellationReason = reason;
		}
	}

	public static CoroutineScope CreateRoot(CoroutineScheduler scheduler, string name) => new(scheduler, null, name);
	public CoroutineScope CreateChild(string name) => !Cancelled ? new CoroutineScope(scheduler, this, name) :
		throw new InvalidOperationException("cannot create a child from a cancelled scope");

	internal void Cancel(CoroCancellationReason reason) {
		if (Cancelled)
			return;
		cancellationReason = reason;
		CoroutineScope[] childrenSnap = children.Count > 0 ? new CoroutineScope[children.Count] : Array.Empty<CoroutineScope>();
		if (childrenSnap.Length > 0)
			children.CopyTo(childrenSnap);
		CoroutineHandle[] membersSnap = new CoroutineHandle[members.Count];
		members.CopyTo(membersSnap);
		for (int i = 0; i < childrenSnap.Length; i++)
			childrenSnap[i].Cancel(reason);
		for (int i = 0; i < membersSnap.Length; i++)
			scheduler.TryCancel(membersSnap[i], reason);
	}
	public void Cancel() => Cancel(CoroCancellationReason.ScopeCancelled);
	public bool TryGetCancellationReason(out CoroCancellationReason reason) {
		if (cancellationReason is CoroCancellationReason r) {
			reason = r;
			return true;
		}
		reason = default;
		return false;
	}

	internal bool TryRegister(CoroutineHandle handle) => !Cancelled ? members.Add(handle) : false;
	internal void Unregister(CoroutineHandle handle) => members.Remove(handle);
}
