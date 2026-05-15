// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Injure;
using Injure.ModKit.Abstractions;
using Injure.ModKit.Abstractions.MonoMod;
using Injure.ModKit.Loader;
using Injure.ModKit.MonoMod;
using MonoMod.Cil;
using TestGame.ModApi;

[assembly: HotReloadLevel(AssemblyHotReloadLevel.SafeBoundary)]

namespace TestMod;

[ModEntrypoint]
public sealed class Entrypoint : IModEntrypoint<ITestGameModApi> {
	public ValueTask LoadAsync(IModLoadContext<ITestGameModApi> ctx, CancellationToken ct) {
		ctx.Api.MarkLoaded(ctx.OwnerID);
		return ValueTask.CompletedTask;
	}

	public ValueTask LinkAsync(IModLinkContext<ITestGameModApi> ctx, CancellationToken ct) => ValueTask.CompletedTask;

	public ValueTask ActivateAsync(CancellationToken ct) => ValueTask.CompletedTask;

	public ValueTask DeactivateAsync(CancellationToken ct) => ValueTask.CompletedTask;

	public ValueTask UnloadAsync(CancellationToken ct) => ValueTask.CompletedTask;

	[LoadILHook(TestGame.Hooks.GameplayLayer.GetSomeColor)]
	public static void IL_GameplayLayer_GetSomeColor(ILContext il) {
		ILCursor c = new(il);
		//c.RequireGotoNext("ldsfld Injure.Color32::Magenta", static i => i.MatchLdsfld<Color32>(nameof(Color32.Magenta)));
		c.RequireGotoNext("ldc.i4 42", static i => i.MatchLdcI4(42));
		FieldInfo fi = typeof(Color32).GetField(nameof(Color32.Green), BindingFlags.Static | BindingFlags.Public) ??
			throw new MissingFieldException("Color32.Green unexpectedly missing");
		c.Remove(); // TODO: avoid destructive IL edits, they can mess up IL hooks from other mods
		c.EmitLdsfld(fi);
	}
}
