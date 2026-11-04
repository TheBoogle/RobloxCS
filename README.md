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

Instances use exported structural Luau type aliases. Generic type arguments are erased at runtime; constructors carry hidden default values for type parameters.

Inheritance, constructor/method overloads, generic methods, static state on generic classes, `default(T)` in static generic members, and generic entry-point classes remain unsupported. Operations on constrained type parameters remain subject to the existing API/lowering limitations.

The regression suite executes emitted code with `luau` and checks it with `luau-analyze`; both executables must be on PATH.
