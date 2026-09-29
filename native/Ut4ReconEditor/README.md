# UT4 Recon native editor adapter

This is an experimental editor-only adapter for the installed UT4 editor
`4.15.0-3525360+++UT+Release-Next`. The editor distribution omits C++ headers,
UnrealHeaderTool, and import libraries. `Build-BinaryPlugin.ps1` combines:

- a matching UT4/UE4.15 source tree supplied through `-SourceRoot`;
- the exact module exports and API version from the installed editor; and
- the installed Visual Studio 2022 C++ toolchain and Windows SDK.

The script creates import libraries from the exact installed editor DLLs,
compiles the module without reflected Unreal types, embeds the editor's API
version resource, and emits a conventional binary plugin under the selected
artifact output directory.

```powershell
.\native\Ut4ReconEditor\Build-BinaryPlugin.ps1 `
  -SourceRoot "E:\path\to\UnrealTournament-source" `
  -EditorRoot "E:\path\to\UnrealTournamentEditor" `
  -Install
```

The first Core-only smoke module and the current Slate panel module have both
loaded successfully in the installed editor. The current module registers a
nomad tab named **UT4 Recon** with Map, Selected object, and Build views. It
reads the generated `proxy-scene.json`, support report, manifest, and validation
reports. The Build view asynchronously launches the exact backend recorded in
editor-workspace schema v3. It exports the saved map, validates supported
changes, prepares a distinct `UUTLevelSummary.Title`, builds and integrity-checks
a renamed pak, and opens the report directory without blocking Slate. The log
contains:

```text
LogUt4ReconEditor: UT4 Recon editor adapter loaded.
```

The Map view separately controls exact actors, collision overlays, reconstructed
BSP context, and workspace lighting. The BSP layer shows final cooked surfaces
as a locked non-colliding context brush; the key/fill lights provide stable
Lit-mode navigation. Selecting either helper states that it has no pak effect.
Both use `UT4RECON_VISUALIZATION` tags that the editor exporter ignores.

The Map view also reports recovered mesh-preview readiness. Workspace creation
stages decoded LOD0 OBJ geometry with the exact original asset filename. The
panel imports only packages that are absent from the editor project and saves
them under their original `/Game/...` paths, so a subsequent proxy-map import
resolves the original actor references. Existing stock source assets are never
overwritten. These packages improve navigation only: the validated build starts
from the immutable cooked baseline and never packages the preview assets.

The pinned-editor Switchback certification imported and saved all eight absent
custom meshes, skipped fourteen existing packages, and then skipped all twenty-two
packages on a repeat run. Reimporting the proxy map resolved 62 custom mesh
references with zero `NULL StaticMesh` exports, and the unchanged diff still
produced zero patch operations. Missing source materials remain load warnings and
may leave these previews neutral-colored.

Pass `-UT4ReconOpen` when launching the editor to open the tab automatically.
Generated workspaces enable the adapter when it is installed, open their
recovered map, and show the tab through the normal `open-editor-workspace`
command. The visibility checkboxes temporarily hide the tool's tagged actor
categories without changing or exporting the map. Save the map and stop editing
while the separate unattended export process reads it. The panel shows the
active process, exit status, log location, Map Check result, editor diff,
compiled-behavior result, final pak report, and output path.

The Build button is gated by file provenance: the saved map must not be newer
than its T3D export, and both the passing diff report and edit manifest must not
be older than that export. The panel states which numbered step is stale. A
failed action includes a retry instruction and an **Open action log** button.

For isolated certification fixtures, `-UT4ReconPreload=<object-path>` loads one
exact asset before editor command files run. This internal automation hook lets
a T3D actor resolve a Blueprint generated class without loading a large reference
map. Normal safe workspaces do not use it, and it does not add editing rights.

## Boundary

The recovered headers are from CL 3228288 while the installed runtime is CL
3525360 with compatible API CL 3525109. Module startup and Slate tab
construction have been exercised in the exact installed editor. The Switchback
workspace certification loaded 3,082 inventory objects, enumerated 1,460 tagged
workspace actors, hid and restored a category with zero hidden-state mismatches,
and selected an exact collision overlay whose inventory mapping passed. This
certifies inspection, selection, and temporary visibility for the pinned
runtime. Persistent actor mutation remains owned by the external manifest
workflow.

The asynchronous-action certification opened the real panel, launched both
editor-diff validation and a saved-map commandlet export, captured their output,
and observed successful completion. A full Switchback probe then chained the
distinct title operation into a renamed pak build. It preserved 6,637 exports,
changed only `UUTLevelSummary`, passed compiled-behavior and UnrealPak
validation, and wrote `CTF_Switchback_PRO2-Repaired-WindowsNoEditor.pak`.

The Build view also exposes **Add Custom Collision**. It creates and initializes
an isolated donor project, opens its single starter `BlockingVolume`, cooks and
certifies the edited brush with the pinned editor, and appends a placement
operation with actor name, location, rotation, and scale. The recovery map is
never used as the donor cook target.

Slate's public headers reference generated headers that are absent from the
recovered tree. The build creates empty generated-header stubs and suppresses
reflection declaration macros while parsing the small set of Slate value types
used by this module. The plugin defines no reflected types. This workaround is
specific to the pinned editor and must be re-certified whenever the compiled
surface changes.

This path can implement a Slate tab, menus, process invocation, JSON protocol,
selection helpers, and editor delegates without `UCLASS`, `USTRUCT`, or
`UFUNCTION`. Reflected types and custom UObject classes remain unavailable until
a matching UnrealHeaderTool executable can be recovered or built.
