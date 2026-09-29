# Certified collision box template

`source/CollisionBoxTemplate.t3d` is the complete authoring source for one
100 x 100 x 100 Unreal brush `BlockingVolume`. It contains no map-specific
content. The source was imported into a blank map and cooked with UT4 Editor
`4.15.0-3525360+++UT+Release-Next` (runtime API CL 3525109).

`ut4-4.15-windows-no-editor-v1/CollisionBoxTemplate.umap` is the resulting
cooked package. `collision-box-template.json` pins its file hash, four-export
actor closure, geometry, collision payload, native dependencies, supported
transforms, editor changelist, and runtime API. The CLI recertifies all of those
claims before creating an operation and again while building the output pak.

The closure consists of one `BlockingVolume`, `BrushComponent`, `UModel`, and
`BodySetup`. Transplanting it adds a genuinely new runtime collision actor; it
does not require a suitable actor in the target map. Translation, rotation, and
nonuniform scale are supported. Editing the brush topology or collision format
is outside this template profile.
