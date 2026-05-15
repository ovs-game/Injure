// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Reflection;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Loader;

public static class EntrypointDiscovery {
	public static Type FindEntrypointType(Assembly assembly, Type gameApiType, string sourceName) {
		Type expected = typeof(IModEntrypoint<>).MakeGenericType(gameApiType);
		Type[] types;
		try {
			types = assembly.GetTypes();
		} catch (ReflectionTypeLoadException ex) {
			// don't swallow ReflectionTypeLoadException, even if it happens to work right now
			// we might be accepting an only partially loaded mod
			throw new ModLoadException($"{sourceName}: failed to load all types from entry assembly: " + string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message)));
		}
		Type[] withAttr = types.Where(type =>
			type is { IsClass: true, IsAbstract: false } &&
			type.GetConstructor(Type.EmptyTypes) is not null &&
			expected.IsAssignableFrom(type) &&
			type.GetCustomAttribute<ModEntrypointAttribute>() is not null
		).ToArray();
		return withAttr.Length switch {
			1 => withAttr[0],
			0 => throw new ModLoadException($"{sourceName}: no types with [ModEntrypoint] found"),
			_ => throw new ModLoadException($"{sourceName}: multiple types with [ModEntrypoint] found"),
		};
	}
}
