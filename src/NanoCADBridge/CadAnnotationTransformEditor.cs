using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Placement of ordinary native objects. Content editing and maintenance of
// external associative sources use their owning adapters, not this route.
internal static class CadAnnotationTransformEditor
{
    internal static bool Supported(Type type)=>type==typeof(Table)||type==typeof(MLeader)||
        DimensionGeometryReader.Supported(type)||type==typeof(Hatch)||type==typeof(Region)||type==typeof(Helix);
    internal static bool Supported(string? type)=>type==typeof(Table).FullName||type==typeof(MLeader).FullName||
        type==typeof(Hatch).FullName||type==typeof(Region).FullName||type==typeof(Helix).FullName||
        new[]{typeof(AlignedDimension),typeof(RotatedDimension),typeof(RadialDimension),typeof(DiametricDimension),
            typeof(LineAngularDimension2),typeof(Point3AngularDimension),typeof(OrdinateDimension),typeof(ArcDimension),typeof(RadialDimensionLarge)}.Any(t=>t.FullName==type);
    internal static bool Placement(EditSpec operation)=>operation is MoveSpec or RotateSpec or ScaleSpec or CopySpec;
    private static void Need(bool value,string message,string code="UNSUPPORTED_ANNOTATION_DEPENDENCIES")
    {if(!value)throw new DomainException(code,message);}
    private static double Number(double value,bool positive=false)
    {Need(double.IsFinite(value)&&Math.Abs(value)<=1e9&&(!positive||value>0),"Native placement geometry is nonfinite, excessive or degenerate.","INVALID_GEOMETRY");return value;}
    private static double[] Point(Point3d point)=>new[]{Number(point.X),Number(point.Y),Number(point.Z)};
    private static double[] Vector(Vector3d vector)
    {Need(vector.Length>0,"Native placement has a zero direction/normal.","INVALID_GEOMETRY");return new[]{Number(vector.X),Number(vector.Y),Number(vector.Z)};}
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static void Reference(ObjectId id)
    {Need(!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"A native annotation style/content reference is unavailable.");}
    internal static MText? OwnedText(MLeader leader)
    {
        var text=leader.MText;
        Need(text==null||text.ObjectId.IsNull,"Native MLeader text getter returned a resident alias.","UNSAFE_NATIVE_CLONE");
        return text;
    }

    private static void Dependencies(DBObject source,Transaction tr,Action cancel)
    {
        cancel();Need(!source.HasFields&&!CadDrawingGroups431.HasNonOrganizationalReactors(source,tr,cancel),"Placement requires no fields or geometric persistent dependencies; selection Group membership is retained.");
        Need(source.Annotative!=AnnotativeStates.True,"Annotative context placement requires its owning context adapter.");
        // A DimAssoc/AssocNetwork can be owned by an extension dictionary even
        // when a dimension's reactor list is empty. Read these links without
        // rejecting unrelated owned metadata that native DeepClone retains.
        var visited=new HashSet<ObjectId>();var pending=new Stack<ObjectId>();var identities=new ModelInventoryBudget(cancel);
        void Push(ObjectId id){cancel();identities.Add(Id(id));pending.Push(id);}
        if(!source.ExtensionDictionary.IsNull)Push(source.ExtensionDictionary);
        while(pending.Count>0)
        {
            cancel();var id=pending.Pop();if(!visited.Add(id))continue;Reference(id);
            var item=tr.GetObject(id,OpenMode.ForRead);
            Need(item.GetType().Name is not ("DimAssoc" or "AssocDependency" or "AssocGeomDependency" or "AssocNetwork" or "DataLink" or "Field"),
                "Native annotation has an associated source/data/field link requiring its owning adapter.");
            if(item is DBDictionary dictionary)foreach(DBDictionaryEntry entry in dictionary)Push(entry.Value);
        }
    }

    internal static void CheckSource(Entity entity,Transaction tr,Action cancel)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(entity);entity=nativeBinding.Value;
        Need(Supported(entity.GetType()),"Expected an exact ordinary native annotation, Region or Helix.","UNSUPPORTED_ENTITY_TYPE");
        Dependencies(entity,tr,cancel);
        switch(entity)
        {
            case Table table:
                using(var links=table.GetDataLink())Need(links.Count==0,"Data-linked table placement requires its owning data adapter.");
                Reference(table.TableStyle);
                for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
                {
                    cancel();Need(!table.IsLinked(row,column),"Linked table cells require their owning data adapter.");
                    for(int content=0;content<table.Cells[row,column].Contents.Count;content++)
                    {cancel();Need(table.GetContentTypes(row,column,content)!=CellContentTypes.Field&&table.GetFieldId(row,column,content).IsNull,"Field-bearing table contents require their owning content adapter.");}
                }
                break;
            case MLeader leader:
                Need(!leader.EnableAnnotationScale,"MLeader annotation-scale contexts require their owning adapter.");Reference(leader.MLeaderStyle);
                if(leader.ContentType.ToString()=="MTextContent")
                {using var text=OwnedText(leader);Need(text!=null&&!text.HasFields,"Field-bearing MLeader text requires its content adapter.");Reference(text!.TextStyleId);}
                else if(leader.ContentType.ToString()=="BlockContent")
                {
                    Reference(leader.BlockContentId);var block=(BlockTableRecord)tr.GetObject(leader.BlockContentId,OpenMode.ForRead);
                    Need(!block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference,"External MLeader block content requires its ownership adapter.");
                    foreach(ObjectId id in block)
                    {
                        cancel();if(tr.GetObject(id,OpenMode.ForRead) is not AttributeDefinition definition)continue;
                        Need(!definition.HasFields,"Field-bearing MLeader block content requires its content adapter.");
                        if(definition.Constant)continue;
                        var attribute=leader.GetBlockAttribute(id);
                        Need(attribute==null||attribute.ObjectId.IsNull,"Native MLeader attribute getter returned a resident alias.","UNSAFE_NATIVE_CLONE");
                        using(attribute)if(attribute!=null)Need(!attribute.HasFields,"Field-bearing MLeader attributes require their content adapter.");
                    }
                }
                break;
            case Dimension dimension:Reference(dimension.DimensionStyle);break;
            case Hatch hatch:
                Need(!hatch.Associative,"Associative hatch placement requires its boundary-owning route; native erase/set_properties remains available.");
                using(var links=hatch.GetAssociatedObjectIds())Need(links.Count==0,"Hatch retains associated boundary links requiring their owning route.");
                break;
        }
        CheckGeometry(entity,cancel);
    }

    internal static void CheckGeometry(Entity entity,Action cancel)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(entity);entity=nativeBinding.Value;
        cancel();Need(Supported(entity.GetType()),"Unsupported native placement type.","UNSUPPORTED_ENTITY_TYPE");
        foreach(var point in Anchors(entity,cancel).Values)Point(point);
        switch(entity)
        {
            case Table table:
                Vector(table.Normal);Vector(table.Direction);Number(table.Width,true);Number(table.Height,true);
                Need(table.Rows.Count>0&&table.Columns.Count>0,"Native table has no rows/columns.","INVALID_GEOMETRY");
                for(int row=0;row<table.Rows.Count;row++){cancel();Number(table.RowHeight(row),true);}
                for(int column=0;column<table.Columns.Count;column++){cancel();Number(table.ColumnWidth(column),true);}
                break;
            case MLeader leader:Vector(leader.Normal);Number(leader.Scale,true);break;
            case Dimension dimension:
                Vector(dimension.Normal);Point(dimension.TextPosition);Number(dimension.Elevation);Number(dimension.TextRotation);
                var definition=Wire.Element(DimensionGeometryReader.Definition(dimension));
                foreach(var member in definition.EnumerateObject())
                {cancel();if(member.Value.ValueKind==JsonValueKind.Array)foreach(var n in member.Value.EnumerateArray())Number(n.GetDouble());else if(member.Value.ValueKind==JsonValueKind.Number)Number(member.Value.GetDouble());}
                break;
            case Hatch hatch:
                Vector(hatch.Normal);Number(hatch.Elevation);Number(hatch.PatternAngle);Number(hatch.PatternScale);
                Need(hatch.NumberOfLoops>0,"Native hatch has no boundary loops.","INVALID_GEOMETRY");break;
            case Region region:
                Need(!region.IsNull&&double.IsFinite(region.Area)&&region.Area>0,"Native Region is empty or has invalid area.","INVALID_GEOMETRY");Vector(region.Normal);break;
            case Helix helix:
                Vector(helix.AxisVector);Number(helix.BaseRadius);Number(helix.TopRadius);Number(helix.Height);Number(helix.Turns,true);
                Need(helix.BaseRadius>=0&&helix.TopRadius>=0&&(helix.BaseRadius>0||helix.TopRadius>0),"Native Helix has no usable radius.","INVALID_GEOMETRY");break;
        }
        // Detached annotation clones may have no dimension/table display block.
        // Their stored definition is checked without database regeneration.
        if(!entity.ObjectId.IsNull||entity is Hatch or Region or Helix)
        {var bounds=entity.GeometricExtents;Point(bounds.MinPoint);Point(bounds.MaxPoint);}
    }

    internal static Dictionary<string,Point3d> Anchors(Entity entity,Action cancel)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(entity);entity=nativeBinding.Value;
        var result=new Dictionary<string,Point3d>();var budget=new ModelInventoryBudget(cancel);
        void Add(string name,Point3d point){cancel();var value=Point(point);budget.Add(new{name,point=value});result.Add(name,point);}
        switch(entity)
        {
            case Table table:Add("position",table.Position);break;
            case MLeader leader:
                foreach(int branch in leader.GetLeaderIndexes())foreach(int line in leader.GetLeaderLineIndexes(branch))
                {int count=leader.VerticesCount(line);Need(count>=0,"Native MLeader returned a negative vertex count.","INVALID_GEOMETRY");for(int i=0;i<count;i++)Add($"leader/{branch}/{line}/{i}",leader.GetVertex(line,i));}
                if(leader.ContentType.ToString()=="MTextContent")Add("content",leader.TextLocation);
                else if(leader.ContentType.ToString()=="BlockContent")Add("content",leader.BlockPosition);break;
            case AlignedDimension d:Add("x1",d.XLine1Point);Add("x2",d.XLine2Point);break;
            case RotatedDimension d:Add("x1",d.XLine1Point);Add("x2",d.XLine2Point);break;
            case RadialDimension d:Add("center",d.Center);Add("chord",d.ChordPoint);break;
            case DiametricDimension d:Add("chord",d.ChordPoint);Add("far_chord",d.FarChordPoint);break;
            case LineAngularDimension2 d:Add("x1_start",d.XLine1Start);Add("x1_end",d.XLine1End);Add("x2_start",d.XLine2Start);Add("x2_end",d.XLine2End);break;
            case Point3AngularDimension d:Add("center",d.CenterPoint);Add("x1",d.XLine1Point);Add("x2",d.XLine2Point);break;
            case ArcDimension d:Add("center",d.CenterPoint);Add("x1",d.XLine1Point);Add("x2",d.XLine2Point);if(d.HasLeader){Add("leader1",d.Leader1Point);Add("leader2",d.Leader2Point);}break;
            case RadialDimensionLarge d:Add("center",d.Center);Add("chord",d.ChordPoint);Add("override_center",d.OverrideCenter);Add("jog",d.JogPoint);break;
            case OrdinateDimension d:Add("origin",d.Origin);Add("definition",d.DefiningPoint);Add("leader_end",d.LeaderEndPoint);break;
            case Helix helix:Add("axis",helix.GetAxisPoint());Add("start",helix.StartPoint);break;
        }
        return result;
    }

    // Stable stored content only: scaling/orientation/layout dimensions are
    // intentional geometry changes and are not confused with lost content.
    internal static JsonElement Content(Entity entity,Action cancel,bool reflection=false)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(entity);entity=nativeBinding.Value;
        object? content=null;var budget=new ModelInventoryBudget(cancel);
        if(entity is Table table)
        {
            var cells=new List<object>();
            for(int row=0;row<table.Rows.Count;row++)for(int column=0;column<table.Columns.Count;column++)
            {
                cancel();var values=new List<object>();var valueBudget=new ModelInventoryBudget(cancel);int count=table.Cells[row,column].Contents.Count;
                for(int i=0;i<count;i++)
                {
                    cancel();var kind=table.GetContentTypes(row,column,i);
                    if(kind==CellContentTypes.Value)
                    {
                        var type=table.GetDataType(row,column,i);var value=table.GetValue(row,column,i);
                        string? stored=value switch{DateTime date=>date.ToString("O",CultureInfo.InvariantCulture),double n=>n.ToString("R",CultureInfo.InvariantCulture),float n=>n.ToString("R",CultureInfo.InvariantCulture),_=>Convert.ToString(value,CultureInfo.InvariantCulture)};
                        var item=new{kind=kind.ToString(),data_type=type.DataType.ToString(),unit_type=type.UnitType.ToString(),
                            value_type=value?.GetType().FullName,value=stored,text=table.GetTextString(row,column,i),formula=table.GetFormula(row,column,i)};
                        valueBudget.Add(item);values.Add(item);
                    }
                    else {var item=new{kind=kind.ToString(),block=kind==CellContentTypes.Block?Id(table.GetBlockTableRecordId(row,column,i)):null};valueBudget.Add(item);values.Add(item);}
                }
                var merged=CadTableEditingReader.MergedRange(table,row,column,cancel);
                var cell=new{row,column,state=(int)table.GetCellState(row,column),merged,values};budget.Add(cell);cells.Add(cell);
            }
            content=new{style=Id(table.TableStyle),rows=table.Rows.Count,columns=table.Columns.Count,cells};
        }
        else if(entity is MLeader leader)
        {
            string? text=null,stringStyle=null;if(leader.ContentType.ToString()=="MTextContent")
            {using var mt=OwnedText(leader);Need(mt!=null,"Native MLeader text is unavailable.");text=mt!.Contents;stringStyle=Id(mt.TextStyleId);}
            content=new{kind=leader.ContentType.ToString(),style=Id(leader.MLeaderStyle),text_style=stringStyle,text,
                block=leader.ContentType.ToString()=="BlockContent"?Id(leader.BlockContentId):null,line_type=leader.LeaderLineType.ToString()};
        }
        else if(entity is Dimension dimension)content=new{style=Id(dimension.DimensionStyle),text=dimension.DimensionText,default_text=dimension.UsingDefaultTextPosition,native_mode_controls=CadExtendedDimensions356.PlacementControls(dimension)};
        else if(entity is Hatch hatch)content=new{pattern=hatch.PatternName,type=hatch.PatternType.ToString(),hatch.PatternDouble,hatch.Associative,loops=hatch.NumberOfLoops};
        else if(entity is Helix helix)content=new{helix.Turns,Twist=reflection?(bool?)null:helix.Twist};
        var result=Wire.Element(new{runtime_type=entity.GetType().FullName,layer=Id(entity.LayerId),properties=EntityAdapter.StoredProperties(entity),content});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }

    internal static void Apply(Entity entity,EditSpec operation,Action cancel)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(entity);entity=nativeBinding.Value;
        Need(Placement(operation),"Only move, rotate, positive uniform scale and copy use native annotation placement.","UNSUPPORTED_ENTITY_TYPE");
        cancel();CheckGeometry(entity,cancel);var content=Content(entity,cancel);var anchors=Anchors(entity,cancel);
        var id=entity.ObjectId;var owner=entity.OwnerId;var matrix=CadModifier.Matrix(operation);
        entity.TransformBy(matrix);
        if(!entity.ObjectId.IsNull)
        {
            if(entity is Dimension dimension)dimension.RecomputeDimensionBlock(true);
            else if(entity is Table table){table.GenerateLayout();table.RecomputeTableBlock(true);}
            entity.RecordGraphicsModified(true);
        }
        cancel();CheckGeometry(entity,cancel);
        Need(entity.ObjectId==id&&entity.OwnerId==owner&&ModelParameterSnapshot.Equivalent(content,Content(entity,cancel)),
            "Native placement changed root ownership, stored content/style or display properties.","NATIVE_ANNOTATION_MISMATCH");
        var after=Anchors(entity,cancel);Need(anchors.Keys.ToHashSet().SetEquals(after.Keys),"Native placement changed defining point/leader topology.","NATIVE_ANNOTATION_MISMATCH");
        foreach(var entry in anchors)
        {
            cancel();var expected=entry.Value.TransformBy(matrix);var actual=after[entry.Key];
            double tolerance=1e-8+1e-10*Math.Max(1,expected.DistanceTo(Point3d.Origin));
            Need(expected.DistanceTo(actual)<=tolerance,"Native placement did not retain the requested transformed defining point.","NATIVE_ANNOTATION_MISMATCH");
        }
    }

    internal static Entity Copy(Entity source,CopySpec operation,ObjectId targetOwner,Transaction tr,Action cancel)
    {
        using var sourceBinding=CadNativeDimensionBinding357.Bind(source);source=sourceBinding.Value;
        CheckSource(source,tr,cancel);var original=Snapshot(source,cancel);
        using var ids=new ObjectIdCollection(new[]{source.ObjectId});using var mapping=new IdMapping();
        source.Database.DeepCloneObjects(ids,targetOwner,mapping,false);
        Need(mapping.Contains(source.ObjectId),"Native DeepClone returned no mapping for the requested root.","NATIVE_ANNOTATION_COPY_MISMATCH");
        var pair=mapping.Lookup(source.ObjectId);var id=pair.Value;Reference(id);
        Need(pair.IsCloned&&pair.Key==source.ObjectId&&id!=source.ObjectId,"Native DeepClone did not create a distinct mapped root.","NATIVE_ANNOTATION_COPY_MISMATCH");
        var openedCopy=tr.GetObject(id,OpenMode.ForWrite) as Entity;
        using var copyBinding=openedCopy==null?null:CadNativeDimensionBinding357.Bind(openedCopy);var copy=copyBinding?.Value;
        Need(copy!=null&&copy.GetType()==source.GetType()&&copy.OwnerId==targetOwner,"Native copy has a different type or requested space owner.","NATIVE_ANNOTATION_COPY_MISMATCH");
        foreach(IdPair mapped in mapping)
        {
            cancel();if(!mapped.IsCloned||mapped.Value==id)continue;
            if(tr.GetObject(mapped.Value,OpenMode.ForRead) is Entity child)
                Need(child.OwnerId!=targetOwner,"Native copy created an additional top-level entity without returned provenance.","NATIVE_ANNOTATION_COPY_MISMATCH");
        }
        // Native clone-created database objects belong to this transaction;
        // do not AppendEntity/AddNewlyCreatedDBObject or manually dispose them.
        Need(ModelParameterSnapshot.Equivalent(Content(source,cancel),Content(copy!,cancel)),"Native copy lost stored content/style or display properties.","NATIVE_ANNOTATION_COPY_MISMATCH");
        if(source is Dimension originalDimension&&CadExtendedDimensions356.Supported(source.GetType()))
        {
            Need(ModelParameterSnapshot.Equivalent(CadExtendedDimensions356.Controls(originalDimension),CadExtendedDimensions356.Controls((Dimension)copy!)),"Native clone lost retained stored extended-dimension options before placement.","NATIVE_ANNOTATION_COPY_MISMATCH");
            Need(DimensionEditingValidation341.SameDefinition(Wire.Element(DimensionGeometryReader.Definition(originalDimension)),Wire.Element(DimensionGeometryReader.Definition((Dimension)copy!))),"Native clone changed stored extended-dimension references or physical arc placement before transformation.","NATIVE_ANNOTATION_COPY_MISMATCH");
            var sourceAnchors=Anchors(source,cancel);var copiedAnchors=Anchors(copy!,cancel);
            Need(sourceAnchors.Keys.ToHashSet().SetEquals(copiedAnchors.Keys),"Native clone changed stored extended-dimension anchor/leader membership.","NATIVE_ANNOTATION_COPY_MISMATCH");
            foreach(var anchor in sourceAnchors){cancel();Need(anchor.Value.DistanceTo(copiedAnchors[anchor.Key])<=1e-8,"Native clone lost an actual original definition/active leader anchor before placement.","NATIVE_ANNOTATION_COPY_MISMATCH");}
        }
        Apply(copy!,operation,cancel);
        Need(ModelParameterSnapshot.Equivalent(original,Snapshot(source,cancel)),"Native copy changed its retained original source.","NATIVE_ANNOTATION_COPY_MISMATCH");
        return openedCopy!;
    }

    // Private post-commit comparison, without public response/count limits or
    // regeneration. Bounds/definition do not claim a full Region B-rep or an
    // arbitrary hatch curve-boundary serialization.
    internal static JsonElement Snapshot(Entity entity,Action cancel)
    {
        using var nativeBinding=CadNativeDimensionBinding357.Bind(entity);entity=nativeBinding.Value;
        CheckGeometry(entity,cancel);var anchors=Anchors(entity,cancel).ToDictionary(p=>p.Key,p=>Point(p.Value));
        object? geometry=entity switch
        {
            Table table=>new{normal=Vector(table.Normal),direction=Vector(table.Direction),table.Width,table.Height,
                rows=Enumerable.Range(0,table.Rows.Count).Select(table.RowHeight).ToArray(),columns=Enumerable.Range(0,table.Columns.Count).Select(table.ColumnWidth).ToArray(),
                scale=new[]{table.ScaleFactors.X,table.ScaleFactors.Y,table.ScaleFactors.Z}},
            Dimension dimension=>new{definition=DimensionGeometryReader.Definition(dimension),native_details=CadExtendedDimensions356.DetailSnapshot(dimension),normal=Vector(dimension.Normal),dimension.Elevation,text_position=Point(dimension.TextPosition),dimension.TextRotation},
            MLeader leader=>new{normal=Vector(leader.Normal),leader.Scale,leader.TextHeight,leader.DoglegLength,leader.LandingGap,leader.ArrowSize},
            Hatch hatch=>new{normal=Vector(hatch.Normal),hatch.Elevation,hatch.PatternAngle,hatch.PatternScale},
            Region region=>new{normal=Vector(region.Normal),region.Area},
            Helix helix=>new{axis=Vector(helix.AxisVector),helix.BaseRadius,helix.TopRadius,helix.Height,helix.Turns,helix.Twist},
            _=>null
        };
        var extents=entity.GeometricExtents;
        return Wire.Element(new{snapshot_scope="stored_native_definition_and_bounds",content=Content(entity,cancel),anchors,geometry,bounds=new{min=Point(extents.MinPoint),max=Point(extents.MaxPoint)}});
    }
}
