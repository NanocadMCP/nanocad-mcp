# nanoCAD MCP release notes

[Documentation](docs/en/index.md)

## Build 461

- Fixed write recovery when the bridge connection is interrupted. Oversized requests report that no write started. If a native command leaves writes isolated, new requests identify that command immediately.
- Improved paging for concurrent drawing queries, reduced PNG capture processing, and bounded routine log growth across application restarts.
- Fixed parametric body renaming in 3D History. The name is stored on the top-level part and survives reopening the DWG. Nonparametric bodies that lose their captions on reopen now return a specific error before any rename in the request.
- Added in-place contour edits for associative hatches bounded by lines and arcs. Connected dimensions and circular holes retain their original associations.
- Added guide-to-section contact checks for Loft and material interval measurements on sections with spline or elliptical edges. Loft errors now identify a rejected section normal setting.
- Added downstream feature history and before-and-after readings for associated sheet dimensions when a model parameter changes. Fixed vertical projected dimensions and verification of sketch elements constrained to a moving edge.

## Build 460

- Revised drawing review instructions to check view placement before dimensioning, inspect annotations at a readable scale, and review every changed DWG sheet before delivery.
- Updated capture guidance for unavailable or stale images. The assistant is instructed to flag sheets it could not inspect.

## Build 459

Fixed a regression in 458 that prevented move and edit operations on imported 3D solids with DWG group labels.

## Build 458

- Added persistent labels for imported ACIS solids. Labels are stored in DWG groups and are editable in Group Manager.
- Added individual edge selection for parametric fillets and chamfers.
- Added associative center-to-center dimensions for projected holes, including scaled views.
- Added capture of the current drawing or the last saved DWG, with framing by drawing coordinates. Fixed loading of relative raster references in current-drawing captures.

## Build 457

- Solids in the model tree can now be named and searched by name.
- Added planar solid sections, material thickness measurements along a line, and point-cloud comparison against a solid section.
- Face inspection now identifies the feature that created a face and exposes its parameters and dependencies when available.
- Added concentric hole placement on cylindrical bosses and corrected through-hole depth after part length changes.
- Sheet layout tools now check for overlap with views and annotations. New layouts can copy a title block with its attributes.
- Added dimension lookup by projected geometry and support for repositioning associative diameter labels with `DIMTMOVE=2`.
- Added gap detection for selected curves and pixel-to-drawing coordinates for raster attachments.

## Build 456

- Added first-angle and third-angle projection, child view alignment, and angular dimensions between two projected segments. Corrected diameter dimensions on scaled views.
- Added inspection of table cell formulas, units and calculated values. Row insertion now preserves references in supported formulas.
- Fixed false rejections of small drill cuts on large solids.
- Fixed a dimension text bug that prevented restoring the measured value after a long text override.
- Added inspection of loft profiles and thread pitch and length parameters.
- Added 3D bounds filtering for object searches, nested block transforms, editing of external DWG reference paths, and a third control point for checking raster calibration.

## Build 455

Fixed a nanoCAD crash when Codex Computer Use queried an empty tab control.

## Build 454

- Setup checks the full nanoCAD version and required API libraries before
  installing runtime files, registering the bridge or changing client settings.
  The minimum tested executable is `nCad.exe` version `26.0.7476.4950` in the
  nanoCAD 26.0 series; required libraries have individual version checks.

- Older, incomplete or unreadable installations receive the detected and
  required versions with instructions to update nanoCAD manually. Direct and
  elevated bridge registration use the same check. Removal remains available.

- This installer update retains the exact server, bridge and native runtime
  products from453. No CAD modeling behavior or runtime component version changes.

Use the matching installer, runtime ZIP, source/documentation archives and
`SHA256SUMS-454.txt` from
[release454](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/454).

## Build 453

- Reuse eligible local millimetre DWG Model Space content as independent static
  block definitions. Replace an eligible shared definition while retaining its
  identity, insertion placements and existing attribute values. Supported
  metadata and native symbol references are checked through the actual clone
  and readback.

- Replace eligible direct-solid geometry while retaining the target identity,
  supported data and documentation links. The production route includes native
  owning cleanup; the source remains independent.

- Retire explicit ordinary view subtrees, supported Linear/Curved Break2d
  objects and independent non-live Plane Sections while retaining model
  sources and unrelated views. An eligible standalone direct source can then
  be removed through its native owner and verified owned data.

- Read and explicitly control native DrawingView preview mode, or request a
  selected-view refresh. Preview control was checked against actual graphics
  and a saved DWG. Existing sources, placement and supported links remain.

The accepted Simple Hole formula, existing dynamic-property, geometry-snap
and projected-dimension workflows from 452 remain. Their previous supported
contexts and unassessed combinations are unchanged.

Local/outer-cut functionality is excluded: views carrying these cuts and their
cut sketches are read-only, with dependency protection retained. Named
Shell-thickness formulas remain guarded because native updates can lose the
formula and dependency; supported literal Shell edits remain available.

This package uses server 0.282.0, bridge 0.238.0 and IPC protocol 0.144.
The installer is unsigned. Use the matching installer, ready-to-run ZIP,
source/documentation archives and `SHA256SUMS-453.txt` from
[release 453](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/453).
Read [capabilities and limits](docs/en/capabilities.md) for eligibility and scope.

## Build 452

- Snaps to existing geometry now include Circle/Arc centers and the point roles
  of all nine ordinary dimension kinds. A snap resolves a point once;
  persistent dimension associations remain separate.

- Native projected radius, circular-arc length and arc-angle dimensions can
  follow their selected sources in ordinary auto-updating 1:1 Paper views of
  one local body.
  Stored native view-anchor observations were verified for five existing
  dimensions: one diameter, two radii, one arc length and one angle.

- Named User formulas can drive a selected native control of an eligible fixed
  Simple Hole. The assessed diameter change retained the formula and User
  dependency, changed the bore and updated its projected diameter after DWG
  reopening. Depth, seat and other Hole families remain unassessed.

- Existing dynamic blocks expose native property inventories and single-property
  editing. Distance, visibility and Int16 flip edits were checked against actual
  geometry and retained their saved values and geometry after reopening.
  Point2d, Point3d, Int32 and wider lookup/attribute/dependency combinations
  remain unassessed. Dynamic-block authoring is unavailable.

Named Shell-thickness formulas remain unavailable because native updates can
lose the formula and dependency. Supported literal Shell operations remain.
DWG-library import/redefinition writes are refused before mutation pending
native acceptance; their read-only preflight does not establish a working
import. The experimental native solid-replacement cleanup module is excluded;
the existing replacement route retains its provisional lifetime scope.

This package uses server 0.267.0, bridge 0.224.0 and IPC protocol 0.144.
The installer is unsigned. See [GitHub Release 452](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/452)
for the installer, archives and SHA-256 checksums. Read
[capabilities and limits](docs/en/capabilities.md) for the supported contexts.

## Build 451

Write tools can check a proposed operation with `validate_only` and return
current object state with `return_state`. Geometry writes default to millimeters.
A revision qualified with its bridge and document can identify the only active
drawing without repeating both IDs.

Object queries can summarize type and layer counts and drawing bounds.
Point and Line creation and native trim accept snaps to existing geometry.
The MCP catalog exposes tool arguments directly and offers focused variant help.
WCS bounds can crop a visible orthographic Model view without moving the camera.

This package uses server 0.254.0, bridge 0.209.0 and IPC protocol 0.144.
The installer is unsigned. See [GitHub Release 451](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/451)
for the installer, archives and SHA-256 checksums.

## Build 450

Aligned dimensions can follow a selected line of an ordinary unit-scale
drawing view in Model Space or on an active Paper sheet. The native reference
retains the selected line at shared corners. An A4 dimension updated from 12 to
18 mm after a change to the source extrusion. Paper view creation verifies its
resident view and source history after the nanoCAD command completes.

The fixed-center native Hole feature applies and verifies through extent.
This package uses server 0.253.0, bridge 0.208.0 and IPC protocol 0.144.

Tag `450` identifies the packaged source. Download the installer, ZIPs and
checksums from [GitHub Release 450](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/450).

## Build 449

Server 0.252.0 publishes top-level input fields for `manage_document` and
`analyze_geometry` in the default MCP catalog. Clients that discover arguments
from those fields can call both tools. The bridge still checks each action or
mode against its exact input contract. This package uses bridge 0.207.0 and IPC
protocol 0.144.

Tag `449` identifies the source used to build the package. Download the EXE,
portable ZIP and checksums from [GitHub Release 449](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/449).

## Build 448

Server 0.251.0 verifies the named pipe owner's Windows user SID directly, so
the Administrators group can be the elevated token's default owner. The
per-user pipe permissions and peer process, session and elevation checks
remain in force. Bridge 0.207.0 and IPC protocol 0.144 are carried forward.
The default tool catalog uses compact operation indexes for entity and
structure edits. Exact variants remain available through focused guidance.

Tag `448` identifies the source used to build the package. The EXE, portable ZIP
and checksums are available from [GitHub Release 448](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/448).
The installer is unsigned. Installation on a clean Windows machine has not
been verified.

## Package 447

Tag `447` identifies the source of the preceding maintenance package.

## Package contents

| Component | Version |
|---|---|
| MCP server | 0.250.0 |
| nanoCAD bridge | 0.207.0 |
| IPC protocol | 0.144 |

The Windows x64 release contains a self-contained MCP server, the nanoCAD bridge and native helpers, the view-capture worker, installation and connection-check scripts, and English documentation. Original project code and documentation use [MIT](LICENSE); [third-party terms](THIRD-PARTY-NOTICES.md) remain separate.

Supported workflows include ordinary 2D drawing, direct and retained parametric modeling of individual parts, editable DWG documentation, local references and loaded point-cloud interpretation. Profile consumers include extrusion, revolution, sweep and loft within their documented requirements. Read [capabilities](docs/en/capabilities.md).

## Changes in 447

- The initial MCP tool list is smaller. The same tools and input rules are available; request the focused schema and operation help for the full details of a selected write.
- The server resolves exact published tool names. Input validation reports malformed geometry and invalid text as request errors.
- The developer workflow includes a Windows managed build and test job, structured test reports, and a way to rerun failed checks by name. See [development](docs/en/development.md).

The nanoCAD bridge and protocol versions are unchanged. The package carries forward the native modeling capabilities and limits documented for [release 446](https://github.com/NanocadMCP/nanocad-mcp/releases/tag/446). The shared validation library includes the request fixes; packaging does not add new native CAD acceptance.

Use the EXE for guided installation or the ready-to-run ZIP for manual setup. The installer is unsigned; installation on a clean computer has not been verified. Read [installation](docs/en/installation.md) for setup, updates and removal.
