// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace Injure.Native;

public static unsafe partial class Unibreak {
	// void set_linebreaks_utf16(const utf16_t *s, size_t len, const char *lang, char *brks);
	[LibraryImport("injurenative", StringMarshalling = StringMarshalling.Utf8)]
	private static partial void set_linebreaks_utf16(char *s, nuint len, string? lang, byte *brks);

	public static void SetLineBreaks(string text, Span<byte> breaks, string? lang = null) {
		if (breaks.Length < text.Length)
			throw new ArgumentException("breaks buffer too small", nameof(breaks));
		fixed (char *pText = text)
		fixed (byte *pBreaks = breaks) {
			set_linebreaks_utf16(pText, (nuint)text.Length, lang, pBreaks);
		}
	}
}
