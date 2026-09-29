# UT4 Recon

UT4 Recon is an experimental recovery pipeline for Unreal Tournament 4 cooked maps. It inventories a pak, preserves a hashed cooked baseline, emits a versioned package inventory, applies narrowly allowlisted edits, and builds a new integrity-tested pak. Milestones 2 and 3 provide direct cooked-property editing and an isolated editable proxy map. Milestone 4 adds static-mesh/BodySetup reconstruction. Milestone 5 adds decoded BSP workspaces and constrained cooked-donor model-closure replacement, including a recovered brush cooked in a fresh package.

The cooked baseline is the authoritative map. The generated editor workspace is a controlled patch-authoring view, not recovered original source. The product plan classifies editor objects as Exact editable, Reconstructed, Behavioral proxy, or Context only; the plugin must show these limitations and export only supported operations. The current external reports use `Preserve only` for the same context-only preservation state until their display labels migrate. Independent asset extraction, donor validation, and injection use the same backend contracts so advanced users can modify supported assets outside the generated workspace.

Editor protocol v2 writes those classifications, per-object evidence, invented metadata, limitations, capabilities, and collision provenance into `proxy-scene.json`. Cooked BlockingVolume UModels are decoded into tagged, non-colliding brush overlays in the generated editor map. Moving a canonical overlay exports supported serialized transform changes, duplicating a compatible overlay exports a complete BlockingVolume closure clone, and deleting the canonical overlay removes the original cooked actor from the persistent level. The overlays themselves never enter the rebuilt pak. Safe visual mode is the default and includes static visual actors and exact collision overlays while excluding active gameplay/navigation actors. `--include-runtime-proxies` enables the previous full representation for investigation only; it is explicitly experimental and may crash the pinned editor.

Visual-only workspaces also decode the principal cooked level UModel into a locked, non-colliding **Reconstructed BSP context** brush so floors and structural BSP remain visible for navigation. Two tagged movable, shadowless directional lights provide stable Lit-mode key/fill illumination without relying on unavailable baked lighting. The BSP context and preview lights are visualization helpers: the editor exporter explicitly ignores their `UT4RECON_VISUALIZATION` tags and never writes them to an edit manifest or output pak.

Workspace creation also stages recovered LOD0 OBJ geometry for cooked static meshes using each asset's original name and package directory. The native panel can import only the source packages missing from the editor installation, then save them as neutral-material preview assets. Reimporting the generated proxy map resolves its original mesh references. Stock source packages are skipped, and cooked-baseline pak builds never include these previews. The recovered shape is useful context, but it does not recreate authored materials, additional source LODs, sockets, or original import metadata.

The supported inspection profile is WindowsNoEditor content cooked by UE4/UT4 4.15. The known editor build is `4.15.0-3525360+++UT+Release-Next`. The CLI rejects a different UnrealPak binary rather than applying the supported profile to an unverified toolchain. Encrypted paks are also rejected.

Direct property grafting also supports the plain-string `Title` field on `/Script/UnrealTournament.UTLevelSummary`. The rule is class-constrained and preserves the package name/import tables and every unrelated export.

A build can also assign a distinct map identity after applying a title edit:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- build .\RecoveredMap `
  --manifest .\title-and-repair-manifest.json `
  --rename-map CTF-Example-Fixed `
  --output .\CTF-Example-Fixed-WindowsNoEditor.pak
```

`--rename-map` rewrites the cooked World and BuiltData package names, their long package references, pak entry paths, AssetRegistry object/package records, AssetRegistry filename, and version filename. It requires exactly one `UUTLevelSummary.Title` operation so the display title and package identity cannot diverge accidentally. The numeric version-file contents remain unchanged because UT4 checks that value against the cooked engine compatibility version; the runtime rejects and unmounts a renamed pak if it is incremented arbitrarily.

## Build and test

```powershell
dotnet build .\Ut4Recon.sln
dotnet test .\Ut4Recon.sln
```

The golden tests parse the retained cooked Glass and Example_Map fixtures. They do not launch or require the editor.

## Inspect a pak

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- inspect .\Map.pak --output .\Map-inspection --editor E:\path\to\UnrealTournamentEditor
```

This writes three reports under `Map-inspection/.ut4recon/` and removes its temporary extraction directory:

- `input-manifest.json` records the pak, UnrealPak binary, mount point, physical entry order, compression metadata, and hashes.
- `reconstruction-ir.json` records package summaries, dependencies, custom versions, sidecars, stable object identities, raw export hashes, tagged-property counts, opaque native bytes, unavailable source-authoring fields, and support classifications.
- `support-report.json` summarizes the classifications and parser diagnostics.

## Create recovery state

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- create-project .\Map.pak --output .\RecoveredMap --editor E:\path\to\UnrealTournamentEditor
```

This produces the same reports plus `.ut4recon/project.json`, an immutable read-only cooked baseline, a donor area, `MeshWorkspaces`, and `BspWorkspaces`. Each supported custom `StaticMesh` receives a versioned mesh interchange workspace. Each supported map `UModel` receives decoded BSP IR, OBJ diagnostics, and a T3D recovered-brush import whose invented editor metadata explicitly records that the original CSG history is unavailable.

## Create and build a direct edit

Use an object ID from `reconstruction-ir.json`. `new-edit` reads the original serialized value from the baseline, so the manifest contains an exact precondition:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- new-edit .\RecoveredMap `
  --object 71865d7d6e8250d2d6592f8b503b8c46e48abe82ca389dba4976ae97f8154b84 `
  --property RelativeLocation `
  --after '{"x":0,"y":0,"z":40}'

dotnet run --project .\src\Ut4Recon.Cli -- build .\RecoveredMap --output .\Map-Repaired.pak
```

Use `--append` on later `new-edit` commands to add independent operations to the same manifest. Current rules cover relative location, rotation and scale; box, capsule and sphere dimensions; selected collision profile/enabled fields; per-channel collision responses such as `BodyInstance.CollisionResponses.Pawn`; serialized BodySetup flags; scalar floats represented by the allowlisted fields; and `OverrideMaterials` arrays. The property must already be serialized in the cooked export. Material overrides may select only object references already present in the package, and the slot count cannot change.

## Add a new collision box without a map donor

The bundled template creates a new runtime `BlockingVolume` even when the input
map contains no suitable collision actor to clone. It is pinned to UT4 Editor CL
3525360/runtime API CL 3525109 and is recertified from its package hash, exact
four-export closure, 100-unit cube geometry, collision payload, imports, and
supported transforms during every build.

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- add-collision-box .\RecoveredMap `
  --template .\fixtures\collision-box-template\ut4-4.15-windows-no-editor-v1\collision-box-template.json `
  --name UT4Recon_Blocker_01 `
  --location '{"x":120,"y":-30,"z":45}' `
  --rotation '{"pitch":0,"yaw":16384,"roll":0}' `
  --scale '{"x":2,"y":0.5,"z":3}'
```

The command writes an ordinary edit-manifest operation anchored to the map's
persistent Level. `build` transplants and remaps the certified
`BlockingVolume`/`BrushComponent`/`UModel`/`BodySetup` closure, approves those
four new exports and the Level actor-list change, and rejects every unexplained
change. The behavior gate permits only the two native package dependencies
declared by this template; compiled classes, defaults, delegates, functions, and
bytecode still have to match the cooked baseline. The box can be translated,
rotated, and scaled. Arbitrary brush topology is handled by the separate custom
collision donor workflow planned next.

Monolithic packages and packages with exactly one `.uexp` sidecar are writable. A name-valued allowlisted field may append its requested value to the name table, and validation rejects every other table change. `.ubulk`, `.uptnl`, multiple-sidecar packages, and edits that require adding imports, properties, slots, or object references remain preserve-only.

Before packaging, `build` verifies the input pak, IR, baseline files and sidecars, object IDs, original payload hashes, expected property values, support rules, and opaque native bytes. It rejects any changed export without a matching operation. The validation report records every changed export and the number of untouched exports whose raw payload hashes still match. UnrealPak then builds and integrity-tests the new archive. This is structural validation; runtime certification is a separate gate.

## Install a repaired stock-package override

UT4 treats a replacement for an existing stock package differently from a new custom map. The runtime pak must use the shared-content layout, include a root `<pak-base>-version.txt` containing `3525360\r\n`, and be installed as `<pak-base>-WindowsNoEditor_P.pak` under `UnrealTournament/Content/Paks`. An ordinary pak in `Saved/Paks/DownloadedPaks` mounts, but loses package lookup precedence to the stock pak when both contain the same virtual path.

The retained runtime artifact is [Example_Map-WindowsNoEditor_P.pak](artifacts/milestone7-runtime/Example_Map-WindowsNoEditor_P.pak), SHA-256 `f7f25bafba0915da6016e81ea3bda335a6cf739c1b256590ce874b731e98f7ab`. Its entries are:

```text
Content/RestrictedAssets/Maps/Example_Map.umap
Example_Map-version.txt
```

The UT4 server mounted this pak, loaded `Example_Map`, selected `UTDMGameMode`, completed map loading, and remained alive. A controlled negative run replaced the map entry at the same patch path with an invalid package tag; loading then failed with `contains unrecognizable data`. That establishes that the positive run used the repaired package instead of silently falling back to stock. A root `Example_Map-AssetRegistry.bin` is still absent, so UT warns during discovery; explicit package-path loading works without it.

The full architecture, safety rules, test matrix, and milestone gates are in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md). The experiments that establish the current feasibility boundaries remain under [research](research).

Safe workspaces also expose existing player starts as inert cylinder markers. Their visible shape has no runtime meaning; moving a marker updates the original cooked capsule location, and its rotation is editable only when that property survived cooking. Player-start deletion, cloning, scale, team assignment, and spawn behavior remain blocked.

The current pickup extension uses inert sphere markers for existing pickup actors with one unambiguous direct capsule. It captures only serialized location/rotation values and blocks item type, respawn, deletion, cloning, and runtime behavior changes. The marker/import path works on a 63-pickup DM-Chill source fixture, and the indexed/lazy diff path compares that 10,452-object workspace in 8.45 seconds instead of more than four minutes. A compact cooked `Health_Small_C` fixture completed the full proof: an unchanged editor round trip emitted zero operations, a 100-unit marker move emitted one capsule `RelativeLocation` operation, the build changed one export while verifying 12 untouched exports, and the versioned repaired pak loaded in the standalone server. Independent reinspection of the mounted pak reports `(X=100,Y=0,Z=200)`.

## Create and round-trip an editor proxy

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- create-editor-project .\RecoveredMap `
  --output .\RecoveredMap\EditorWorkspace --editor E:\path\to\UnrealTournamentEditor

dotnet run --project .\src\Ut4Recon.Cli -- run-editor-import .\RecoveredMap\EditorWorkspace
```

For maps with custom meshes missing from the editor install, open the workspace once, use **Map > Import missing recovered mesh previews**, close the editor, and run `run-editor-import` again before authoring. The importer uses fixed automated static-mesh defaults and never overwrites an existing source package. This two-stage setup is required because unresolved T3D object references are fixed when the proxy map is imported; creating the source asset later does not retroactively repair a component that imported as `NULL`.

This workflow is certified on CTF-Switchback-PRO2: the panel imported and saved all eight missing custom mesh packages, skipped fourteen packages already supplied by the editor, and a second run skipped all twenty-two. Reimport resolved 62 custom mesh references with no `NULL StaticMesh` exports, while an unchanged editor diff still contained zero operations. Missing material packages can still produce warnings and neutral preview surfaces; the build continues to preserve their original cooked packages.

The workspace uses a separate UT4 editor process and a versioned `proxy-scene.json`. Native and stock actors are recreated through T3D. Custom Blueprint actors become labeled native proxies with surviving component templates; their original cooked behavior remains in the immutable baseline. Reconstruction IDs are stored in actor and component tags and must resolve uniquely after save. The generated `README.md` explains the mapper workflow, while `workspace-support-report.json` lists every object's fidelity, supported operations, and limitations without requiring a native editor plugin.

Open the isolated project, edit and save the proxy map, close the editor, and refresh the interchange export before producing the patch manifest:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- open-editor-workspace .\RecoveredMap\EditorWorkspace
dotnet run --project .\src\Ut4Recon.Cli -- run-editor-export .\RecoveredMap\EditorWorkspace
dotnet run --project .\src\Ut4Recon.Cli -- export-editor-edits .\RecoveredMap .\RecoveredMap\EditorWorkspace
dotnet run --project .\src\Ut4Recon.Cli -- build .\RecoveredMap --rename-map Map-Repaired --output .\Map-Repaired.pak
```

`run-editor-export` writes `editor-map-check.json` and prints the error and warning counts. It completes with a warning status when Map Check has warnings so automation and users cannot silently treat a degraded viewport as clean. Missing custom source assets can appear as `NULL StaticMesh` warnings even though their original cooked exports remain preserved in the immutable baseline; review the report before authoring geometry-dependent fixes.

Editor protocol v1 exports relative transforms and the simple collision fields represented directly in the proxy. It converts a missing supported actor ID into a persistent-level actor deletion. An untagged `StaticMeshActor` can be added when its mesh and material-slot references exactly match an existing simple two-export actor closure; the writer clones that cooked closure, remaps its component references, assigns deterministic object GUIDs, and applies the editor transform. Other additions and component-only deletions are rejected. Duplicate reconstruction IDs always stop the workflow. BSP maps remain scheduled for the BSP milestone.

The same graph operations can be authored without opening the editor:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- delete-actor .\RecoveredMap --object <actor-id>
dotnet run --project .\src\Ut4Recon.Cli -- clone-actor .\RecoveredMap --template <actor-id> `
  --name AddedCube --location '{"x":100,"y":200,"z":300}'
```

## Decode a static mesh and prepare a collision donor

`inspect-mesh` reads UE4.15 cooked render buffers and BodySetup data directly. The current profile records every LOD position/index/tangent/UV stream, sections, bounds, simple convex hull vertices, and each inline cooked collision payload identity.

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- inspect-mesh .\Extracted `
  UnrealTournament/Content/MyMap/Meshes/ProblemMesh.uasset `
  --output .\ProblemMesh.mesh-ir.json --editor-obj .\ProblemMesh.editor.obj

dotnet run --project .\src\Ut4Recon.Cli -- create-mesh-workspace .\Extracted `
  UnrealTournament/Content/MyMap/Meshes/ProblemMesh.uasset .\ProblemMesh-workspace
```

The editor OBJ contains the decoded render mesh, imported normals and UV0, plus `UCX_` convex collision meshes. After the user edits collision and cooks a donor with the matching UT4 editor, graft only its BodySetup and NavCollision closure into a copy of the original cooked asset:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- graft-collision `
  .\original\ProblemMesh.uasset .\donor\ProblemMesh.uasset .\output\ProblemMesh.uasset `
  --internal-path UnrealTournament/Content/MyMap/Meshes/ProblemMesh.uasset
```

The recovery-project workflow records the same replacement in the edit manifest, pins the cooked donor by hash, and applies it during the normal verified pak build:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- replace-collision .\RecoveredMap `
  --object <bodysetup-id> --donor .\donor\ProblemMesh.uasset

dotnet run --project .\src\Ut4Recon.Cli -- build .\RecoveredMap --output .\Map-Repaired.pak
```

Collision graft v1 requires identical name/import tables and export identities. It copies the donor BodySetup and NavCollision exports, then proves the original StaticMesh render export stayed byte-identical, the BodySetup matches the donor, and unrelated exports did not change. `compare-mesh` provides a second semantic comparison of all decoded render and collision streams.

The retained `SM_Chair` fixture has 1,467 render vertices, 1,782 triangles, one 16-vertex convex hull, and a 51,929-byte PhysXPC payload. Its changed donor moves four hull vertices from X=40.2119 to X=35 and was recooked by UT4 Editor. The complete manifest build changes only the BodySetup export, verifies two untouched exports, semantically matches the donor collision, preserves the original render data, and produces byte-identical paks on repeated builds (SHA-256 `006a04e60b8985c4d1fdebf57aa675201e392cdeab60186369b1b060f42a3f2c`).

The installed editor's `ImportAssets` commandlet builds the generated OBJ but asserts before saving, so this binary-only environment currently needs interactive OBJ import or an already-created compatible source asset before cooking. Collision graft v1 also requires the donor to retain identical name/import tables and export identities. The matching standalone client and server now load the repair patch; deterministic runtime PhysX A-B queries remain part of Milestone 7.

## Decode and replace BSP

`inspect-bsp` decodes UT4 4.15 `UModel` vectors, points, nodes, surfaces, vertex-pool entries, and polygon geometry while hashing native header/tail data that is not yet interpreted. `create-bsp-workspace` selects the principal model and writes an editable recovered brush:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- inspect-bsp .\Example_Map.umap --output .\Example_Map.bsp-ir.json
dotnet run --project .\src\Ut4Recon.Cli -- create-bsp-workspace .\Example_Map.umap .\Example_Map-BSP
```

After editing, rebuilding, and cooking a donor with the matching editor, record it in the recovery project and build the pak:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- replace-bsp .\RecoveredMap `
  --object <principal-model-id> --donor .\donor\Example_Map.umap
dotnet run --project .\src\Ut4Recon.Cli -- build .\RecoveredMap --output .\Map-Repaired.pak
```

Use `--fresh-shell` when the donor was created by importing the recovered T3D into a new map package:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- replace-bsp .\RecoveredMap `
  --object <principal-model-id> --donor .\donor\RecoveredBSP.umap --fresh-shell
dotnet run --project .\src\Ut4Recon.Cli -- build .\RecoveredMap --output .\Map-Repaired.pak
```

The retained changed `Example_Map` donor moves one referenced BSP point by 25 Unreal units. Its manifest build changes only the principal `UModel`, preserves the `Level` export and all compiled level data, verifies 2,267 untouched exports, and produces byte-identical paks on repeated builds (SHA-256 `a924d868c304915530f5a83d04ee43f4ffb7e14b5a9c244652b12d82d8e69c2a`). The separately reconstructed brush demonstrates that original CSG history is unnecessary for editable surface recovery: after editor rebuild it retains identical bounds and offline ray results, with a surface-area difference ratio of `7.9e-9`.

The fresh-shell profile maps donor object references into the original package by resolved object path, rewrites native UModel and ModelComponent indices, transplants each cooked BodySetup, reuses existing closure exports, and appends closure pairs when the rebuilt model has more components. On the retained recovered-brush donor it maps 67 donor component/BodySetup pairs into a 66-pair baseline, changes 134 existing closure exports, appends two exports, preserves the Level's opaque compiled data, verifies 2,134 unrelated exports, and matches the donor's 1,005-polygon geometry and normalized collision payloads. Repeated full builds produce identical pak SHA-256 `cfe5dbae47dfa2ca43c6171a1a0472b90422fdb1fd6a935c8466e16841d14cd3`.

This initial profile requires one persistent `Level`, one principal UModel, the observed UT4 4.15 UModel/ModelComponent/BodySetup native layouts, and every external donor reference to resolve uniquely in the original package. Unsupported layouts or unresolved references stop the build. The standalone server now proves that the reconstructed BSP package loads when it overrides the stock package; runtime collision equivalence still requires the Milestone 7 query harness.

## Extract and inject an asset independently

Milestone 9 exposes the preservation/graft engine without requiring the generated editor workspace. Start from a recovery project and select a reconstruction object ID:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- list-extractable .\RecoveredMap `
  --output .\extractable-assets.json
dotnet run --project .\src\Ut4Recon.Cli -- extract-asset .\RecoveredMap `
  --object <object-id> --output .\AssetBundle
```

`list-extractable` reports only objects that have one of the supported replacement contracts and live in a directly writable cooked package. Use `--match <substring>` to narrow the inventory by object path or class. Each result states its contract kind and, for a property-set object, the exact editable property paths.

The bundle contains an immutable cooked baseline, included package dependencies, editable interchange files, `extraction-manifest.json`, and a hash-pinned `replacement-contract.json`. Its generated README states what the user owns and what the injector preserves. Supported initial contracts are:

- **Property set:** edit only values in `interchange/properties/modified-properties.json`; identity, property paths, and writer rules are fixed.
- **Static-mesh collision:** use the OBJ/JSON mesh workspace to author collision, import and cook it with the matching UT4 editor, and supply a compatible-shell cooked donor. Render data remains baseline-owned.
- **BSP model closure:** edit the recovered T3D/OBJ representation, rebuild and cook it, then use a compatible-shell or validated fresh-shell donor. The original brush history is unavailable; persistent Level and compiled behavior exports remain baseline-owned.

Validate and inject the result:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- validate-donor .\AssetBundle .\Donor.uasset
dotnet run --project .\src\Ut4Recon.Cli -- inject .\RecoveredMap .\AssetBundle `
  --donor .\Donor.uasset --output .\asset-edit-manifest.json
dotnet run --project .\src\Ut4Recon.Cli -- set-map-title .\RecoveredMap `
  --title Map-Repaired --output .\asset-edit-manifest.json
dotnet run --project .\src\Ut4Recon.Cli -- build .\RecoveredMap `
  --manifest .\asset-edit-manifest.json --rename-map Map-Repaired `
  --output .\Map-Repaired.pak
```

Property documents default to `external` mode. Cooked collision and BSP packages default to `compatible`; pass `--mode fresh` for a BSP asset rebuilt in a new package shell. Validation recomputes the declared closure from the hash-checked baseline, preflights the real native writer/graft, rejects an unchanged selected root, and reports donor-owned objects, preserved scopes, and the number of unrelated exports verified. Injection only emits ordinary recovery-project operations, so the existing build, behavior-preservation, pak-integrity, rename, and runtime-certification stages remain in force.

Asset bundles do not currently accept arbitrary render-mesh replacement or a general actor closure. Actor transforms, supported collision properties, blocker/static-mesh actor cloning, and deletion use the recovery-project/editor-export manifest path described above.

## Add custom collision through an isolated donor

Custom brush collision is authored away from the recovery map so cooking cannot
replace unrelated map content. The native panel exposes the same lifecycle under
**Add Custom Collision**, and the commands remain available for scripting:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- create-custom-collision-workspace `
  --output .\CustomCollisionDonor --editor E:\path\to\UnrealTournamentEditor
dotnet run --project .\src\Ut4Recon.Cli -- run-custom-collision-import .\CustomCollisionDonor
dotnet run --project .\src\Ut4Recon.Cli -- open-custom-collision-workspace .\CustomCollisionDonor
```

Edit the single `BlockingVolume` brush, save, and close that editor. Then cook and
certify it:

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- cook-custom-collision-donor .\CustomCollisionDonor
dotnet run --project .\src\Ut4Recon.Cli -- add-custom-collision .\RecoveredMap `
  --donor .\CustomCollisionDonor\custom-collision-donor.json `
  --name UT4Recon_CustomCollision_01 `
  --location '{"x":0,"y":0,"z":0}' `
  --rotation '{"pitch":0,"yaw":0,"roll":0}' `
  --scale '{"x":1,"y":1,"z":1}' --append
```

Certification permits one persistent `BlockingVolume` plus the structural map
actors created by the editor. It rejects gameplay actors, empty or degenerate
geometry, an unexpected closure, the wrong editor/runtime profile, and changed
package or manifest hashes. The user-owned result is exactly one
`BlockingVolume`, `BrushComponent`, `UModel`, and PhysX `BodySetup` closure.
Build remaps that closure into the original cooked map, appends its Level actor
reference, and preserves compiled behavior and every unrelated export.

This workflow supports one connected brush actor per donor and translation,
rotation, and relative scale at placement time. Multiple blockers use multiple
certified additions. General gameplay actors, custom Blueprint behavior,
navigation rebuilding, and arbitrary non-brush collision assets remain outside
this contract.

## Preserve compiled behavior

`inspect` and `create-project` now emit `behavior-baseline.json`. The behavior inventory records each cooked Blueprint-generated class, inheritance and class metadata, class default object, owned component/SCS templates, delegate data, complete import-table fingerprint, compiled functions, decoded opcode counts, call targets, and both semantic bytecode and serialized function hashes.

```powershell
dotnet run --project .\src\Ut4Recon.Cli -- inspect-behavior .\Blueprint_CeilingLight.uasset `
  --internal-path UnrealTournament/Content/MyMap/Blueprint_CeilingLight.uasset `
  --output .\Blueprint_CeilingLight.behavior.json

dotnet run --project .\src\Ut4Recon.Cli -- compare-behavior `
  .\before.behavior.json .\after.behavior.json --output .\behavior-validation.json
```

Every recovery-project `build` recomputes the behavior snapshot from the immutable baseline, checks any recorded baseline against it, and compares the staged packages before UnrealPak runs. A changed function, generated-class field, default object, component/SCS template, delegate, behavior package set, or import table stops the build. Behavior-bearing editor actors are explicitly labeled `[Behavior preserved; proxy]`; their compiled class remains in the cooked baseline while allowlisted instance/component edits are applied to the original map exports.

The retained CeilingLight fixture contains a 127-byte construction script whose decoded calls are `SetIntensity`, `SetLightColor`, and `SetSourceRadius`. The negative regression changes its class-default brightness and is rejected while separately confirming that its function bytes remained unchanged. The fresh Example_Map BSP repair verifies one LevelScript class and five compiled functions with identical behavior fingerprints, while retaining the same deterministic pak SHA-256 as Milestone 5. Runtime observation of construction results, delegates, and events still requires the matching client in Milestone 7.

## Experimental native editor adapter

The installed UT4 editor omits headers, UnrealHeaderTool, and module import
libraries. The recovered `UnrealTournament-clean-master` tree supplies UE4.15
headers, while the exact installed editor DLLs supply the ABI exports and module
API version. The binary bootstrap builds and optionally installs an editor-only
module without rebuilding Unreal Engine:

```powershell
.\native\Ut4ReconEditor\Build-BinaryPlugin.ps1 -Install
```

The initial Core-only module has been loaded successfully by editor CL 3525360.
This proves the adapter route, not blanket ABI compatibility: every added Slate,
Engine, UnrealEd, or UT interface must receive a focused load/use test. The first
panel will avoid reflected Unreal types, because the matching UnrealHeaderTool is
still unavailable. See `native/Ut4ReconEditor/README.md` for the current boundary.

The Build view is operational. Editor-workspace schema v3 pins the recovery
project and exact backend executable, so the plugin does not discover a shell or
repository at runtime. After saving the map, use **Export saved map**, **Validate
changes**, and **Build integrity-tested pak** in order. Each command runs
asynchronously, writes `backend-action.log`, and leaves Slate responsive. The
build fields specify a distinct map/package name and output pak. The panel first
upserts the corresponding supported `UUTLevelSummary.Title` operation and then
runs the package, BuiltData, AssetRegistry, version-file, and map-URL rename with
the normal preservation gates. The view reports Map Check warnings, editor diff,
compiled behavior, final pak validation, the output path, and any separately
recorded runtime certification. Runtime evidence is shown at its actual level: a
successful `package-load` report does not claim that live actor state or player
interaction was tested.

The Build button is enabled only when the saved `.umap`, T3D export, passing
editor-diff report, and edit manifest form a current sequence. If the map was
saved after export, or the export is newer than validation, the panel names the
numbered step to rerun. Failed actions keep the immutable baseline untouched,
show a recovery instruction, and expose the captured action log directly.

After testing a built pak with the matching standalone server, attach the tested
pak and log to the recovery project with:

```powershell
ut4recon certify-runtime-load <project> --pak <tested.pak> --log <server.log> `
  --map /Game/path/to/Map --game-mode UTCTFGameMode
```

This verifies, in order, that the named pak mounted, the requested map began
loading, the same map completed loading, and (when supplied) the expected game
mode was selected. It writes `.ut4recon/reports/runtime-certification.json` and
hashes the supplied pak for traceability. Because the UT4 log records only the
pak filename, it cannot cryptographically bind the logged mount to those bytes. The report
explicitly leaves live actor/component state and actual player collision,
movement, pickup, and other interaction behavior uncertified.

## Build the Windows prototype bundle

```powershell
.\release\Build-Prototype.ps1
```

The repeatable package build runs the tests by default, publishes a self-contained
Windows x64 CLI, compiles the editor plugin against the pinned headers and editor
exports, and bundles schemas, fixtures, documentation, a dependency inventory,
per-file SHA-256 checksums, and a zip archive. Archive compression writes to a
partial filename and exposes the final zip only after successful completion. The generated
`Install-EditorPlugin.ps1` rejects an editor whose compatible API is not 3525109.
Prototype archives are unsigned; signing and a separate-machine installer test
remain release-hardening work.

The retained Switchback prototype certification rebuilds the four Half_Blinds
collision closures byte-identically, verifies 6,636 preserved exports, and loads
the distinctly renamed map in the pinned dedicated server with `UTCTFGameMode`.
It also exercises stale-hash, unsupported-rule, missing-title, and wrong-editor
rejections. The machine-readable evidence is written outside the distributable
archive under `artifacts/release-package-workspace-smoke`.

The `cert4` package additionally certifies the existing-pickup transform profile.
Its extracted archive passed all 61 recorded hashes, the packaged CLI reproduced
the certified one-export pickup move byte-for-byte, and the packaged plugin DLL
installed, loaded in editor CL 3525360, and resolved the pinned selection ABI.
Evidence is retained in `artifacts/release-cert4-certification.json`.

The final Switchback feature playtest confirms the main bug-fixing operations in
the standalone client: a static-mesh move persisted, duplicated/transformed
BlockingVolumes changed traversal, deleting all 81 collision overlays exposed
previously blocked corners, and all 12 team player starts could be relocated to
one shared runtime position. The latest panel smoke test loaded 3,106 indexed
objects, exported those 81 deletions with zero diagnostics, retained the chosen
`Repaired4` identity across panel reconstruction, and completed the renamed pak
build while verifying 6,636 untouched exports. Custom collision donor authoring
remains an advanced workflow whose final human runtime test is deferred.
