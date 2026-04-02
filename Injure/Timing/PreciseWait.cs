// SPDX-License-Identifier: MIT

namespace Injure.Timing;

public static partial class PreciseWait {
	public static void Init() => global::Injure.Native.PreciseWait.Init();
	public static void Deinit() => global::Injure.Native.PreciseWait.Deinit();
	public static void Wait(long ns) => global::Injure.Native.PreciseWait.Wait(ns);
}
