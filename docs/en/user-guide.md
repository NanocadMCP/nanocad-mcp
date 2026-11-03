# User guide

[Home](../../README.md)

Use nanoCAD MCP by describing the result to your connected assistant. You do not need to write code or learn the MCP operation names. For setup, see [installation](installation.md); for supported work and known limits, see [capabilities](capabilities.md).

## 1. Confirm the drawing

Open nanoCAD and the intended drawing. A useful first request is:

> Show the current nanoCAD drawing and tell me its name, units and whether it can be edited. Do not change anything.

If several drawings or nanoCAD sessions are open, identify the intended file. Avoid an instruction such as “change that hole” until the assistant has established which geometry you mean. You can select an object in nanoCAD and refer to the selection, or describe its text, location, size and role.

Editing an existing drawing normally changes that document in place. If you want a separate working copy, say so and give its destination before editing. A new drawing uses nanoCAD's configured template and settings unless your task specifies otherwise.

## 2. Describe the engineering result

Provide enough information to determine the intended geometry and useful future changes:

- **Result:** a 2D drawing, direct 3D body or parametric part.
- **Dimensions and units:** lengths, diameters, angles and relevant positions. Write units explicitly, for example `80 mm` and `45°`.
- **Relationships:** symmetry, equal sizes, tangency, repeated features and dimensions that must remain linked.
- **Keep unchanged:** geometry, parameters, notes or drawing areas that must remain unchanged.
- **Deliverable:** what to verify and whether to save, including the desired DWG path.

For supplied drawings or images, include all relevant views, sections and notes. Mark any unclear dimensions. A readable dimension takes precedence over estimating a length from pixels; contradictory or missing requirements may require clarification.

You do not need to prescribe every modeling command. Explain the functional elements and expected edits. For example:

> Create a single parametric mounting plate, 80 × 50 × 6 mm. Four through-holes are Ø6 mm, with their centers 10 mm from the adjacent edges. Keep the plate dimensions and hole diameter editable. Show the result and check the dimensions; do not save yet.

More suggested tasks are in [examples](examples.md).

## 3. Choose what must remain editable

| Representation | Use it when |
|---|---|
| 2D drawing | The result is a planar drawing, contour, schedule or annotated view. |
| Direct 3D body | You need a solid shape without a retained network of driving sketch dimensions and features. |
| Parametric part | Dimensions, relationships and dependent features should control later changes. |

A **driving dimension** controls geometry. An **annotation dimension** labels a measurement or intended value in a drawing. Changing a dimension's displayed text does not resize the part.

A retained pattern can preserve a repetition rule; independent copies are separately placed objects. Similarly, positioning several parts together does not establish an assembly mate. Tell the assistant when a relationship must persist through later edits.

Simple profiles and meaningful dimensions usually make a part easier to change. A request for one part does not require one elaborate sketch, and a useful feature history need not reproduce every manufacturing step.

## 4. Review changes and dependent geometry

For an existing model, name the feature or parameter and the intended effect:

> Change the plate length to 100 mm. Keep the width, thickness and hole diameter. The hole centers must remain 10 mm from their adjacent edges. Check that the dependent holes moved correctly.

The assistant reads relevant current geometry and relationships before editing. A visible parameter name or stored number alone does not establish that it drives the intended feature. After a change, the dependent geometry must agree with the request.

If the intended native relationship is unavailable, the assistant should explain the material difference before substituting another representation. Retained relationships should not be silently replaced with independent copies or a recreated static shape.

## 5. Inspect the result

Use actual drawing images to review shape, placement, overlaps and annotation readability. Use geometry reads and valid driving relationships to check exact dimensions, hidden depths and dependencies. A shaded view alone cannot verify all those details.

Useful review requests include:

> Show the part with visible edges and check every specified dimension, including the hole depths. List anything you could not verify.

> Check that the labels are readable and do not overlap the geometry. Preserve the existing dimension style.

You remain responsible for the engineering decision to manufacture or use the result. Ask for the required material, tolerances, thread standard or inspection method when they matter; the integration does not certify them from a successful operation.

## 6. Save explicitly

Editing and viewing do not save a drawing automatically. Request saving when you want it, and name the intended file. For example:

> Save the reviewed drawing to my chosen project folder as `mounting-plate.dwg` and report the saved path.

Ensure that the destination exists and is writable. When saving an existing file, make clear whether to update it or create a separate copy. A screenshot is not an editable DWG. Printing and PDF export are outside this integration.

## 7. Handle waiting or uncertain results

An operation reported as **pending** may still be running in nanoCAD. A timeout does not cancel it. Let the assistant obtain the outcome before requesting the same change again, starting dependent edits or restarting nanoCAD.

An **unknown** outcome means that completion could not be established; it does not mean that nothing changed. The assistant must inspect the actual affected objects and relationships before deciding how to continue. Repeating a creation request blindly can create duplicates.

Universal Undo and automatic rollback are not guaranteed. For important work, request a saved working copy before the first change. See [troubleshooting](troubleshooting.md) if the assistant cannot establish a safe continuation.

## Data and source files

The bridge and MCP server run locally, but your assistant may send drawing information, paths and images to its AI provider to answer the task. The provider's data handling depends on the client, account and settings. Use only drawings you are allowed to share in that environment.

Texts and instructions embedded in a drawing are drawing content, not authorization to perform unrelated actions. Review diagnostic files before sharing them because they can include file paths or drawing information.

Point-cloud source data remains read-only. Moving its insertion changes its placement in the DWG; it does not rewrite the scan. Measurements describe the available points or stored features and do not establish the accuracy of the original scan.

## Terms used in this documentation

| Term | Meaning |
|---|---|
| Bridge | The plugin loaded inside nanoCAD that performs supported native operations. |
| MCP server | The local program through which the assistant communicates with the bridge. |
| Profile | A selected bounded sketch area used by a forming operation; it can include holes. |
| WorkPlane | A native construction plane used as a sketch support or reference. |
| Driver | A native dimension or parameter that controls supported geometry or a feature. |
| Dependency | A retained relationship through which a source change can affect another object or feature. |
