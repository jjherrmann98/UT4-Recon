# UT4 Recon 0.1 prototype

This early Windows x64 prototype targets unencrypted UT4 `WindowsNoEditor` paks
and was built and tested with UT4 Editor
`4.15.0-3525360+++UT+Release-Next`, compatible API `3525109`. Community client
updates that retain the stock UT4 4.15 package format are expected to work but
are not all individually certified.

1. If you used `UT4Recon-Setup-<version>.exe`, close UT4 Editor, select its root
   in Setup, and then open **UT4 Recon command prompt** from the Start Menu. For
   the portable ZIP, run `Install-EditorPlugin.ps1 -EditorRoot <path>` from
   PowerShell instead.
2. Run `cli\Ut4Recon.Cli.exe create-project <map.pak> --output <project>
   --editor <editor-root>`.
3. Run `cli\Ut4Recon.Cli.exe create-editor-project <project> --output
   <workspace> --editor <editor-root>`.
4. Run `cli\Ut4Recon.Cli.exe run-editor-import <workspace>`, then
   `cli\Ut4Recon.Cli.exe open-editor-workspace <workspace>`.
5. Follow the numbered workflow in the **UT4 Recon** panel.

After a standalone server test, `cli\Ut4Recon.Cli.exe certify-runtime-load`
can bind the exact tested pak and server log to the project. A passing
`package-load` result proves mount and map deserialization only; the panel keeps
live actor state and player interaction visibly uncertified.

The editor workspace is a constrained authoring view. Generated context,
lighting, and recovered mesh previews are never packaged. Compiled Blueprint
and material behavior stays in the immutable cooked baseline. Read the root
`README.md` and each generated workspace README before editing.

The certified transform profiles include existing player starts and conservatively
recognized existing pickups. They appear as inert editor markers and expose only
the serialized location/rotation fields listed by the panel. Item type, respawn,
team assignment, deletion, cloning, collision shape, and runtime behavior remain
locked unless a separate capability explicitly enables them.

Exact collision overlays support move/rotate, compatible duplication, and
deletion. Deleting an overlay removes its corresponding original cooked
`BlockingVolume`; deleting editor-only context or lighting never changes the pak.
The panel retains the chosen distinct map identity and output path within each
workspace. Custom collision donor authoring is included as an advanced workflow,
but its final human runtime test remains deferred in this prototype.

This is an unsigned experimental prototype. The binary editor plugin requires
the compatible editor API above and does not support encrypted paks, arbitrary render-mesh replacement,
material graph recovery, Blueprint graph recovery, navigation regeneration,
landscapes, foliage, skeletal content, or general actor creation.
