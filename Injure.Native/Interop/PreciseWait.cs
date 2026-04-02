// SPDX-License-Identifier: MIT

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure.Native;

public static partial class PreciseWait {
	[LibraryImport("injurenative")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial int precisewait_init();

	[LibraryImport("injurenative")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial void precisewait_deinit();

	[LibraryImport("injurenative")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial int precisewait(long ns);

	private static bool inited = false;

	private static void wrap(string fn, int rv) {
		if (rv != 0) {
			if (OperatingSystem.IsWindows())
				throw new Win32Exception(rv, fn);
			if (OperatingSystem.IsMacOS())
				throw new InvalidOperationException($"{fn}: kern_return_t {rv}");
			if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
				throw new InvalidOperationException($"{fn}: errno {rv}");
			throw new InvalidOperationException($"{fn}: {rv}");
		}
	}

	public static void Init() {
		if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD()) {
			wrap("precisewait_init", precisewait_init());
			inited = true;
		} else {
			throw new PlatformNotSupportedException("supported: Windows, MacOS, Linux, FreeBSD");
		}
	}

	public static void Deinit() {
		if (inited) {
			precisewait_deinit();
			inited = false;
		}
	}

	public static void Wait(long ns) {
		if (!inited)
			throw new InvalidOperationException("PreciseWait not initialized");
		ArgumentOutOfRangeException.ThrowIfNegative(ns);
		wrap("precisewait", precisewait(ns));
	}
}
