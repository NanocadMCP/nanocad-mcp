# Capabilities and limits

[Home](../../README.md)

nanoCAD MCP connects an AI assistant to a running nanoCAD session. You describe the engineering result; the assistant reads the drawing, chooses supported native operations, makes changes and checks the result. The integration covers ordinary 2D drawings and individual 3D parts. It does not make every nanoCAD command available.

See [installation](installation.md) for requirements and connection instructions, and the [user guide](user-guide.md) for the working process.

## Drawings

| Area | Available work |
|---|---|
| Find and inspect | Search by object type, text, location or selection; read geometry, properties, drawing structure and supported relationships. |
| Curves and contours | Create and edit Points, Lines, Circles, Arcs, Ellipses, Polylines and Splines within each operation's supported plane and ownership. Spatial Splines and 3D Polylines are available. Smooth Polylines can use actual tangent arcs and straight segments. |
| Drafting edits | Supported move, rotation, copying, mirroring, scaling, offset, trim, split, extension, fillet and chamfer operations. Availability depends on the object and its relationships. |
| Repetition | Rectangular, circular and path drawing arrays; block grids; independent copies. Arrays and copies have different editing behavior. |
| Text and notes | Native TEXT/MTEXT, dimensions, leaders and multi-arrow MLeaders; supported formatting, alignment, placement and editing. |
| Tables and symbols | Native Tables with supported values, formulas and layout edits; static blocks and attributes; selected shared style edits. |
| Fills | Hatch and Region creation and editing, including supported holes and associative boundary links. |
| DWG presentation | Supported drawing views, sections, layouts and viewports; ordinary local raster and DWG references. The editable deliverable is a DWG. |

Existing-object edits aim to preserve useful source geometry, identities and supported dependencies. Objects with foreign ownership, opaque custom data or unsupported relationships can require a different route or be refused. Read access does not guarantee edit access.

## Individual 3D parts

Direct modeling includes supported primitives, extrusion, revolution, Sweep, Loft, Boolean operations, splitting, fillets, chamfers, shells and selected face edits.

Parametric modeling can retain native sketches, geometric relations, dimensional controls, named variables, expressions, profiles and feature history. Supported features can be repeated with native rectangular or circular patterns and mirrored within one part. The assistant can change supported drivers and check the dependent geometry.

Owned sketches can contain full Splines and analytic Ellipses. Native Smooth relations can preserve endpoint tangency between a Spline and an eligible adjacent curve. This is G1 tangency within the supported route; G2 curvature continuity is not promised.

Supported extrusion, revolution and Sweep operations can use existing native profiles. Loft can consume ordered existing profile selections while retaining their sketches and controls. The demonstrated controlled-Loft scope uses circular sections; arbitrary section shapes, constraint networks and later edits require their own eligibility and result checks.

Selected profile replacement, sketch support reassignment, feature suppression/restoration and feature deletion are available within their declared dependency scope. These operations do not provide automatic repair of arbitrary broken model history.

A physical thread can be constructed from a real section, helical path and supported sweep/cut operations. A thread feature or annotation alone does not establish physical helical geometry or compliance with a thread standard.

## Point clouds

For an already loaded cloud, the assistant can read metadata and actual points, inspect stored native features, take point sections, obtain statistics and make supported measurements. The drawing insertion can be translated and rotated without changing the source points. A separate editable drawing contour can be constructed from an appropriate section.

Cloud source points and storage remain read-only. The integration does not import or load missing sources, establish scan accuracy or turn a point section into a fitted surface automatically.

## Important current limits

- **Path arrays after reopening a DWG:** a count edit can store the requested number without regenerating the visible items. The integration may also be unable to identify the original path. Check the actual instances before relying on such an edit.
- **Spline Hatch boundaries:** each Spline loop must be one genuinely closed planar Spline in the world XY plane at Z = 0. Open-Spline chains and automatic projection are outside this route. Holes must meet its separation and containment requirements.
- **Parameters:** persistent named-variable formulas for Shell thickness are unavailable. Exact named Hole diameter, depth and seat bindings are unavailable; supported literal edits remain. Suitable controlled sketches and cuts can express some such designs using a different feature representation.
- **Dimensions:** annotation dimensions and driving sketch dimensions differ. Display of native driving annotations outside the sketch editor is not assured in every context. General parameter links to arbitrary text, table cells and properties are unavailable.
- **Blocks and rich content:** there is no general dynamic-block editor or universal editor for Fields, rich text and external data links. Basic Ray/Xline support does not include every curve-editing operation.
- **Modeling scope:** persistent assembly mates, general surface modeling, additional specialist modules, printing and PDF export are outside this integration. Placing parts together does not create an assembly mate.
- **Recovery:** operations do not provide a universal Undo or rollback guarantee. An uncertain result must be reconciled with the actual drawing before continuing.

## Verification

The assistant can combine geometry reads, driver values, dependency observations and actual drawing images. Stored values alone do not prove that a model rebuilt correctly. An image does not prove every hidden dimension and does not save a DWG.

Review the result against your dimensions, manufacturing requirements and intended use. Successful operations do not establish manufacturing readiness, safety or compliance with an engineering standard. For recovery and connection problems, see [troubleshooting](troubleshooting.md).
