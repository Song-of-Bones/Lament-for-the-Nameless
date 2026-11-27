# MoonSharp dependency

- Upstream: https://github.com/moonsharp-devs/moonsharp
- Version: official release `v2.0.0.0`
- Archive: https://github.com/moonsharp-devs/moonsharp/releases/download/v2.0.0.0/moonsharp_release_2.0.0.0.zip
- Imported member: `interpreter/net35/MoonSharp.Interpreter.dll` (managed CLR assembly; no native Lua plugin)
- DLL SHA-256: `97f3207a579d19235fc68f742a9e5f2431c5035f309b7b3a4371b48886442a87`
- License: upstream BSD-style terms copied verbatim to `LICENSE.txt`.
- No remote debugger, REPL or editor integration from the archive is imported.

The host deliberately does not register CLR userdata or use MoonSharp's default full module set. Only the explicit visual-effect callbacks are exposed. Editor compilation and runtime tests are verified; IL2CPP/mobile/player build compatibility has not been verified because project builds are currently prohibited by the user.
