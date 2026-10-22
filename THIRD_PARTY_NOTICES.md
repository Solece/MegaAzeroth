# Third-party notices — private installation backup

This notice accompanies the user's personal private MegaAzeroth installation backup. Game files and prepared converted artwork retain their original copyright and license terms. See [CREDITS.md](CREDITS.md) and [PRIVATE_BACKUP_SCOPE.md](PRIVATE_BACKUP_SCOPE.md). These notices do not relicense the original games or their resources.

## Verified loader package

The accepted loader files were matched against the unmodified maintained **BepInExPack_IL2CPP 6.0.755** archive. All 226 inventoried installed vendor files matched that archive byte for byte. The archive is 34,367,097 bytes; SHA256:

```text
a3ec977c7760c6045b742a9bf3e59c7b11baa80b023f402ebdbff07bad6b0d96
```

Official package: [Thunderstore 6.0.755 download](https://thunderstore.io/package/download/BepInEx/BepInExPack_IL2CPP/6.0.755/). The independently downloaded file resolved to the [official package CDN](https://ccdn.thunderstore.io/live/repository/packages/BepInEx-BepInExPack_IL2CPP-6.0.755.zip) and had the same SHA256. Runtime files are the contents under `BepInExPack/` in that ZIP.

The vendor ZIP contains no license/notice files. The following license texts were therefore collected directly from pinned upstream sources, without modification. `Licenses/upstream-provenance.json` records each original URL, resolved URL, byte length and SHA256.

| Component and observed version | Collected license | Corresponding upstream source |
| --- | --- | --- |
| BepInEx `6.0.0-be.755+3fab71a1914132a1ce3a545caf3192da603f2258` | [LGPL 2.1](Licenses/BepInEx-LGPL-2.1.txt) | [Exact source commit](https://github.com/BepInEx/BepInEx/tree/3fab71a1914132a1ce3a545caf3192da603f2258) |
| UnityDoorstop `4.5.0` (`winhttp.dll`) | [LGPL 2.1](Licenses/UnityDoorstop-v4.5.0-LICENSE.txt) | [Version 4.5.0 source](https://github.com/NeighTools/UnityDoorstop/tree/v4.5.0) |
| Embedded .NET runtime `6.0.7`, runtime commit `0ec02c8c96e2eda06dc5b5edfdbdba0f36415082` | [MIT](Licenses/dotnet-runtime-v6.0.7-LICENSE.txt) and [third-party notices](Licenses/dotnet-runtime-v6.0.7-THIRD-PARTY-NOTICES.txt) | [.NET runtime v6.0.7](https://github.com/dotnet/runtime/tree/v6.0.7) |
| Il2CppInterop `1.5.1-ci.829+6d9007c18cc8440830379c5e1d5714085e7ec577` | [LGPL 3 and included GPL terms](Licenses/Il2CppInterop-6d9007c-LICENSE.txt) | [Exact source commit](https://github.com/BepInEx/Il2CppInterop/tree/6d9007c18cc8440830379c5e1d5714085e7ec577) |
| HarmonyX `2.10.2` | [MIT](Licenses/HarmonyX-v2.10.2-LICENSE.txt) | [Version 2.10.2 source](https://github.com/BepInEx/HarmonyX/tree/v2.10.2) |
| MonoMod RuntimeDetour/Utils `22.7.31.1` | [MIT](Licenses/MonoMod-v22.07.31.01-LICENSE.txt) | [Version 22.07.31.01 source](https://github.com/MonoMod/MonoMod/tree/v22.07.31.01) |
| Mono.Cecil / Mdb / Pdb / Rocks `0.11.4` | [MIT](Licenses/Mono-Cecil-0.11.4-LICENSE.txt) | [Version 0.11.4 source](https://github.com/jbevain/cecil/tree/0.11.4) |
| Cpp2IL / LibCpp2IL / WasmDisassembler / StableNameDotNet `2022.1.0-development.1452+558ddd98642010897d54316b51fbaa7889fda093` | [MIT](Licenses/Cpp2IL-558ddd9-LICENSE.txt) | [Exact source commit](https://github.com/SamboyCoding/Cpp2IL/tree/558ddd98642010897d54316b51fbaa7889fda093) |
| AsmResolver `6.0.0-beta.5+124e161a9bc0ebb83b4d97276ae3da3c9cfe2fc5` | [MIT](Licenses/AsmResolver-124e161-LICENSE.txt) | [Exact source commit](https://github.com/Washi1337/AsmResolver/tree/124e161a9bc0ebb83b4d97276ae3da3c9cfe2fc5) |
| Iced `1.21.0+601c40570d6c986c8e1a6a2e483dd2fdf5ade552` | [MIT](Licenses/Iced-v1.21.0-LICENSE.txt) | [Version 1.21.0 source](https://github.com/icedland/iced/tree/v1.21.0) |

UnityDoorstop 4.5.0 is licensed under LGPL 2.1, not MIT. The embedded .NET license is MIT, but its separate upstream third-party notices are also preserved. The LGPL components remain unmodified and separate from the authored mod DLLs. Their corresponding source links are provided above; this file is not a substitute written source offer and does not claim that a public redistribution compliance review has been completed.

## Other observed vendor components

The loader also contains these separately identified dependencies. Their project links are retained here; exact license-text collection for these individual builds is not claimed by this bounded audit:

| Observed component | Source project |
| --- | --- |
| MonoMod.Backports `1.1.2+a1b82852b` | [MonoMod](https://github.com/MonoMod/MonoMod) |
| Disarm `2022.1.0-master.99+8574010015c8c56bb7faad7e407c50b0abe9cc72` | [Disarm](https://github.com/SamboyCoding/Disarm) |
| AssetRipper.CIL `1.2.2+06216e69825ccd6536dc17e9ca81c4d539ce9321` | [AssetRipper.CIL](https://github.com/AssetRipper/AssetRipper.CIL) |
| AssetRipper.Primitives `3.2.0+d759b3db98bf2a1cb8c2181faf5d405610aac585` | [AssetRipper.Primitives](https://github.com/AssetRipper/AssetRipper.Primitives) |
| Gee.External.Capstone `2.3.2+b90e380c14e857865c94a355954236e678bd557c` | [Capstone .NET binding](https://github.com/9ee1/Capstone.NET), [Capstone engine](https://github.com/capstone-engine/capstone) |
| SemanticVersioning `2.0.2` | [SemanticVersioning](https://github.com/adamreeve/semver.net) |
| `dobby.dll` (native version unavailable in file metadata) | [Dobby](https://github.com/jmpews/Dobby) |

The archive's base game and engine files also retain the notices supplied with the installed game. Their game-specific third-party notices were not replaced by the loader notices in this directory.
