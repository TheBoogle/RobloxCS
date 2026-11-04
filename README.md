# RobloxCS

[![Discord](https://img.shields.io/discord/1441762849666895964?label=discord&logo=discord&logoColor=white&color=5865F2)](https://discord.gg/Q5KgRENHwk)

> [!NOTE]
> Experimental software. Here be dragons.
>
> Found an issue? Submit an issue, or PR a fix.

Empowering ROBLOX developers to do better.

A C# to Luau compiler.

## Status

> [!NOTE]
> Huge rewrite PRs will not be accepted as I neither have the time nor the energy to review them,
> and the core is constantly changing.

Issues and feature suggestions are welcome.

## Classes

The existing HIR, lowering, AST, and rendering pipeline supports:

- Generic classes, constructed and nested type arguments.
- Generic instance fields, methods, and `default(T)` with C# primitive/reference defaults.
- Inheritance between transpiled classes, including abstract classes and virtual overrides.
- Explicit and implicit base constructors, including optional base-constructor arguments.
- Generic base classes and direct `base` method/computed-property calls.

Instances use metatable lookup chains and exported structural Luau type aliases. Generic type arguments are erased at runtime; constructors carry hidden default values for type parameters.

Constructor/method overloads, delegating constructors, generic methods, hidden instance members, and inheritance from native Roblox or external .NET classes remain unsupported. Static state on generic classes, `default(T)` in static generic members, and generic entry-point classes produce diagnostics rather than incorrect output. Operations on constrained type parameters remain subject to the existing API/lowering limitations.

The regression suite executes emitted code with `luau` and checks it with `luau-analyze`; both executables must be on PATH.
