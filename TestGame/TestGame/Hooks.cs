// SPDX-License-Identifier: MIT

// note: almost this entire file should be sourcegen'd, it's just Not right now

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reflection;
using Injure;
using Injure.ModKit.Abstractions.MonoMod;

[assembly: ModHookTargetStore(typeof(TestGame.__Injure_ModKit_HookTargetStore))]

namespace TestGame;

[ModHookRoot]
public static class Hooks {
	public static class GameplayLayer {
		public const string GetSomeColor = "jdoe.test-game:GameplayLayer:GetSomeColor";
		public delegate Color32 orig_GetSomeColor();
	}
}

[ModRawHookRoot]
public static class RawHooks {
	public static class GameplayLayer {
		public const string GetSomeColor = "raw::jdoe.test-game:GameplayLayer:GetSomeColor";
		public delegate Color32 orig_GetSomeColor();
	}
}

public static class __Injure_ModKit_HookTargetStore {
	private static readonly FrozenDictionary<string, HookTarget> map = new Dictionary<string, HookTarget>(StringComparer.Ordinal) {
		{
			Hooks.GameplayLayer.GetSomeColor,
			new HookTarget {
				ID = Hooks.GameplayLayer.GetSomeColor,
				Method = typeof(GameplayLayer).GetMethod(
					nameof(GameplayLayer.GetSomeColor),
					BindingFlags.Static | BindingFlags.Public
				) ?? throw new MissingMethodException(),
				OrigDelegateType = typeof(Hooks.GameplayLayer.orig_GetSomeColor),
			}
		},
		{
			RawHooks.GameplayLayer.GetSomeColor,
			new HookTarget {
				ID = RawHooks.GameplayLayer.GetSomeColor,
				Method = typeof(GameplayLayer).GetMethod(
					nameof(GameplayLayer.GetSomeColor),
					BindingFlags.Static | BindingFlags.Public
				) ?? throw new MissingMethodException(),
				OrigDelegateType = typeof(RawHooks.GameplayLayer.orig_GetSomeColor),
			}
		},
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static IEnumerable<HookTarget> Enumerate() => map.Values;
}
