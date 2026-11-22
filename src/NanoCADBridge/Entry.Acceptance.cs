#if BLOCK_POSITION_PROBE || CORE_PARAMETER_PROBE || DIMENSION_PLACEMENT_PROBE || DOCUMENT_CONSTRAINTS_PROBE || DOCUMENT_DRIVEN_MODEL_PROBE || DRAWING_VIEW_FIXTURE || DRAWING_VIEW_WRITE_PROBE || HATCH_UNDO_PROBE || MULTICAD_TRANSACTION_PROBE || PARAMETRIC_SKETCH_PROBE || POINT_CLOUD_FIXTURE || SHEET_METAL_FIXTURE || SOLID_TOPOLOGY_PROBE || STATE_IMPORT_PROBE
using Teigha.Runtime;

namespace NanoCADBridge;

public sealed partial class Entry
{
#if HATCH_UNDO_PROBE
    [CommandMethod(HatchUndoProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeHatchUndo() => HatchUndoProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if BLOCK_POSITION_PROBE
    [CommandMethod(BlockPositionProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeBlockPosition() => BlockPositionProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if DIMENSION_PLACEMENT_PROBE
    [CommandMethod(DimensionPlacementProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeDimensionPlacement() => DimensionPlacementProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if CORE_PARAMETER_PROBE
    [CommandMethod(CoreParameterProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeCoreParameter() => CoreParameterProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if PARAMETRIC_SKETCH_PROBE
    [CommandMethod(ParametricSketchProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeParametricSketch() => ParametricSketchProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if DOCUMENT_CONSTRAINTS_PROBE
    [CommandMethod(DocumentConstraintsProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeDocumentConstraints() => DocumentConstraintsProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if DOCUMENT_DRIVEN_MODEL_PROBE
    [CommandMethod(DocumentDrivenModelProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeDocumentDrivenModel() => DocumentDrivenModelProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if STATE_IMPORT_PROBE
    [CommandMethod(StateImportProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeStateImport() => StateImportProbe.Execute(_current?._descriptor,_current?._documents);
#endif
#if MULTICAD_TRANSACTION_PROBE
    [CommandMethod(ModelTransactionProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeModelTransaction() => ModelTransactionProbe.Execute(_current?._descriptor);
#endif
#if DRAWING_VIEW_FIXTURE
    [CommandMethod(DrawingViewFixture.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void SeedDrawingViews() => DrawingViewFixture.Execute(_current?._descriptor);
#endif
#if DRAWING_VIEW_WRITE_PROBE
    [CommandMethod(DrawingViewWriteProbe.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ProbeDrawingViewWrite() => DrawingViewWriteProbe.Execute(_current?._descriptor);
#endif
#if SHEET_METAL_FIXTURE
    [CommandMethod(SheetMetalFixture.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void SeedSheetMetal() => SheetMetalFixture.Execute(_current?._descriptor);
#endif
#if POINT_CLOUD_FIXTURE
    [CommandMethod(PointCloudFixture.CommandName, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void SeedPointCloud() => PointCloudFixture.Execute(_current?._descriptor);
#endif
    partial void CheckAcceptancePending(ref bool pending)
    {
#if STATE_IMPORT_PROBE
        if (StateImportProbe.Pending) { pending = true; return; }
#endif
#if MULTICAD_TRANSACTION_PROBE
        if (ModelTransactionProbe.Pending) { pending = true; return; }
#endif
#if DRAWING_VIEW_FIXTURE
        if (DrawingViewFixture.Pending) { pending = true; return; }
#endif
#if DRAWING_VIEW_WRITE_PROBE
        if (DrawingViewWriteProbe.Pending) { pending = true; return; }
#endif
#if SHEET_METAL_FIXTURE
        if (SheetMetalFixture.Pending) { pending = true; return; }
#endif
#if POINT_CLOUD_FIXTURE
        if (PointCloudFixture.Pending) { pending = true; return; }
#endif
#if CORE_PARAMETER_PROBE
        if (CoreParameterProbe.Pending) { pending = true; return; }
#endif
#if PARAMETRIC_SKETCH_PROBE
        if (ParametricSketchProbe.Pending) { pending = true; return; }
#endif
#if DOCUMENT_CONSTRAINTS_PROBE
        if (DocumentConstraintsProbe.Pending) { pending = true; return; }
#endif
#if DOCUMENT_DRIVEN_MODEL_PROBE
        if (DocumentDrivenModelProbe.Pending) { pending = true; return; }
#endif
#if DIMENSION_PLACEMENT_PROBE
        if (DimensionPlacementProbe.Pending) { pending = true; return; }
#endif
#if HATCH_UNDO_PROBE
        if (HatchUndoProbe.Pending) { pending = true; return; }
#endif
#if BLOCK_POSITION_PROBE
        if (BlockPositionProbe.Pending) { pending = true; return; }
#endif
    }

    partial void PumpAcceptanceProbes()
    {
#if HATCH_UNDO_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            HatchUndoProbe.Pump(_descriptor!,_documents!);
#endif
#if BLOCK_POSITION_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            BlockPositionProbe.Pump(_descriptor!,_documents!);
#endif
#if DIMENSION_PLACEMENT_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            DimensionPlacementProbe.Pump(_descriptor!,_documents!);
#endif
#if CORE_PARAMETER_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            CoreParameterProbe.Pump(_descriptor!,_documents!);
#endif
#if PARAMETRIC_SKETCH_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            ParametricSketchProbe.Pump(_descriptor!,_documents!);
#endif
#if DOCUMENT_CONSTRAINTS_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            DocumentConstraintsProbe.Pump(_descriptor!,_documents!);
#endif
#if DOCUMENT_DRIVEN_MODEL_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            DocumentDrivenModelProbe.Pump(_descriptor!,_documents!);
#endif
#if SOLID_TOPOLOGY_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            SolidTopologyProbe.Pump(_descriptor!,_documents!);
#endif
#if STATE_IMPORT_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            StateImportProbe.Pump(_descriptor!,_documents!);
#endif
#if MULTICAD_TRANSACTION_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            ModelTransactionProbe.Pump(_descriptor!);
#endif
#if DRAWING_VIEW_FIXTURE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            DrawingViewFixture.Pump(_descriptor!);
#endif
#if DRAWING_VIEW_WRITE_PROBE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            DrawingViewWriteProbe.Pump(_descriptor!);
#endif
#if SHEET_METAL_FIXTURE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            SheetMetalFixture.Pump(_descriptor!);
#endif
#if POINT_CLOUD_FIXTURE
        if (_ready && _queue?.Count == 0 && _writeQueue?.Count == 0 && _writes?.Pending == false)
            PointCloudFixture.Pump(_descriptor!);
#endif
    }
}
#endif
