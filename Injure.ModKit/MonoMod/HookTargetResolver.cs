// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Reflection;
using Injure.ModKit.Abstractions.MonoMod;

namespace Injure.ModKit.MonoMod;

internal sealed class HookTargetResolver {
	private readonly Dictionary<string, HookTarget> targets = new(StringComparer.Ordinal);

	public HookTargetResolver() {}
	public HookTargetResolver(IEnumerable<Assembly> assemblies) {
		foreach (Assembly assembly in assemblies)
			AddStoreAssembly(assembly);
	}

	public void AddStoreAssembly(Assembly assembly) {
		foreach (ModHookTargetStoreAttribute attr in assembly.GetCustomAttributes<ModHookTargetStoreAttribute>())
			addStore(attr.StoreType);
	}

	public bool TryResolve(string id, out HookTarget target) {
		return targets.TryGetValue(id, out target);
	}

	public HookTarget Resolve(string id) {
		if (targets.TryGetValue(id, out HookTarget target))
			return target;
		throw new MissingMethodException($"could not resolve hook target '{id}'");
	}

	private void addStore(Type storeType) {
		MethodInfo enumerate = storeType.GetMethod(
			"Enumerate",
			BindingFlags.Public | BindingFlags.Static,
			binder: null,
			types: Type.EmptyTypes,
			modifiers: null
		) ?? throw new InvalidOperationException($"was expecting hook target store '{storeType.FullName}' to expose public static IEnumerable<HookTarget> Enumerate()");

		if (!typeof(IEnumerable<HookTarget>).IsAssignableFrom(enumerate.ReturnType))
			throw new InvalidOperationException($"was expecting hook target store '{storeType.FullName}'.Enumerate() to return IEnumerable<HookTarget>");

		IEnumerable<HookTarget> values = (IEnumerable<HookTarget>)enumerate.Invoke(null, null)!;
		foreach (HookTarget target in values) {
			if (string.IsNullOrWhiteSpace(target.ID))
				throw new InvalidOperationException($"hook target store '{storeType.FullName}' unexpectedly returned a target with a null/empty/whitespace id");
			if (!targets.TryAdd(target.ID, target))
				throw new InvalidOperationException($"hook target store '{storeType.FullName}' unexpectedly returned a duplicate hook target id '{target.ID}'");
		}
	}
}

