// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace Injure.ModKit.Loader;

public sealed class ModAlc(string mainAssemblyPath, IEnumerable<string> sharedAssemblyNames, string name) : AssemblyLoadContext(name, isCollectible: true) {
	private readonly AssemblyDependencyResolver resolver = new(mainAssemblyPath);
	private readonly HashSet<string> sharedAssemblyNames = new(sharedAssemblyNames, StringComparer.OrdinalIgnoreCase);

	protected override Assembly? Load(AssemblyName assemblyName) {
		if (assemblyName.Name is not null && sharedAssemblyNames.Contains(assemblyName.Name))
			return null;
		string? path = resolver.ResolveAssemblyToPath(assemblyName);
		return path is null ? null : LoadFromAssemblyPath(path);
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
		string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
	}
}
