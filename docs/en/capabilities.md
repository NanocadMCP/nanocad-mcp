# Capabilities and limits

[Home](../../README.md)

nanoCAD MCP connects an AI assistant to a running nanoCAD session. You describe the engineering result; the assistant reads the drawing, chooses supported native operations, makes changes and checks the result. The integration covers ordinary 2D drawings and individual 3D parts. It does not make every nanoCAD command available.

See [installation](installation.md) for requirements and connection instructions, and the [user guide](user-guide.md) for the working process.

## Sketch degrees of freedom

Part sketches and ordinary drawing geometry can report their remaining local degrees of freedom with driving dimensions held fixed. Preview a group of proposed constraints to see which conditions are independent, already implied or in conflict. Each decision identifies the affected proposal and its reason. A sliding direction can be protected explicitly. An additional geometric condition already implied by the current set is omitted. An overlapping driving dimension requires a decision about which dimension should control the geometry.

The calculation uses the actual selected geometry, its relations and the sketch plane. Where the native inventory, references or equations are incomplete, the affected count is unavailable. A fully constrained flag alone cannot supply a count. Sketch angles use radians and ordinary drawing angles use degrees.

## Part names and model history

Naming a parametric body sets its top-level caption in the 3D History panel and retains it in the DWG. In nanoCAD 26, a nonparametric solid can show a new caption during the session but reverts to its generated caption on reopen, including when it was renamed in nanoCAD itself. The bridge reports BODY_NAME_PERSISTENCE_UNAVAILABLE before changing any body in a naming request that includes such a solid. The construction history item and its sketches and features keep their individual names. Drawings created with older versions can still be searched by their existing history item names and DWG group labels. Parts that share a name appear as separate search results.

For a selected face, you can list its boundary edges or trace it to the feature that created it when nanoCAD provides that link. Inspect the feature's parameters and dependencies to find what controls a dimension. Parametric fillets and chamfers accept individual edges. A concentric hole can follow its boss as the boss moves, and an existing Simple Fixed through hole can be updated after the part changes length.

## Sections and drawing views

A planar section shows the cut profile of a solid. A measurement line reports the intervals of material and void along the cut, including cuts whose boundary contains spline or elliptical segments. For a loaded point cloud, the assistant can compare points against a solid section and show which parts of the profile have scan coverage.

On a sheet, you can check whether a proposed view overlaps other views or annotations and find the dimensions attached to a particular projected edge or circle. A dimension attached to two projected hole centers follows their spacing. A dimension placed using paper coordinates needs a separate edit when its defining points move. Associative diameter labels can be repositioned with `DIMTMOVE` set to 2.

The assistant is instructed to inspect each changed DWG sheet after placing views, after adding dimensions and before delivery. Drawing captures can show the open drawing, including unsaved edits, or the last saved DWG. If the drawing renderer cannot capture the current sheet, the full nanoCAD window can show the current edits. Use `saved_drawing` to review a version already saved to disk. The drawing renderer can display an attached raster upside down. When tracing, use the original image and the attachment's pixel origin and axis vectors to locate points in drawing coordinates.

## Drawings

| Area | Available work |
|---|---|
| Find and inspect | Search by object type, text, location or selection; read geometry, properties, drawing structure and supported relationships. |
| Curves and contours | Create and edit Points, Lines, Circles, Arcs, Ellipses, Polylines and Splines within each operation's supported plane and ownership. Spatial Splines and 3D Polylines are available. Smooth Polylines can use actual tangent arcs and straight segments. |
| Drafting edits | Supported move, rotation, copying, mirroring, scaling, offset, trim, split, extension, fillet and chamfer operations. Availability depends on the object and its relationships. |
| Existing geometry snaps | Point and Line inputs, native trim, Circle/Arc centers and the point roles of all nine ordinary dimension kinds can use supported snaps to existing geometry. |
| Repetition | Rectangular, circular and path drawing arrays; block grids; independent copies. Arrays and copies have different editing behavior. |
| Text and notes | Native TEXT/MTEXT, dimensions, leaders and multi-arrow MLeaders; supported formatting, alignment, placement and editing. |
| Tables and symbols | Native Tables with supported values, formulas and layout edits; static blocks and attributes; supported properties of existing dynamic block insertions; selected shared style edits. |
| Fills | Hatch and Region creation and editing, including supported holes and associative boundary links. |
| DWG presentation | Supported drawing views, sections, layouts and viewports; ordinary local raster and DWG references. The editable deliverable is a DWG. |

Existing-object edits aim to preserve useful source geometry, identities and supported dependencies. Objects with foreign ownership, opaque custom data or unsupported relationships can require a different route or be refused. Read access does not guarantee edit access.

For a hatched XY outline made from Lines and Arcs, submit the endpoint and arc changes together with `set_curve_geometry`. The bridge checks the proposed closed outline and holes before editing the resident curves, then checks the Hatch loops and supported dimension attachments against the edited geometry. A circular hole can retain its own boundary and associated diameter.

Snaps resolve observed points once; a retained dimension relationship requires
an actual association. Native projected radius, circular-arc length and arc-angle
dimensions can follow selected visible circular sources in ordinary auto-updating
1:1 Paper views of one local body. Arc length and angle require a finite circular
arc; radius can use a full circle or a finite arc. The existing projected aligned and diameter
routes keep their own supported contexts. Projected diameters account for view scale and follow changes to the model diameter. Angular dimensions can use two converging visible segments. A marker identifies the solid edge only when nanoCAD supplies a verifiable source reference. Native stored view-anchor reads are
available for all five assessed dimension cases; ordinary measurement data can
still be explicitly unavailable for a particular association.

Existing dynamic block properties can be inspected and edited individually
using their current native inventory. Assessed distance, visibility and Int16
flip changes updated the actual geometry and survived DWG reopening. Values
retain their native units and coordinate frame. Point2d, Point3d, Int32 and
wider lookup, attribute-effect and dependency combinations remain unassessed;
the hidden Origin property's meaning is not inferred.

Table reads expose cell types, units, formulas and evaluated values after row edits. Spatial search can filter objects by X, Y and Z in world coordinates. Nested block inspection can follow one placement path and report its accumulated transform.

## Reuse DWG components

Eligible saved local millimetre DWGs can supply supported static 2D content
from Model Space as a new independent block definition. Definition replacement
can update an existing eligible static block while retaining its definition
identity, origin, insertion placements, attribute definitions and insertion
attribute values.

These routes check supported entity data, symbol references and destination
metadata. They do not author or synchronise source Model Space attribute
definitions, create dynamic blocks or import arbitrary foreign/custom objects.
The assistant reads eligibility before writing and checks the actual result.

## Individual 3D parts

Direct modeling includes supported primitives, extrusion, revolution, Sweep, Loft, Boolean operations, splitting, fillets, chamfers, shells and selected face edits.

For a guided Loft, `loft_guide_contacts` reports finite 3D intersection points between one guide and each ordered section. A guide that misses a section, or a section the kernel cannot inspect, is identified individually. Use a cut through the resulting solid to check the shape between the input sections.

The optional `normal_option` requests that the side wall leave a section in the direction of its plane normal. For smooth unguided Lofts through three circles and through mixed profiles, nanoCAD 26.0.7476 rejected `first`, `last`, `ends` and `all` during solid creation. The same sections formed solids with the default settings. In an axial cut through the circular control, the side wall inclined about 10° from the section normal at the first end and 18° at the last. The bridge preserves the requested condition when reporting a failure.

Parametric modeling can retain native sketches, geometric relations, dimensional controls, named variables, expressions, profiles and feature history. Supported features can be repeated with native rectangular or circular patterns and mirrored within one part. The assistant can change supported drivers and check the dependent geometry.

When changing a driver, `include_downstream_history` identifies the downstream history items reported by nanoCAD. On a parameter write, `observe_dimension_handles` records selected associated sheet dimensions before and after the rebuild. Use their retained anchors, view scale and measured values to check whether the drawing follows the edited part. A missing post-edit reading is reported separately from the parameter write.

A selected native control of an eligible fixed Simple Hole can use a named
User formula. The assessed diameter change retained the formula and dependency,
changed the physical bore and updated its linked projected diameter after
reopening the DWG. This does not establish automatic diameter/depth/seat
bindings or formula support for other Hole families.

Owned sketches can contain full Splines and analytic Ellipses. Native Smooth relations can preserve endpoint tangency between a Spline and an eligible adjacent curve. This is G1 tangency within the supported route; G2 curvature continuity is not promised.

Supported extrusion, revolution and Sweep operations can use existing native profiles. Loft can consume ordered existing profile selections while retaining their sketches and controls. The demonstrated controlled-Loft scope uses circular sections; arbitrary section shapes, constraint networks and later edits require their own eligibility and result checks.

Selected profile replacement, sketch support reassignment, feature suppression/restoration and feature deletion are available within their declared dependency scope. These operations do not provide automatic repair of arbitrary broken model history.

Eligible direct-solid geometry can be replaced from another existing solid
while retaining the target identity, supported data and documented view links.
The source stays independent. Native owning cleanup is part of this route;
it does not transplant an arbitrary parametric feature history.

A physical thread can be constructed from a real section, helical path and supported sweep/cut operations. A thread feature or annotation alone does not establish physical helical geometry or compliance with a thread standard.

Thread edits can inspect retained pitch and length drivers. For a chamfer, the selected edges are checked against their face. Loft inspection returns the available native section sources.

## Editable DWG documentation

Place projected views using first-angle or third-angle projection and select the alignment of a child view. For local references, inspect the saved path and resolved file after repointing. Raster calibration can use an independent third point to report the placement error in millimetres.

Native view preview can be read and explicitly switched between its bounding
frame and full view graphics. Selected ordinary views can be refreshed while
retaining their sources, placement and supported relationships.

Explicit ordinary view subtrees, supported Linear/Curved Break2d objects and
independent non-live Plane Sections can be retired through their native owners.
All associated views must be selected when retiring a Section. Model sources
and unrelated views remain. Once supported documentation dependencies have
been removed, an eligible standalone direct source can be erased through its
native owning body and verified owned data.

Views carrying local/outer cuts and their cut sketches are read-only through
MCP, including property, refresh, rename and deletion routes. Their metadata
and dependency protection remain available.

## Point clouds

For an already loaded cloud, the assistant can read metadata and actual points, inspect stored native features, take point sections, obtain statistics and make supported measurements. The drawing insertion can be translated and rotated without changing the source points. A separate editable drawing contour can be constructed from an appropriate section.

Cloud source points and storage remain read-only. The integration does not import or load missing sources, establish scan accuracy or turn a point section into a fitted surface automatically.

## Important current limits

- **Path arrays after reopening a DWG:** a count edit can store the requested number without regenerating the visible items. The integration may also be unable to identify the original path. Check the actual instances before relying on such an edit.
- **Spline Hatch boundaries:** each Spline loop must be one genuinely closed planar Spline in the world XY plane at Z = 0. Open-Spline chains and automatic projection are outside this route. Holes must meet its separation and containment requirements.
- **Parameters:** named Shell-thickness formulas are unavailable because native updates can lose the formula and dependency; supported literal Shell edits remain. Hole formulas have the selected-control scope described above. Depth, seat and other Hole families remain unassessed.
- **Dimensions:** annotation dimensions and driving sketch dimensions differ. Display of native driving annotations outside the sketch editor is not assured in every context. General parameter links to arbitrary text, table cells and properties are unavailable.
- **Blocks and rich content:** dynamic-block authoring and universal editing of Fields, rich text and external data links are unavailable. Existing dynamic-property editing has the assessed scope above. Basic Ray/Xline support does not include every curve-editing operation.
- **DWG libraries:** import and replacement support eligible static 2D source graphs, not arbitrary DWG contents or dynamic-block authoring. Unsupported ownership, Fields and foreign relationships remain guarded.
- **Modeling scope:** persistent assembly mates, general surface modeling, additional specialist modules, printing and PDF export are outside this integration. Placing parts together does not create an assembly mate.
- **Recovery:** some failures after native work starts can leave partial changes. Check the affected drawing state before continuing from an uncertain result.

## Verification

The assistant can combine geometry reads, driver values, dependency observations and actual drawing images. Stored values alone do not prove that a model rebuilt correctly. An image does not prove every hidden dimension and does not save a DWG.

Review the result against your dimensions, manufacturing requirements and intended use. Successful operations do not establish manufacturing readiness, safety or compliance with an engineering standard. For recovery and connection problems, see [troubleshooting](troubleshooting.md).
