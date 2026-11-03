#pragma once

// SDK26's 3DCore.h:18 includes this omitted header. In the consumed public
// header these types occur only in an array typedef, manager pointer return,
// and const array-reference argument (3DCore.h:42,5259,9307).
// No definition, size, virtual slot or ownership rule is reconstructed.
// The parameter reader never creates/copies/dereferences either type or calls
// any of the sketch-constraint APIs using them.
struct Mcs3dSketchConstraintInfo;
struct IMcs3dSketchConstraintsManager;
