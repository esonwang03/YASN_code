# yasn-notify — native notification bridge

A thin Rust `cdylib` that fronts the [`user-notify`](https://crates.io/crates/user-notify)
crate and exposes a minimal, **synchronous** FFI surface to YASN's .NET app through
[UniFFI](https://mozilla.github.io/uniffi-rs/) (C# bindings via NordSecurity's
`uniffi-bindgen-cs`).

## Why this exists

YASN previously used the managed `OsNotifications` NuGet package, whose transitive
`Tmds.DBus` marshals via reflection and cannot be fully annotated for NativeAOT
(residual IL2104/IL3053 warnings). Moving the OS-notification work into a separate
native library called over a hand-generated P/Invoke surface removes that managed
reflection entirely — the binding is AOT/trim-clean and the platform code lives in
Rust, outside the IL trimmer's scope.

## FFI surface

Two synchronous functions (the .NET callers are fire-and-forget and use only
title + body today):

- `init(app_id) -> bool` — create and cache the platform notification manager. On
  macOS this also performs the first-run permission ask. Idempotent.
- `show_notification(title, body, tag) -> bool` — display a notification. `tag` maps
  to the platform "thread id" used to group notifications; pass `""` for none.

`user-notify`'s own API is async (Tokio); the async calls are driven to completion
inside this crate on a private runtime so the C# side stays a plain blocking P/Invoke.

## License

This crate links `user-notify`, which is **LGPL-3.0-or-later**. It is shipped as a
separate, replaceable dynamic library (`.dll`/`.dylib`) loaded over FFI — the
dynamic-linking case the LGPL explicitly permits. YASN's own sources remain MIT.
The LGPL license text travels with the distributed binaries; see `LICENSE.md`.

## Building

```sh
cargo build --release --target <triple>   # x86_64-pc-windows-msvc | x86_64-apple-darwin | aarch64-apple-darwin
```

The .NET publish drives this automatically via an MSBuild target (`BuildRustNotify`).
Generated C# bindings are committed under `src/YASN.App/Notifications/Generated/`;
CI regenerates them and fails on drift.
