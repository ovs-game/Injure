// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.MonoMod;

public readonly record struct HookOrder(
	string OrderDomain,
	string LocalID,
	int LocalPriority
);

public abstract class PatchDeclaration(string ownerID, HookOrder order) : IStrongRefDroppable {
	public string OwnerID { get; } = ownerID;
	public HookOrder Order { get; } = order;

	public abstract void Commit(OwnerScope ownerScope);
	public abstract void DropStrongReferences();
}

public sealed class HookDeclaration(string ownerID, HookOrder order, MethodBase target, MethodInfo replacement) : PatchDeclaration(ownerID, order) {
	private MethodBase? target = target;
	private MethodInfo? replacement = replacement;

	public override void Commit(OwnerScope ownerScope) {
		ArgumentNullException.ThrowIfNull(ownerScope);
		if (target is null || replacement is null)
			throw new InvalidOperationException("target/replacement method strong refs have already been dropped");
		Hook? h = null;
		try {
			h = new Hook(target, replacement);
			ownerScope.Add(new ClearableDisposable<Hook>(h));
			h = null;
		} finally {
			h?.Dispose();
		}
	}

	public override void DropStrongReferences() {
		target = null;
		replacement = null;
	}
}

public sealed class ILHookDeclaration(string ownerID, HookOrder order, MethodBase target, MethodInfo manipulator) : PatchDeclaration(ownerID, order) {
	private MethodBase? target = target;
	private MethodInfo? manipulator = manipulator;

	public override void Commit(OwnerScope ownerScope) {
		ArgumentNullException.ThrowIfNull(ownerScope);
		if (target is null || manipulator is null)
			throw new InvalidOperationException("target/manipulator method strong refs have already been dropped");
		ILContext.Manipulator? m;
		ILHook? h = null;
		try {
			m = (ILContext.Manipulator)Delegate.CreateDelegate(typeof(ILContext.Manipulator), manipulator);
			h = new ILHook(target, m);
			ownerScope.Add(new ClearableDisposable<ILHook>(h));
			h = null;
		} finally {
			h?.Dispose();
#pragma warning disable IDE0059 // unnecessary assignment to local
			m = null;
#pragma warning restore IDE0059 // unnecessary assignment to local
		}
	}

	public override void DropStrongReferences() {
		target = null;
		manipulator = null;
	}
}
