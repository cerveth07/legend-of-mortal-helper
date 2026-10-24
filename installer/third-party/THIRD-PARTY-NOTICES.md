# Bundled loader and third-party notices

The offline installer contains an unmodified BepInEx Unity Mono Windows x64 loader,
version `6.0.0-be.692+851521c`, and its upstream dependencies. These components
are separate from MortalHelper. They retain their original licenses and are
provided without warranty. No game assemblies, game images or user saves are
included in this loader bundle.

## Components

| Component | Version / revision | License file | Upstream |
| --- | --- | --- | --- |
| BepInEx | 851521cc126e4f9d841d2d9bfe857558f0395939 (BE 692) | BepInEx-LGPL-2.1.txt (LGPL 2.1 or later) | https://github.com/BepInEx/BepInEx |
| UnityDoorstop (`winhttp.dll`) | v4.3.0 | UnityDoorstop-LGPL-2.1.txt | https://github.com/NeighTools/UnityDoorstop |
| HarmonyX (`0Harmony.dll`) | 2.10.1 | HarmonyX-MIT.txt | https://github.com/BepInEx/HarmonyX |
| MonoMod.Utils and MonoMod.RuntimeDetour | 22.5.1.1 | MonoMod-MIT.txt | https://github.com/MonoMod/MonoMod |
| Mono.Cecil, Rocks, Mdb, Pdb | 0.10.4 | Mono.Cecil-MIT.txt; Microsoft.Cci.Pdb-MS-PL.txt | https://github.com/jbevain/cecil |
| SemanticVersioning | 2.0.2 | SemanticVersioning-MIT.txt | https://github.com/adamreeve/semver.net |
| AssetRipper.Primitives | 3.1.1 | AssetRipper.Primitives-MIT.txt; uTinyRipper-MIT.txt | https://github.com/AssetRipper/AssetRipper.Primitives |

Copyright and permission notices are reproduced verbatim in the listed files.
The included NuGet `.nuspec` files preserve upstream package version, author,
license and dependency metadata. Mono.Cecil.Pdb includes Microsoft.Cci.Pdb code
under the Microsoft Public License. AssetRipper.Primitives includes material
credited to uTinyRipper; its upstream notice is included separately.

## Corresponding source and replacement

The following unmodified upstream source archives are supplied **inside this
package**, alongside their license texts, rather than relying on a future written
offer or on the continued availability of external links:

- `BepInEx-851521cc126e4f9d841d2d9bfe857558f0395939-source.zip`
- `UnityDoorstop-v4.3.0-source.zip`

These contain the source and upstream build scripts for the LGPL components.
See their own README/build instructions. Compiler and external build tools are
not included. `manifest.json` records original download URLs, archive hashes,
and the installed loader file hashes.

MortalHelper loads BepInEx and Harmony as separate runtime DLLs; these libraries
are not merged into the plugin or installer executable. Users may inspect,
modify, rebuild and replace these dependencies subject to their licenses. No
restriction on reverse engineering for debugging such modifications is imposed
by this installer. Keep the source archives and notices with any redistribution
of this loader bundle. The installer does not alter the upstream loader binaries.

## Verification of this bundle

The loader archive was downloaded from the official BepInEx build index and its
SHA-256 matches the locally validated loader:

`9a3472f5eefb35a84ae8c6dea16814b728aff807c67c14fbfd448e20112951a6`

All shipped managed DLLs other than the BepInEx assemblies match the `net35`
DLLs in the corresponding official NuGet packages byte-for-byte. The loader
source commit and Doorstop version match the pinned BepInEx build changelog and
README. This verification does not imply upstream endorsement of MortalHelper.
