// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Injure.Coroutines;

internal sealed class NamedEnumerator(IEnumerator enumerator,
		string debugName, string sourceFile, int sourceLine, string sourceMember) : IEnumerator, IDisposable {
	private readonly IEnumerator enumerator = enumerator;
	public string DebugName { get; } = debugName;
	public string SourceFile { get; } = sourceFile;
	public int SourceLine { get; } = sourceLine;
	public string SourceMember { get; } = sourceMember;

	public object Current => enumerator.Current;
	public bool MoveNext() => enumerator.MoveNext();
	public void Reset() => enumerator.Reset();

	public void Dispose() => (enumerator as IDisposable)?.Dispose();
}

internal static partial class CoroNameCleanup {
	[GeneratedRegex(@"^<(?<name>[^>]+)>d(?:__\d+)?$")]
	private static partial Regex IteratorNameRe();
	[GeneratedRegex(@"^<<(?<outer>[^>]+)>g__(?<inner>[^|>]+)\|[^>]*>d(?:__\d+)?$")]
	private static partial Regex LocalFunctionIteratorNameRe();

	public static string Clean(string s) {
		if (LocalFunctionIteratorNameRe().Match(s) is Match { Success: true } m)
			return m.Groups["outer"].Value + "." + m.Groups["inner"].Value;
		if (IteratorNameRe().Match(s) is Match { Success: true } m2)
			return m2.Groups["name"].Value;
		return s;
	}
}

public static class CoroNamingExtensions {
	public static IEnumerator Named(this IEnumerator enumerator, string debugName,
		[CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0, [CallerMemberName] string sourceMember = "") =>
		new NamedEnumerator(enumerator, debugName, sourceFile, sourceLine, sourceMember);
}
