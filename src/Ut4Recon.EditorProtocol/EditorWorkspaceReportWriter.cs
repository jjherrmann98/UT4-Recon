using System.Text;
using Ut4Recon.Core;

namespace Ut4Recon.EditorProtocol;

public sealed class EditorWorkspaceReportWriter
{
    public EditorWorkspaceReport Write(ProxyScene scene, CollisionOverlayIndex overlays, string reportPath, string guidePath, WorkspaceVisualizationIndex? visualization = null)
    {
        var capabilityCounts = scene.Objects.SelectMany(x => x.Capabilities)
            .GroupBy(x => (x.Operation, x.State))
            .OrderBy(x => x.Key.Operation, StringComparer.Ordinal).ThenBy(x => x.Key.State)
            .Select(x => new CapabilityCount(x.Key.Operation, x.Key.State, x.Count())).ToArray();
        var objects = scene.Objects.OrderBy(x => x.ObjectPath, StringComparer.Ordinal)
            .Select(x => new EditorWorkspaceObjectSummary(x.ReconstructionId, x.ObjectPath, x.ClassPath, x.Fidelity,
                x.Capabilities.Where(c => c.State == CapabilityState.Available).Select(CapabilityName).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray(),
                x.Limitations)).ToArray();
        var diagnostics = scene.Diagnostics.Concat(overlays.Diagnostics).Distinct(StringComparer.Ordinal).ToArray();
        var report = new EditorWorkspaceReport(FormatVersions.EditorWorkspaceReport, scene.Profile, scene.InputPak, scene.MapPackagePath,
            scene.Objects.Count, scene.FidelityCounts, capabilityCounts, overlays.Entries.Count,
            scene.Objects.Count(x => x.Fidelity == FidelityCategory.BehavioralProxy), scene.Objects.Count(x => x.Limitations.Count != 0), false, objects, diagnostics,
            visualization?.BspPolygonCount ?? 0, visualization?.LightingActorCount ?? 0);
        ReconJson.Write(reportPath, report);
        File.WriteAllText(guidePath, Guide(report), new UTF8Encoding(false));
        return report;
    }

    private static string CapabilityName(ProxyCapability capability) => capability.PropertyPath is null ? capability.Operation : $"{capability.Operation}:{capability.PropertyPath}";

    private static string Guide(EditorWorkspaceReport report)
    {
        var counts = string.Join(Environment.NewLine, report.FidelityCounts.Select(x => $"- **{Label(x.Category)}:** {x.Count}"));
        return $"""
# UT4 cooked-map patch workspace

This workspace represents `{report.MapPackagePath}` using data recovered from the cooked pak. The original cooked packages remain the authoritative baseline. The editor map is an authoring view whose supported changes are translated into a patch manifest.

## Fidelity summary

{counts}
- **Exact collision overlays:** {report.ExactCollisionOverlayCount}
- **Reconstructed BSP context polygons:** {report.ReconstructedBspContextPolygons}
- **Workspace lighting actors:** {report.WorkspaceLightingActorCount}

Actor labels and tags identify fidelity inside the editor. See `workspace-support-report.json` for every represented object, its available operations, and its limitations.

## Workflow

The installed UT4 Recon panel drives the normal workflow:

1. `ut4recon run-editor-import <workspace>` — materialize the generated map. Run this again only to reset the editor map from the generated T3D.
2. `ut4recon open-editor-workspace <workspace>` — open the isolated project for interactive editing.
3. On first use, the panel's **Map** view reports missing custom source meshes. **Import missing recovered mesh previews** imports recovered LOD0 geometry with fixed automated static-mesh defaults and without overwriting stock source assets. Close the editor, rerun `run-editor-import`, and reopen the workspace so actor references resolve.
4. Make only edits listed under **Editable in this workspace** for the selected object, then save the map. Direct/external operations are available through the manifest tools but are not captured from the Details panel.
5. In the panel's **Build** view, run **Export saved map**. Stop editing while its separate unattended process reads the saved package.
6. Run **Validate changes** and review Map Check and editor-diff results. The panel lists every validated pending operation before packaging.
7. Enter a distinct map name and output path, then run **Build integrity-tested pak**. The panel synchronizes the supported map title and builds through the same preservation backend as the CLI.

The Build button remains disabled until the saved map, T3D export, passing editor-diff report, and edit manifest form a current sequence. The panel identifies the numbered step to repeat when the map or export is newer. Failed helper commands leave the immutable cooked baseline untouched; use **Open action log**, correct the reported problem, and retry that step.

The equivalent `run-editor-export`, `export-editor-edits`, `set-map-title`, and `build --rename-map` commands remain available for scripted use.

## Necessary limitations

- **Exact editable** objects expose values with validated cooked-package writers.
- **Reconstructed** objects are rebuilt from surviving runtime data; missing source authoring history cannot be restored.
- **Behavioral proxy** objects preserve their original compiled cooked behavior. Their displayed native proxy is not the missing Blueprint graph.
- **Preserve only** objects remain in the cooked baseline but have no general editor representation or writer.
- Exact collision overlays are non-colliding visualization actors. Moving, rotating, duplicating, or deleting a canonical BlockingVolume overlay is supported. Deletion removes the original cooked BlockingVolume from the persistent level. Arbitrary brush-vertex edits are rejected.
- Reconstructed BSP context shows final cooked level surfaces for navigation. It is protected, non-colliding, editor-only, and cannot be edited through the normal workspace.
- Workspace preview lights improve Lit-mode navigation. They are editor-only and are never inserted into an edit manifest or output pak.
- Player starts use inert, non-colliding cylinder markers. Moving a marker writes the original cooked capsule location, and serialized rotation is writable when present. Marker shape/scale, deletion, cloning, team assignment, and runtime spawn behavior are blocked.
- Existing pickups with one unambiguous cooked capsule use inert, non-colliding sphere markers. Only serialized location/rotation edits are captured; item type, respawn settings, marker appearance, deletion, cloning, and runtime pickup behavior are blocked.
- Recovered mesh previews restore decoded LOD0 shape under missing original source paths. They use neutral import materials and do not reconstruct authored materials, other LODs, sockets, or source import settings. They are navigation context; importing them never changes the cooked baseline.
- Editor labels, folders, proxy classes, and warning tags are generated metadata and are not injected into the runtime map.
- The exporter rejects missing or duplicate reconstruction IDs, unsupported additions, ambiguous mappings, and unexplained cooked changes.

Do not distribute the generated editor map as the repaired map. The final pak must be produced by the validated build command.
""" + Environment.NewLine;
    }

    private static string Label(FidelityCategory category) => category switch
    {
        FidelityCategory.ExactEditable => "Exact editable",
        FidelityCategory.Reconstructed => "Reconstructed",
        FidelityCategory.BehavioralProxy => "Behavioral proxy",
        _ => "Preserve only"
    };
}
