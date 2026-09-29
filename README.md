# UT4 Recon

UT4 Recon is an early Windows prototype for making narrowly controlled changes
to cooked Unreal Tournament 4 maps when the original editor source is no longer
available.

I built it primarily for community map maintenance: fixing collision holes,
moving misplaced geometry, adjusting player or item spawn positions, and
producing a clearly renamed replacement pak. It is not a general Unreal Engine
uncooker, and its editor workspace is not equivalent to the original source
map.

> **Early release:** version `0.1.0-alpha.1` has completed a focused human
> playtest, but it has only been exercised on a small number of maps and one UT4
> toolchain. Keep the original pak, review every validation report, give repaired
> maps a distinct name, and playtest the result before distributing it.

## The idea

Cooking removes or transforms much of the information that made a map pleasant
to author: Blueprint graphs, material graphs, comments, folders, source import
settings, original CSG history, and other editor metadata may no longer exist.
The runtime data needed to play the map still contains useful geometry,
transforms, collision, object references, compiled behavior, and asset payloads.

UT4 Recon treats that cooked runtime data as an immutable baseline:

```text
cooked map pak
    -> inventory and hash the original packages
    -> reconstruct a constrained editor workspace
    -> record only supported edits
    -> apply those edits to copies of the original cooked packages
    -> validate every changed and preserved export
    -> build a distinctly named replacement pak
```

This preservation model is the central design decision. The tool does not
recook a newly reconstructed approximation of the entire map. Unchanged cooked
bytes remain authoritative, including compiled Blueprint behavior and assets
that cannot be represented faithfully in the editor workspace.

## What the editor shows

The generated workspace combines several kinds of objects:

- **Exact editable objects** represent cooked actors or components with a
  validated write path. The panel lists the operations that can reach the pak.
- **Exact collision overlays** visualize cooked blocking volumes. Supported
  movement, duplication, and deletion are translated back to runtime collision.
- **Move/rotate markers** expose conservative transform edits for recognized
  player starts and pickups while preserving their original runtime behavior.
- **Reconstructed context** supplies floors, structural BSP, recovered mesh
  previews, and neutral lighting so the map can be understood. Context helpers
  are never packaged unless the panel explicitly identifies a supported
  operation.
- **Preserved-only objects** provide evidence about runtime content that the
  current writer cannot safely change.

The UT4 Recon panel is the authority for a selected object. Seeing an object in
the viewport does not by itself mean that every Unreal Editor operation on it
can be exported.

## Current capabilities

The prototype can:

- inspect unencrypted UT4 `WindowsNoEditor` paks and preserve a hashed baseline;
- generate a navigable editor workspace with recovered geometry and lighting;
- move, rotate, and scale supported static-mesh instances;
- move, duplicate, and delete compatible blocking-volume collision overlays;
- add a certified rectangular collision box without finding a donor in the map;
- move recognized player-start and pickup markers within their supported fields;
- delete or clone selected actors when a validated cooked closure exists;
- edit selected already-serialized cooked properties through the CLI;
- change the level-summary title and consistently rename the map package, URL,
  registry records, and pak identity;
- preserve compiled behavior and untouched exports from the original pak;
- validate the edit manifest, package structure, changed exports, pak integrity,
  and selected runtime-load evidence;
- extract supported assets into external workspaces and inject compatible donor
  changes through the advanced CLI workflows.

The focused playtest confirmed static-mesh movement, blocking-volume
duplication and deletion, player-start movement, distinct map identity, pak
generation, and loading the result in a community-patched UT4 client.

## Important limitations

UT4 Recon does not recover the original authoring project. In particular, it
does not recreate Blueprint or material graphs, comments, editor folders,
original brush history, source files, or arbitrary asset import settings.
Preview meshes can lack original materials and secondary authoring data.

The current release does not offer arbitrary render-mesh replacement,
navigation rebuilding, general actor creation, landscape or foliage editing,
skeletal-content reconstruction, encrypted-pak support, or a promise that an
unknown native package layout is safe. The custom collision donor workflow is
included for advanced testing but has not completed the same human runtime test
as the core transform and blocking-volume workflow.

Structural validation proves that the output follows the supported package
rules. It cannot prove gameplay correctness. A repaired map still needs a real
client playtest.

## Requirements and compatibility

The binary release requires:

- Windows x64;
- the Unreal Tournament 4 Editor;
- an unencrypted UT4 `WindowsNoEditor` map pak;
- enough free space for the extracted baseline and generated workspace.

The release was built and tested with:

- UT4 Editor `4.15.0-3525360+++UT+Release-Next`;
- compatible API changelist `3525109`;
- the Epic stock game build at changelist `3525360`;
- the UT4ever Installer 1.1.0 client;
- UT4UU 10.1.6 and NetcodePlus 2.0.

Community launchers, server integrations, and runtime plugins that retain the
stock UT4 4.15 package format are expected to work, although every combination
has not been individually certified. A genuinely different editor ABI, cooked
package layout, encrypted pak, or rebuilt engine fork should be treated as
unverified. The installer checks the editor's compatible changelist before
copying the native plugin.

## Installing the binary release

1. Download `ut4recon-0.1.0-alpha.1-win-x64.zip` from the GitHub release and
   extract it to an ordinary writable folder.
2. Close UT4 Editor.
3. Open PowerShell in the extracted folder and run:

   ```powershell
   .\Install-EditorPlugin.ps1 -EditorRoot "E:\path\to\UnrealTournamentEditor"
   ```

4. Create the recovery project and editor workspace:

   ```powershell
   .\cli\Ut4Recon.Cli.exe create-project "C:\Maps\Map.pak" `
     --output "C:\UT4Recon\Map" `
     --editor "E:\path\to\UnrealTournamentEditor"

   .\cli\Ut4Recon.Cli.exe create-editor-project "C:\UT4Recon\Map" `
     --output "C:\UT4Recon\Map\EditorWorkspace" `
     --editor "E:\path\to\UnrealTournamentEditor"

   .\cli\Ut4Recon.Cli.exe run-editor-import "C:\UT4Recon\Map\EditorWorkspace"
   .\cli\Ut4Recon.Cli.exe open-editor-workspace "C:\UT4Recon\Map\EditorWorkspace"
   ```

5. Open **Window > UT4 Recon**, follow the numbered workflow at the top of the
   panel, and read the concise capability description before editing an object.
6. Save the level, export the saved map from the panel, validate the changes,
   and build a distinctly named pak.
7. Install the pak in the client and playtest the intended change.

The release also contains `PROTOTYPE-README.md` with a compact checklist. The
[technical reference](docs/TECHNICAL_REFERENCE.md) documents CLI commands,
formats, validation rules, and advanced asset workflows.

## Building from source

The managed backend requires the .NET 9 SDK:

```powershell
dotnet restore .\Ut4Recon.sln
dotnet build .\Ut4Recon.sln -c Release
dotnet test .\Ut4Recon.sln -c Release
```

The native editor panel additionally requires the matching UT4/UE 4.15 source
and headers plus the installed UT4 Editor. Those Epic files are not part of this
repository and remain governed by Epic's agreements. Build the complete local
bundle with:

```powershell
.\release\Build-Prototype.ps1 `
  -EditorRoot "E:\path\to\UnrealTournamentEditor" `
  -SourceRoot "E:\path\to\UnrealTournament-source"
```

Some private regression tests rely on locally retained cooked research fixtures
that cannot be redistributed. The normal public test suite excludes them. A
maintainer who lawfully has those fixtures can run the extended suite with
`-p:IncludePrivateFixtureTests=true`.

## Source repository and releases

The Git repository contains the C# backend, native plugin source, schemas,
project-authored collision templates, public tests, documentation, and release
scripts. It intentionally excludes UT4/UE source, Epic assets, third-party maps,
user paks, generated recovery projects, build outputs, and local research
evidence.

Compiled Windows bundles belong on the GitHub **Releases** page rather than in
Git history. Each release should include the zip, its SHA-256 checksum, concise
release notes, and a link to the matching tag. See [RELEASING.md](RELEASING.md)
for the repository and authentication procedure.

## Issues and contributions

Bug reports, compatibility results, documentation improvements, and pull
requests are welcome. Please do not upload copyrighted map paks, extracted game
content, Unreal Tournament source, credentials, or generated workspaces to an
issue or pull request. A minimal report, logs, tool version, editor build, and a
description of the affected object are usually more useful.

I may not review every issue quickly, and I cannot promise that I will
personally implement requested features. Opening a clear issue is still useful:
it records the problem for other contributors and helps identify which maps and
community configurations need support. Read [CONTRIBUTING.md](CONTRIBUTING.md)
before submitting code.

## License and Epic disclaimer

My original code is open source under the [MIT License](LICENSE). Anyone may
use, modify, redistribute, or build on it under those terms. This license covers
only the code and other material the project contributors have the right to
license. It does not grant rights to Epic Games material, Unreal Tournament,
Unreal Engine, third-party maps, or other separately licensed content.

This repository does not distribute Unreal Tournament, Unreal Engine source,
the UT4 Editor, or third-party maps. Users must obtain and use those materials
under their applicable licenses.

Portions of the materials used are trademarks and/or copyrighted works of Epic
Games, Inc. All rights reserved by Epic. This material is not official and is
not endorsed by Epic.

Unreal, Unreal Engine, Unreal Tournament, and Epic Games are trademarks or
registered trademarks of Epic Games, Inc. This project is an independent fan
project and is not affiliated with Epic Games. See [NOTICE.md](NOTICE.md).
