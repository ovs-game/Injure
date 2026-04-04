// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Reflection;

namespace Injure.Tests;

public static class Paths {
	public static string ProjectRoot { get; } = get("ProjectRoot");
	public static string RepoRoot { get; } = get("RepoRoot");

	private static string get(string key) =>
		Assembly.GetExecutingAssembly()
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.Single(a => a.Key == key)
			.Value
		?? throw new InvalidOperationException($"missing assembly metadata '{key}'");
}
