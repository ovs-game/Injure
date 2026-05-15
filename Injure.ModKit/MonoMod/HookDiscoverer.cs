// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Reflection;

using Injure.ModKit.Abstractions.MonoMod;
using Injure.ModKit.Runtime;

namespace Injure.ModKit.MonoMod;

internal static class HookDiscoverer<TGameApi> {
	public static void DiscoverLoadHooks(LoadedCodeMod<TGameApi> mod, HookTargetResolver resolver) {
		foreach (Type type in getTypesStrict(mod.Assembly, mod.Staged.Manifest.OwnerID)) {
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)) {
				discoverLoadHookAttributes(mod, method, resolver);
				discoverLoadILHookAttributes(mod, method, resolver);
				discoverLoadMethodHookAttributes(mod, method);
				discoverLoadMethodILHookAttributes(mod, method);
			}
		}
	}

	private static void discoverLoadHookAttributes(LoadedCodeMod<TGameApi> mod, MethodInfo patchMethod, HookTargetResolver resolver) {
		int n = 0;
		foreach (LoadHookAttribute attr in patchMethod.GetCustomAttributes<LoadHookAttribute>()) {
			HookTarget target = resolver.Resolve(attr.TargetID);
			HookMethodValidator.ValidateGeneratedHookMethod(patchMethod, target);
			mod.LoadHooks.Add(new HookDeclaration(
				mod.Staged.Manifest.OwnerID,
				createOrder(mod.Staged.Manifest.OwnerID, attr.OrderDomain, attr.LocalPriority, patchMethod, n++, "load-hook"),
				target.Method,
				patchMethod
			));
		}
	}

	private static void discoverLoadILHookAttributes(LoadedCodeMod<TGameApi> mod, MethodInfo patchMethod, HookTargetResolver resolver) {
		int n = 0;
		foreach (LoadILHookAttribute attr in patchMethod.GetCustomAttributes<LoadILHookAttribute>()) {
			HookTarget target = resolver.Resolve(attr.TargetID);
			HookMethodValidator.ValidateGeneratedILHookMethod(patchMethod, target);
			mod.LoadHooks.Add(new ILHookDeclaration(
				mod.Staged.Manifest.OwnerID,
				createOrder(mod.Staged.Manifest.OwnerID, attr.OrderDomain, attr.LocalPriority, patchMethod, n++, "load-il-hook"),
				target.Method,
				patchMethod
			));
		}
	}

	private static void discoverLoadMethodHookAttributes(LoadedCodeMod<TGameApi> mod, MethodInfo patchMethod) {
		int n = 0;
		foreach (LoadMethodHookAttribute attr in patchMethod.GetCustomAttributes<LoadMethodHookAttribute>()) {
			MethodBase target = resolveMethod(attr.TargetType, attr.MethodName, attr.BindingFlags, attr.ParameterTypes);
			HookMethodValidator.ValidateDirectHookMethod(patchMethod, target);
			mod.LoadHooks.Add(new HookDeclaration(
				mod.Staged.Manifest.OwnerID,
				createOrder(mod.Staged.Manifest.OwnerID, attr.OrderDomain, attr.LocalPriority, patchMethod, n++, "load-method-hook"),
				target,
				patchMethod
			));
		}
	}

	private static void discoverLoadMethodILHookAttributes(LoadedCodeMod<TGameApi> mod, MethodInfo patchMethod) {
		int n = 0;
		foreach (LoadMethodILHookAttribute attr in patchMethod.GetCustomAttributes<LoadMethodILHookAttribute>()) {
			MethodBase target = resolveMethod(attr.TargetType, attr.MethodName, attr.BindingFlags, attr.ParameterTypes);
			HookMethodValidator.ValidateDirectILHookMethod(patchMethod, target);
			mod.LoadHooks.Add(new ILHookDeclaration(
				mod.Staged.Manifest.OwnerID,
				createOrder(mod.Staged.Manifest.OwnerID, attr.OrderDomain, attr.LocalPriority, patchMethod, n++, "load-method-il-hook"),
				target,
				patchMethod
			));
		}
	}

	private static MethodInfo resolveMethod(Type type, string name, BindingFlags flags, Type[]? parameterTypes) {
		if (parameterTypes is not null) {
			MethodInfo? method = type.GetMethod(name, flags, binder: null, types: parameterTypes, modifiers: null);
			return method ?? throw new MissingMethodException(type.FullName, name);
		}
		MethodInfo[] matches = type.GetMethods(flags).Where(m => m.Name == name).ToArray();
		if (matches.Length == 1)
			return matches[0];
		if (matches.Length == 0)
			throw new MissingMethodException(type.FullName, name);
		throw new AmbiguousMatchException($"method '{type.FullName}.{name}' is overloaded; specify ParameterTypes");
	}

	private static HookOrder createOrder(string ownerId, string? localDomain, int localPriority, MethodInfo patchMethod, int ordinal, string prefix) {
		string domain = string.IsNullOrWhiteSpace(localDomain) ? ownerId : ownerId + "::" + localDomain;
		string localId = prefix + ":" + patchMethod.DeclaringType?.FullName + "." + patchMethod.Name + "#" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
		return new HookOrder(domain, localId, localPriority);
	}

	private static Type[] getTypesStrict(Assembly assembly, string ownerId) {
		try {
			return assembly.GetTypes();
		} catch (ReflectionTypeLoadException ex) {
			string details = string.Join(
				Environment.NewLine,
				ex.LoaderExceptions.Where(e => e is not null).Select(e => "  - " + e!.Message)
			);
			throw new InvalidOperationException($"could not inspect all hook types for mod '{ownerId}':\n{details}", ex);
		}
	}

}
