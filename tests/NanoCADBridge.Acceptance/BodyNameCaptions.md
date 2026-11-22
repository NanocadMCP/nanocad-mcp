# Body captions in 3D History

Run this check in a new DWG with the 3D History panel visible. Use the full-window MCP capture and inspect the collapsed rows. Keep the physical solid handles for comparison after opening the saved DWG.

1. Create a box, cylinder and sphere with direct modeling. Create a separate parametric extrusion. Give its sketch and extrusion distinct names.
2. Submit a naming request with the parametric body first and a direct solid second. On nanoCAD 26.0.7476.4950, expect `BODY_NAME_PERSISTENCE_UNAVAILABLE`, the direct solid handle, and `outcome=not_started`. Check that each caption and each existing group label still has its original value.
3. Rename just the parametric body. Collapse its branch and verify the new caption. Expand the branch and compare the sketch and extrusion names with step 1. Save the DWG and open it again. Verify the same top-level caption at the same physical handle.
4. When assessing a new direct-solid naming implementation, give the three primitives the names `test1`, `test2` and `test3`. Inspect the panel after another modeling command. Save the drawing and open it in a fresh nanoCAD process without the candidate's naming code. All three user names must remain visible in the collapsed tree and belong to their original bodies.

A native property read, a successful body search and a stored string in the DWG can all succeed while nanoCAD displays a generated caption after opening the file.
