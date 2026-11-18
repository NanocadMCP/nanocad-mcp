# Capabilities and limits

[Home](../../README.md)

nanoCAD MCP connects an AI assistant to a running nanoCAD session. You describe the engineering result; the assistant reads the drawing, chooses supported native operations, makes changes and checks the result. The integration covers ordinary 2D drawings and individual 3D parts. It does not make every nanoCAD command available.

See [installation](installation.md) for requirements and connection instructions, and the [user guide](user-guide.md) for the working process.

This page describes the scope of build 459. Earlier published
packages retain the limits recorded in their release notes.


Find an imported part by its DWG label, move it or drill a blind hole, then find the same part again by name. Choose a solid edge for an editable fillet or chamfer and frame it to inspect its profile. Attach a dimension to two projected hole centers and read the updated spacing on the saved drawing. On a sheet with protected native dimension containers, current_drawing stops before cloning. Save the DWG and use saved_drawing to see its dimensions. On a floor plan linked by a relative path, both drawing sources showed the raster flipped vertically and reported an orientation warning. Before tracing or measuring a raster, check its original pixels and placement in the drawing.

## Working with named parts and drawing revisions

Eligible modeled solids have an editable name on their native modeling-tree owner. A name search returns separate object references for duplicate labels. A bushing and a pin retained their names after a bore edit and DWG reopening. The operation also accepted a direct box with an existing native body owner. An eligible imported ACIS solid uses a single-member native DWG group. For example, the label `Inspection block 10 x 20 x 30` produced the group `INSPECTION_BLOCK_10_X_20_X_30` and retained the exact text in its Description after reopening. You can inspect or edit that description in nanoCAD's Group Manager (`CLASSICGROUP`). Read `body_name_group` for the actual group key, including any suffix needed to distinguish duplicate names. `body_name_source` and `native_tree_name` distinguish the group label from the construction-tree name.

A labeled 10 × 20 × 30 mm block moved 3 mm along X, from 100–110 mm to 103–113 mm. On another copy, a 2 mm diameter blind hole cut 3 mm into the top face and left 27 mm of material under its floor. Both edits kept the original solid, group and label. The neighboring solid stayed in place.

On a separate copy, an R1 edge fillet produced a cylindrical face. Rotating, scaling, moving and recoloring the finished block preserved its label. Subtracting a cylinder from another copy kept the cutting tool available for reuse. Pulling one side face of that block outward by 1 mm added 600 mm³ while retaining its name and group.

Native group membership is checked during in-place edits. Direct translation of a parametric guide bushing was refused before changing the sketch, extrusion or hole that drives it.

For a selected part, the assistant can filter planar and cylindrical faces by geometry, follow verified native feature links and inspect the inputs of a single feature. It can also inspect a planar section and the material intercepted by a finite segment. In a mounting plate, a length edit from 80 to 100 mm moved the two far holes by 20 mm and kept the four end offsets at 10 mm.

After selecting a face, the assistant can read its boundary edges, limit the results to straight edges, full circles or circular arcs, and use the returned native references for supported part features. On the guide bushing end face, the two circles measured 12.5 and 20 mm in radius. A face belonging to the neighboring pin could not be used as a boundary of the bushing.

A locating plate demonstrates the difference between dependent and independent dimensions. Increasing the pocket depth from 3 to 5 mm left a measured 5 mm wall and updated the section dimension. Increasing the hole spacing from 50 to 60 mm changed the projected distance between the nearest rims from 44 to 54 mm. The independent center-spacing annotation needed its defining points adjusted.

An upstream parameter can change the length of a part with a through hole. Lengthening the guide bushing from 40 to 44 mm kept the Ø24 passage open along the axis and updated the existing sheet to 44 mm overall with a 10 mm shoulder. An eligible existing Simple Fixed through hole can also be reapplied to repair an incomplete cut while retaining the Hole feature.

A native concentric Hole can follow a selected boss. Two Ø16 bosses had a 34 mm center spacing. The second carried a Ø6 blind hole, 5 mm deep. After DWG reopening, increasing the spacing to 46 mm moved the hole axis from X=48 to X=60 mm. The first boss remained solid at X=14 mm.

An existing associative diameter label can be repositioned with an effective DIMTMOVE setting of 2. Moving the label from X=45 to X=52 mm retained its native link to the circle, both defining chord points and the measured diameter of 12 mm.

On a sheet, the assistant can check the bounds of a proposed orthographic view against the current views, dimensions and other measured paper content. The report identifies measured conflicts and available shifts along the alignment axis. Text and arrow bounds depend on the graphics nanoCAD exposes. For loaded scans, the assistant can compare nearby observed points with a selected solid section. Each sampled boundary portion reports its deviation and point evidence. Gaps in coverage remain explicit.

For an eligible raster attachment, the assistant can read the native transformation from image pixels to drawing coordinates. The origin and the two pixel-step vectors describe physical scale and orientation. On an 1800 × 1485 image whose pixel Y axis pointed toward negative drawing Y, this mapping reproduced both corners of the saved clip.

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

Snaps resolve observed points once; a retained dimension relationship requires
an actual association. Native projected radius, circular-arc length and arc-angle
dimensions can follow selected visible circular sources in ordinary auto-updating
1:1 Paper views of one local body. Arc length and angle require a finite circular
arc; radius can use a full circle or a finite arc. The existing projected aligned and diameter
routes keep their own supported contexts. The projected diameter on a 1:2 view retained its physical value and followed a model diameter edit. Angular dimensions can use two converging visible segments. A marker identifies the solid edge only when nanoCAD supplies a verifiable source reference. Native stored view-anchor reads are
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

Parametric modeling can retain native sketches, geometric relations, dimensional controls, named variables, expressions, profiles and feature history. Supported features can be repeated with native rectangular or circular patterns and mirrored within one part. The assistant can change supported drivers and check the dependent geometry.

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
