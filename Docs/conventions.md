# conventions.md

This document is largely a stub/placeholder, I made it because there's two things I needed to document somewhere but didn't know where.

## `DangerousGet*` methods

Some objects expose public methods starting with `DangerousGet`. These methods expose an underlying resource, pointer, or handle while bypassing some wrapper-level ownership/lifetime/revocation contract.

They do not transfer ownership or extend lifetime in any way, and it is up to the caller to make sure they don't use the value past the real lifetime end or release it early. They are intended for advanced unsafe/low-level use.

Unless otherwise documented, these methods still validate the current wrapper state before returning.

## `Coroutine*` vs `Coro*` naming

Use `Coroutine*` if the "coroutine" part is central to the type, for example, `CoroutineHandle`, `CoroutineInfo`, `CoroutineScheduler`, etc. So, for example, "handle" or "info" would be pretty meaningless on its own.

Use `Coro*` if the "coroutine" part is more just like "related to coroutines", for example, `CoroCancellationReason`, `CoroUpdatePhase`, etc. These are just "cancellation reason" / "update phase", they're not primary concepts, but they're still part of the coroutine API so they have a `Coro` prefix.
