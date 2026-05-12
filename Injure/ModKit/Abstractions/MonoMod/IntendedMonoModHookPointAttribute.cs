// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions.MonoMod;

// use open enums since 1) these are just informational and 2) you can't have the closed enum
// types in an attribute

/// <summary>
/// Set of MonoMod hook types that an intended-hook-point method was designed with in mind.
/// </summary>
[Flags]
public enum MonoModHookKinds {
	/// <summary>
	/// Normal hooks (also sometimes known as <c>On.</c> hooks) are explicitly supported.
	/// </summary>
	Hook   = 1 << 0,

	/// <summary>
	/// IL hooks are explicitly supported.
	/// </summary>
	/// <remarks>
	/// This does <b>not</b> imply the method's IL contents are stable across builds in any way.
	/// </remarks>
	ILHook = 1 << 1,
}

/// <summary>
/// Least disruptive reload boundary at which hooks for an intended-hook-point method were intended
/// to be correctly installable/removable/replaceable.
/// </summary>
public enum HookPointReloadBoundary {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified,

	/// <summary>
	/// Installing/removing/replacing a hook for this method may leave behind side
	/// effects that aren't fully reversible without restarting the process.
	/// </summary>
	ProcessLifetime,

	/// <summary>
	/// Hooks may be installed/removed/replaced at a game-defined safe boundary.
	/// </summary>
	SafeBoundary,

	/// <summary>
	/// Hooks may be installed/removed/replaced at a game-defined small live reload
	/// boundary, such as between ticks/frames while the relevant subsystem is quiesced.
	/// </summary>
	LiveBoundary,
}

/// <summary>
/// What thread(s) an intended-hook-point method runs on; hooks must be prepared to run on those threads.
/// </summary>
public enum HookPointThreadAffinity {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified,

	/// <summary>
	/// Runs on a game-defined main thread.
	/// </summary>
	MainThread,

	/// <summary>
	/// Runs on a game-defined render thread.
	/// </summary>
	RenderThread,

	/// <summary>
	/// Runs on a game-defined audio thread.
	/// </summary>
	AudioThread,

	/// <summary>
	/// Runs on one or multiple game-defined worker threads.
	/// </summary>
	WorkerThreads,

	/// <summary>
	/// May run on any arbitrary thread.
	/// </summary>
	AnyThread,

	/// <summary>
	/// Runs on some other thread or multiple other threads not listed in this enum.
	/// See the notes for details.
	/// </summary>
	Other,
}

/// <summary>
/// What kinds of blocking behavior a well-behaved hook for an intended-hook-point method can exhibit.
/// </summary>
public enum HookPointBlockingPolicy {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified,

	/// <summary>
	/// Blocking in a hook for this method is not specifically prohibited beyond standard expectations.
	/// </summary>
	MayBlock,

	/// <summary>
	/// Avoid blocking if possible; blocking may cause apparent stutters or otherwise degrade the
	/// relevant subsystem's functionality.
	/// </summary>
	ShouldNotBlock,

	/// <summary>
	/// Strictly must not block; blocking can snowball into a full failure of the relevant subsystem.
	/// </summary>
	MustNotBlock,
}

/// <summary>
/// Concurrency hazards of this method that hooks must be prepared to deal with.
/// </summary>
[Flags]
public enum HookPointConcurrencyHazards {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified                   = 0,

	/// <summary>
	/// None of the specific concurrency hazards listed in this enum apply.
	/// </summary>
	NoKnownHazards                = 1 << 0,

	/// <summary>
	/// The method may run while iterating over some global collection/list; hooks must be
	/// careful modifying said collection.
	/// </summary>
	IteratorInvalidationRisk      = 1 << 1,

	/// <summary>
	/// The method may run under a held lock/mutex.
	/// </summary>
	RunsUnderLock                 = 1 << 2,

	/// <summary>
	/// The method may recursively call itself or re-enter the same logical operation on
	/// the same thread.
	/// </summary>
	RecursiveOnSameThread         = 1 << 3,

	/// <summary>
	/// The method may be called reentrantly at any point before a previous invocation has completed.
	/// </summary>
	/// <remarks>
	/// Distinct from <see cref="RecursiveOnSameThread"/> since the cause of the reentrant call may not
	/// be known / predictable and can't be easily surrounded by "prepare state" / "release locks" / etc.
	/// code the same way a simple recursive call can be.
	/// </remarks>
	Reentrant                     = 1 << 4,

	/// <summary>
	/// Multiple invocations of this method may run concurrently on different threads.
	/// </summary>
	Concurrent                    = 1 << 5,

	/// <summary>
	/// The method must not call back into its originating subsystem; doing so may deadlock, recurse,
	/// corrupt state, violate phase rules, double-free, etc.
	/// </summary>
	NoCallbackIntoOriginSubsystem = 1 << 6,

	/// <summary>
	/// The method is called without any synchronization guarantees. Any hooks must assume
	/// state not known to be synchronized right now may be unstable / racing and typical
	/// guard locks/mutexes may not be held.
	/// </summary>
	Unsynchronized                = 1 << 7,

	/// <summary>
	/// The method runs in a highly restricted context; see the notes for details. Typically,
	/// this means something like: no allocation, no locks, no blocking, no logging, no throwing,
	/// no subsystem calls not known to be safe. May be as extreme as async-signal-safe contexts.
	/// </summary>
	RestrictedExecutionContext    = 1 << 8,
}

/// <summary>
/// Responsibilities of an intended-hook-point method. Hooks must be prepared to correctly
/// handle them / suppress them / etc.
/// </summary>
[Flags]
public enum HookPointEffects {
	/// <summary>
	/// Unknown or unspecified; don't assume anything, inspect the method's code and notes.
	/// </summary>
	Unspecified           = 0,

	/// <summary>
	/// None of the specific responsibilities listed in this enum apply.
	/// </summary>
	NoKnownEffects        = 1 << 0,

	/// <summary>
	/// The method is pure or a simple query method; hooks should be careful introducing
	/// non-trivial extra work or side effects.
	/// </summary>
	PureOrQuery           = 1 << 1,

	/// <summary>
	/// The method may mutate object-local state in a way that other code depends on.
	/// </summary>
	LocalState            = 1 << 2,

	/// <summary>
	/// The method may depend on game-defined global gameplay/world/entity state or mutate it
	/// in a way that other code depends on.
	/// </summary>
	WorldState            = 1 << 3,

	/// <summary>
	/// The method may perform filesystem or external I/O.
	/// </summary>
	IO                    = 1 << 4,

	/// <summary>
	/// The method may perform creation/disposal of held resources, generally
	/// activate/deactivate/manage long-lived objects, or do other lifetime-sensitive work.
	/// Carelessly suppressing or removing logic may leave resources dangling or uninitialized.
	/// </summary>
	Lifetime              = 1 << 5,

	/// <summary>
	/// The method may touch render state / GPU resources and/or perform and submit draw calls.
	/// </summary>
	Rendering             = 1 << 6,

	/// <summary>
	/// The method may touch audio state / resources or audio callback logic.
	/// </summary>
	Audio                 = 1 << 7,

	/// <summary>
	/// The method may enqueue/retime/cancel timers, tickers, coroutines, etc. that are managed through
	/// engine APIs or otherwise touch state related to them.
	/// </summary>
	EngineScheduling      = 1 << 8,

	/// <summary>
	/// The method may enqueue/retime/cancel jobs, timers, tasks, threads, etc. that are not managed
	/// by the engine (or even known by the engine) or otherwise touch state related to them.
	/// </summary>
	ExternalScheduling    = 1 << 9,

	/// <summary>
	/// The method may touch network/session/replication state or perform network I/O.
	/// </summary>
	Networking            = 1 << 10,

	/// <summary>
	/// The method may touch save/load logic or durable long-term state.
	/// </summary>
	Persistence           = 1 << 11,

	/// <summary>
	/// The method may consume/reseed RNG or otherwise affect deterministic simulation.
	/// </summary>
	Randomness            = 1 << 12,

	/// <summary>
	/// The method may invoke user/mod/game callbacks, event handlers, delegates, virtual methods,
	/// scripts, or other arbitrary externally provided code.
	/// Hooks must treat said code as a black-box that may do anything not contractually prohibited, such as
	/// reenter subsystems, throw, mutate global state, block, spawn threads/children, etc.
	/// </summary>
	CallsUserCode         = 1 << 13,

	/// <summary>
	/// Throwing inside or through the method may leave state partially mutated, break caller
	/// expectations/invariants, skip cleanup, deadlock, etc. Hooks must be careful to catch
	/// exceptions from other methods and not throw any themselves.
	/// </summary>
	/// <remarks>
	/// Does <i>not</i> include unwind-across-FFI-boundary risk, since that's "throwing at any point
	/// while the method's still on the stack", which would make the base definition way too broad to be
	/// useful (what can you even call at that point? most methods can throw); for that case, see
	/// <see cref="FfiOrExternalState"/>.
	/// </remarks>
	ExceptionSensitive    = 1 << 14,

	/// <summary>
	/// The method may interface with other native/external code or touch potentially process-global external state.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This includes methods that do a native/unmanaged call that calls back into managed, since
	/// an exception unwinding across an FFI boundary is dangerous and may abort the process.
	/// </para>
	/// <para>
	/// This does <i>not</i> cover the method being able to generally cause UB. As such, lower-level
	/// unsafe interop code may typically want to couple this with <see cref="UndefinedBehaviorRisk"/>.
	/// </para>
	/// </remarks>
	FfiOrExternalState    = 1 << 15,

	/// <summary>
	/// The method may cause C-style undefined behavior if misused or internally broken by a hook.
	/// </summary>
	/// <remarks>
	/// Doesn't necessarily imply native interop; may be as simple as an unsafe method with some
	/// "clever" pointer arithmetic that can overrun if a hook introduces an off-by-one.
	/// </remarks>
	UndefinedBehaviorRisk = 1 << 16,
}

/// <summary>
/// Marks a suggested MonoMod hook point in this binary/DLL. This attribute is currently purely for
/// information and easier discovery of relevant locations in source code / decompiler output and
/// serves no functional runtime purpose.
/// </summary>
/// <remarks>
/// This does <b>not</b> imply in any way that the marked method is a stable API; it may be removed,
/// have its signature/name changed, etc. at any point without notice. This is merely an informational
/// marker for a plausible hook point in a specific build of a game, intended to be discoverable in e.g
/// decompilations. If you are the game developer and want to expose a stable hook-point API for mods,
/// roll something of your own that doesn't depend on MonoMod patching.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = true, Inherited = false)]
public sealed class IntendedMonoModHookPointAttribute : Attribute {
	public required string Hint { get; init; }

	public MonoModHookKinds SupportedKinds { get; init; } = MonoModHookKinds.Hook | MonoModHookKinds.ILHook;
	public HookPointReloadBoundary ReloadBoundary { get; init; } = HookPointReloadBoundary.Unspecified;
	public HookPointThreadAffinity ThreadAffinity { get; init; } = HookPointThreadAffinity.Unspecified;
	public HookPointBlockingPolicy Blocking { get; init; } = HookPointBlockingPolicy.Unspecified;
	public HookPointConcurrencyHazards ConcurrencyHazards { get; init; } = HookPointConcurrencyHazards.Unspecified;
	public HookPointEffects Effects { get; init; } = HookPointEffects.Unspecified;

	public string? Purpose { get; init; }
	public string? AlternativeApi { get; init; }
	public string? Notes { get; init; }
}
