# Example requests

[Home](../../README.md)

These are suggested tasks for your connected assistant, not supplied DWG files or claims that every variation will succeed. Adapt the geometry and paths to your project. Start with the [user guide](user-guide.md), and check the [current limits](capabilities.md#important-current-limits) before relying on a particular relationship.

## Inspect an existing drawing

Open the intended drawing in nanoCAD, then ask:

> Show the current drawing without changing it. Report the document name and units. Summarize the visible geometry and notes, and identify the objects relevant to the mounting holes. If the target is ambiguous, show me the candidates before editing.

**Review:** the assistant is working in the intended document, recognizes its units and identifies actual objects. Nothing should have been created or edited.

## Draw a simple 2D part

Use a new drawing or identify a clear drawing area:

> In the world XY plane, draw a closed rectangular outline 60 × 40 mm with its lower-left corner at (0, 0). Add two Ø8 mm circles centered at (15, 20) and (45, 20) mm. Add annotation dimensions for the overall width and height, hole diameter and 30 mm center distance using the current dimension style. Keep the result in 2D. Show it and check the geometry and annotation readability; do not save yet.

**Review:** the dimensions agree with the actual outline and circles, the center distance is correct, and labels do not overlap. These are annotation dimensions; the request does not establish a parametric sketch.

## Make a parametric mounting plate

Use a new drawing or a working copy with sufficient empty space:

> Create one parametric rectangular plate, 80 × 50 × 6 mm. Use the lower-left corner as XY origin and positive Z for thickness. Make four Ø6 mm through-holes with centers at (10, 10), (70, 10), (70, 40) and (10, 40) mm. Keep the length, width, thickness and hole diameter editable. The hole centers must remain 10 mm from the adjacent edges when the plate size changes. Use simple profiles and retained relationships. Show the result, check the dimensions and leave it unsaved for review.

**Review:** one intended plate, four through-holes, correct positions and useful driving relationships. A pattern or symmetry relation should not be claimed from independent copies alone.

Then request a meaningful change:

> Change the plate length to 100 mm. Keep its width, thickness and hole diameter unchanged. Preserve the 10 mm edge offsets and check the dependent geometry. Show the result before saving.

**Review:** the affected holes follow their edge offsets, and unchanged dimensions remain correct. A changed stored parameter alone is insufficient.

## Revise a drawing note

First choose an existing note by its actual text, location or selection:

> In the selected native multi-arrow leader, change the note to “Deburr all edges”. Keep all arrow targets and the existing style. Place the note in the clear area above the part so it remains readable. Show the affected drawing area and leave unrelated annotations unchanged.

**Review:** all intended arrows remain connected, text is correct and readable, and the note does not overlap other content. Existing complex Fields or unsupported content may require a more specific edit.

## Build a transition from controlled circular sections

> Create a single parametric transition along Z using two circular sketch sections. The lower section is centered at (0, 0, 0) mm with radius 10 mm; the upper section is centered at (0, 0, 40) mm with radius 4 mm. Keep both section radii editable and construct the Loft from those existing native profiles. Show the body and inspect the profile relationships.

Then ask:

> Change only the upper section radius to 6 mm. Rebuild the dependent Loft, check the resulting section and body, and show the result.

**Review:** the same intended section drives the change and the dependent body remains healthy. This example does not specify wall thickness, tangency to adjacent parts or G2 continuity.

## Inspect a loaded point-cloud section

Open a drawing with the intended cloud already loaded. Establish the cloud and drawing units before choosing section dimensions:

> Inspect the loaded point cloud without changing its placement or source data. Confirm its identity, availability and the known units. Help me choose a section plane through the component I identify. If the units are millimeters, read a centered 5 mm thick point section around that plane, restricted to the component. Report the section frame, whether the scan of the requested subset completed and the limits of the measurement.

**Review:** the correct cloud is used; a complete result is distinguished from an incomplete page; thickness is interpreted in the confirmed units. Source points remain unchanged. A point slice is not a fitted surface or proof of scan accuracy.

If you need a drawing contour, add:

> From that section, propose an editable arc-and-line contour with a stated fitting accuracy. Preserve intentional corners and gaps. Explain any uncertain interpolation before creating the contour, and compare the created result with the section points.

Choose the fitting accuracy from your task and source quality. A fit tolerance does not establish manufacturing tolerance or the accuracy of unseen scan data.

## Save the reviewed result

After checking the drawing, provide an actual writable destination:

> Save this reviewed drawing to `[your full project path]\mounting-plate.dwg`. Report the saved path. Keep the current document available for further work.

Replace the bracketed text before sending the request. To update an existing DWG rather than make a copy, explicitly ask to save that existing file.

For pending operations or uncertain outcomes, follow [troubleshooting](troubleshooting.md) before repeating a write.
