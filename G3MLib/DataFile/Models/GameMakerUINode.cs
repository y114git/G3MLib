using System;
using static G3MLib.DataFile.Models.IGameMakerFlexProperties;

namespace G3MLib.DataFile.Models;

/// <summary>
/// Interface for all UI layer node data.
/// </summary>
public interface IGameMakerUINodeData : GameMakerObject
{
}

/// <summary>
/// Structure containing a UI node.
/// </summary>
/// <remarks>
/// This abstraction is required to work with UI node polymorphism correctly.
/// </remarks>
public class GameMakerUINode : GameMakerObject
{
    /// <summary>
    /// Inner UI node for the node structure.
    /// </summary>
    public IGameMakerUINodeData Node { get; set; }

    /// <inheritdoc/>
    public void Serialize(GameMakerWriter writer)
    {
        // Write type discriminator
        writer.Write(Node switch
        {
            GameMakerUILayer => 0,
            GameMakerUIFlexPanel => 1,
            GameMakerUIGameObject => 3,
            GameMakerUISequenceInstance => 4,
            GameMakerUISpriteInstance => 5,
            GameMakerUITextItemInstance => 6,
            GameMakerUIEffectLayer => 7,
            _ => throw new InvalidOperationException($"Unknown UI node type {Node.GetType().Name}")
        });

        // Write pointer to node data
        writer.WriteGameMakerObjectPointer(Node);

        // Write list of children, if required
        if (Node is GameMakerUIContainer container)
        {
            container.Children.Serialize(writer);
        }
        else
        {
            writer.Write(0);
        }

        // Write the rest of the node data
        writer.WriteGameMakerObject(Node);
    }

    /// <inheritdoc/>
    public void Unserialize(GameMakerReader reader)
    {
        // Handle type discriminator
        int typeId = reader.ReadInt32();
        Node = typeId switch
        {
            0 => new GameMakerUILayer(),
            1 => new GameMakerUIFlexPanel(),
            3 => new GameMakerUIGameObject(),
            4 => new GameMakerUISequenceInstance(),
            5 => new GameMakerUISpriteInstance(),
            6 => new GameMakerUITextItemInstance(),
            7 => new GameMakerUIEffectLayer(),
            _ => throw new GameMakerSerializationException($"Unknown node type ID {typeId}")
        };

        // Read in pointer to node data
        uint dataPtr = reader.ReadUInt32();

        // Read list of children, if required
        if (Node is GameMakerUIContainer container)
        {
            container.Children.Unserialize(reader);
        }
        else
        {
            int alwaysZero = reader.ReadInt32();
            if (alwaysZero != 0)
            {
                throw new GameMakerSerializationException("Expected 0 while reading UI node child count");
            }
        }

        // Unserialize the rest of the node data
        if (dataPtr != reader.AbsPosition)
        {
            reader.SubmitWarning($"Reading misaligned while reading UI node at {reader.AbsPosition:X8}, realigning to {dataPtr:X8}");
            reader.AbsPosition = dataPtr;
        }
        Node.Unserialize(reader);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        uint count = 0;

        // Use type discriminator
        int typeId = reader.ReadInt32();
        reader.ReadInt32(); // skip data pointer

        if (typeId == 0 || typeId == 1)
        {
            // Add children objects to count
            count += GameMakerPointerList<GameMakerUINode>.UnserializeChildObjectCount(reader);
        }
        else
        {
            // Skip past empty list
            reader.ReadInt32();
        }

        // Process each type
        count += typeId switch
        {
            0 => GameMakerUILayer.UnserializeChildObjectCount(reader),
            1 => GameMakerUIFlexPanel.UnserializeChildObjectCount(reader),
            3 => GameMakerUIGameObject.UnserializeChildObjectCount(reader),
            4 => GameMakerUISequenceInstance.UnserializeChildObjectCount(reader),
            5 => GameMakerUISpriteInstance.UnserializeChildObjectCount(reader),
            6 => GameMakerUITextItemInstance.UnserializeChildObjectCount(reader),
            7 => GameMakerUIEffectLayer.UnserializeChildObjectCount(reader),
            _ => throw new GameMakerSerializationException($"Unknown node type ID {typeId}")
        };

        return count;
    }
}

/// <summary>
/// Root structure containing a root-level UI node.
/// </summary>
public class GameMakerUIRootNode : GameMakerUINode, GameMakerNamedResource
{
    /// <inheritdoc/>
    public GameMakerString Name
    {
        get => Node is GameMakerNamedResource named ? named.Name : null;
        set
        {
            if (Node is GameMakerNamedResource named)
            {
                named.Name = value;
            }
        }
    }
}

/// <summary>
/// Represents a value used for UI flex nodes; a float corresponding with its unit.
/// </summary>
public readonly struct GameMakerFlexValue
{
    /// <summary>
    /// Units to be used for flex values.
    /// </summary>
    public enum UnitKind : int
    {
        Undefined = 0,
        Point = 1,
        Percent = 2,
        Auto = 3
    }

    /// <summary>
    /// Floating point value.
    /// </summary>
    public float Value { get; init; }

    /// <summary>
    /// Unit for the floating point value.
    /// </summary>
    public UnitKind Unit { get; init; }

    /// <summary>
    /// Initializes a flex value.
    /// </summary>
    public GameMakerFlexValue(float value, UnitKind unit)
    {
        Value = value;
        Unit = unit;
    }

    /// <summary>
    /// Reads a flex value from the given reader.
    /// </summary>
    /// <param name="reader"></param>
    public GameMakerFlexValue(GameMakerReader reader)
    {
        Value = reader.ReadSingle();
        Unit = (UnitKind)reader.ReadInt32();
    }

    /// <summary>
    /// Serializes a flex value to the given writer.
    /// </summary>
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(Value);
        writer.Write((int)Unit);
    }
}

/// <summary>
/// Interface for UI layout nodes that have flex properties.
/// </summary>
public interface IGameMakerFlexProperties
{
    /// <summary>
    /// Alignment types that can be used for aligning content in a flexbox.
    /// </summary>
    public enum AlignmentKind : int
    {
        Auto = 0,
        FlexStart = 1,
        Center = 2,
        FlexEnd = 3,
        Stretch = 4,
        Baseline = 5,
        SpaceBetween = 6,
        SpaceAround = 7,
        SpaceEvenly = 8
    }

    /// <summary>
    /// Flex direction types that can be used in a flexbox.
    /// </summary>
    public enum FlexDirectionKind : int
    {
        Column = 0,
        ColumnReverse = 1,
        Row = 2,
        RowReverse = 3
    }

    /// <summary>
    /// Wrap types that can be used in a flexbox.
    /// </summary>
    public enum WrapKind : int
    {
        NoWrap = 0,
        Wrap = 1,
        WrapReverse = 2
    }

    /// <summary>
    /// Justification types that can be used in a flexbox.
    /// </summary>
    public enum JustifyKind : int
    {
        FlexStart = 0,
        Center = 1,
        FlexEnd = 2,
        SpaceBetween = 3,
        SpaceAround = 4,
        SpaceEvenly = 5
    }

    /// <summary>
    /// Layout direction types that can be used in a flexbox.
    /// </summary>
    public enum LayoutDirectionKind : int
    {
        Inherit = 0,
        LTR = 1,
        RTL = 2
    }

    /// <summary>
    /// Alignment of child items within the container (along cross axis).
    /// </summary>
    public AlignmentKind AlignItems { get; set; }

    /// <summary>
    /// Main direction that child items are laid out (affects direction of other properties).
    /// </summary>
    public FlexDirectionKind FlexDirection { get; set; }

    /// <summary>
    /// Wrapping type for the flexbox (when items exceed the bounds).
    /// </summary>
    public WrapKind FlexWrap { get; set; }

    /// <summary>
    /// Alignment of lines within the container (along cross axis), when items are wrapped to multiple lines.
    /// </summary>
    public AlignmentKind AlignContent { get; set; }

    /// <summary>
    /// Gap between rows of items.
    /// </summary>
    public float GapRow { get; set; }

    /// <summary>
    /// Gap between columns of items.
    /// </summary>
    public float GapColumn { get; set; }

    /// <summary>
    /// Left padding of the container.
    /// </summary>
    public GameMakerFlexValue PaddingLeft { get; set; }

    /// <summary>
    /// Right padding of the container.
    /// </summary>
    public GameMakerFlexValue PaddingRight { get; set; }

    /// <summary>
    /// Top padding of the container.
    /// </summary>
    public GameMakerFlexValue PaddingTop { get; set; }

    /// <summary>
    /// Bottom padding of the container.
    /// </summary>
    public GameMakerFlexValue PaddingBottom { get; set; }

    /// <summary>
    /// Alignment of child items within the container (along main axis, i.e. main flex direction).
    /// </summary>
    public JustifyKind JustifyContent { get; set; }

    /// <summary>
    /// Layout direction of the container.
    /// </summary>
    public LayoutDirectionKind LayoutDirection { get; set; }

    /// <summary>
    /// Serializes flex properties to the given writer.
    /// </summary>
    public static void Serialize(GameMakerWriter writer, IGameMakerFlexProperties props)
    {
        writer.Write((int)props.AlignItems);
        writer.Write((int)props.FlexDirection);
        writer.Write((int)props.FlexWrap);
        writer.Write((int)props.AlignContent);
        writer.Write(props.GapRow);
        writer.Write(props.GapColumn);
        props.PaddingLeft.Serialize(writer);
        props.PaddingRight.Serialize(writer);
        props.PaddingTop.Serialize(writer);
        props.PaddingBottom.Serialize(writer);
        writer.Write((int)props.JustifyContent);
        writer.Write((int)props.LayoutDirection);
    }

    /// <summary>
    /// Unserializes flex properties from the given reader.
    /// </summary>
    public static void Unserialize(GameMakerReader reader, IGameMakerFlexProperties props)
    {
        props.AlignItems = (AlignmentKind)reader.ReadInt32();
        props.FlexDirection = (FlexDirectionKind)reader.ReadInt32();
        props.FlexWrap = (WrapKind)reader.ReadInt32();
        props.AlignContent = (AlignmentKind)reader.ReadInt32();
        props.GapRow = reader.ReadSingle();
        props.GapColumn = reader.ReadSingle();
        props.PaddingLeft = new(reader);
        props.PaddingRight = new(reader);
        props.PaddingTop = new(reader);
        props.PaddingBottom = new(reader);
        props.JustifyContent = (JustifyKind)reader.ReadInt32();
        props.LayoutDirection = (LayoutDirectionKind)reader.ReadInt32();
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 16 * 4;
        return 0;
    }
}

/// <summary>
/// Interface for UI node instances that have flex properties.
/// </summary>
public interface IGameMakerFlexInstanceProperties
{
    /// <summary>
    /// Whether the instance is visible.
    /// </summary>
    public bool Visible { get; set; }

    /// <summary>
    /// Anchor of the instance's (0, 0) position, based on container.
    /// </summary>
    public int Anchor { get; set; }

    /// <summary>
    /// Whether the width of the instance is stretched to fit its container.
    /// </summary>
    public bool StretchWidth { get; set; }

    /// <summary>
    /// Whether the height of the instance is stretched to fit its container.
    /// </summary>
    public bool StretchHeight { get; set; }

    /// <summary>
    /// Whether the instance is tiled horizontally.
    /// </summary>
    public bool TileH { get; set; }

    /// <summary>
    /// Whether the instance is tiled vertically.
    /// </summary>
    public bool TileV { get; set; }

    /// <summary>
    /// Whether aspect ratio should be maintained when stretching width/height.
    /// </summary>
    public bool KeepAspect { get; set; }

    /// <summary>
    /// Serializes flex instance properties to the given writer.
    /// </summary>
    public static void Serialize(GameMakerWriter writer, IGameMakerFlexInstanceProperties props)
    {
        writer.Write(props.Visible);
        writer.Write(props.Anchor);
        writer.Write(props.StretchWidth);
        writer.Write(props.StretchHeight);
        writer.Write(props.TileH);
        writer.Write(props.TileV);
        writer.Write(props.KeepAspect);
    }

    /// <summary>
    /// Unserializes flex instance properties from the given reader.
    /// </summary>
    public static void Unserialize(GameMakerReader reader, IGameMakerFlexInstanceProperties props)
    {
        props.Visible = reader.ReadBoolean();
        props.Anchor = reader.ReadInt32();
        props.StretchWidth = reader.ReadBoolean();
        props.StretchHeight = reader.ReadBoolean();
        props.TileH = reader.ReadBoolean();
        props.TileV = reader.ReadBoolean();
        props.KeepAspect = reader.ReadBoolean();
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 7 * 4;
        return 0;
    }
}

/// <summary>
/// Base class of all UI container nodes.
/// </summary>
public abstract class GameMakerUIContainer : IGameMakerUINodeData, IGameMakerFlexProperties
{
    /// <summary>
    /// Child nodes of the container.
    /// </summary>
    public GameMakerPointerList<GameMakerUINode> Children { get; set; } = new();

    /// <inheritdoc/>
    public AlignmentKind AlignItems { get; set; }

    /// <inheritdoc/>
    public FlexDirectionKind FlexDirection { get; set; }

    /// <inheritdoc/>
    public WrapKind FlexWrap { get; set; }

    /// <inheritdoc/>
    public AlignmentKind AlignContent { get; set; }

    /// <inheritdoc/>
    public float GapRow { get; set; }

    /// <inheritdoc/>
    public float GapColumn { get; set; }

    /// <inheritdoc/>
    public GameMakerFlexValue PaddingLeft { get; set; }

    /// <inheritdoc/>
    public GameMakerFlexValue PaddingRight { get; set; }

    /// <inheritdoc/>
    public GameMakerFlexValue PaddingTop { get; set; }

    /// <inheritdoc/>
    public GameMakerFlexValue PaddingBottom { get; set; }

    /// <inheritdoc/>
    public JustifyKind JustifyContent { get; set; }

    /// <inheritdoc/>
    public LayoutDirectionKind LayoutDirection { get; set; }

    /// <inheritdoc/>
    public abstract void Serialize(GameMakerWriter writer);

    /// <inheritdoc/>
    public abstract void Unserialize(GameMakerReader reader);
}

/// <summary>
/// Represents a UI layer node.
/// </summary>
public class GameMakerUILayer : GameMakerUIContainer, GameMakerNamedResource
{
    /// <summary>
    /// Coordinate spaces used for drawing UI layers.
    /// </summary>
    public enum DrawSpaceKind
    {
        GUI = 1,
        View = 2
    }

    /// <summary>
    /// Name of the UI layer node.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// Coordinate space used for drawing the UI layer node.
    /// </summary>
    public DrawSpaceKind DrawSpace { get; set; }

    /// <summary>
    /// Whether the UI layer node is visible.
    /// </summary>
    public bool Visible { get; set; }

    /// <inheritdoc/>
    public override void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        writer.Write((int)DrawSpace);
        writer.Write(Visible);
        IGameMakerFlexProperties.Serialize(writer, this);
    }

    /// <inheritdoc/>
    public override void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        DrawSpace = (DrawSpaceKind)reader.ReadInt32();
        Visible = reader.ReadBoolean();
        IGameMakerFlexProperties.Unserialize(reader, this);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 4 * 3;
        return IGameMakerFlexProperties.UnserializeChildObjectCount(reader);
    }
}

/// <summary>
/// Represents a UI flex panel node.
/// </summary>
public class GameMakerUIFlexPanel : GameMakerUIContainer
{
    /// <summary>
    /// Position types for flex panels.
    /// </summary>
    public enum PositionTypeKind
    {
        Static = 0,
        Relative = 1,
        Absolute = 2
    }

    /// <summary>
    /// Name of the UI flex panel node.
    /// </summary>
    public GameMakerString Name { get; set; }

    /// <summary>
    /// Width of the flex panel.
    /// </summary>
    public GameMakerFlexValue Width { get; set; }

    /// <summary>
    /// Height of the flex panel.
    /// </summary>
    public GameMakerFlexValue Height { get; set; }

    /// <summary>
    /// Minimum width of the flex panel.
    /// </summary>
    public GameMakerFlexValue MinimumWidth { get; set; }

    /// <summary>
    /// Minimum height of the flex panel.
    /// </summary>
    public GameMakerFlexValue MinimumHeight { get; set; }

    /// <summary>
    /// Maximum width of the flex panel.
    /// </summary>
    public GameMakerFlexValue MaximumWidth { get; set; }

    /// <summary>
    /// Maximum height of the flex panel.
    /// </summary>
    public GameMakerFlexValue MaximumHeight { get; set; }

    /// <summary>
    /// Left offset of the flex panel.
    /// </summary>
    public GameMakerFlexValue OffsetLeft { get; set; }

    /// <summary>
    /// Right offset of the flex panel.
    /// </summary>
    public GameMakerFlexValue OffsetRight { get; set; }

    /// <summary>
    /// Top offset of the flex panel.
    /// </summary>
    public GameMakerFlexValue OffsetTop { get; set; }

    /// <summary>
    /// Bottom offset of the flex panel.
    /// </summary>
    public GameMakerFlexValue OffsetBottom { get; set; }

    /// <summary>
    /// Whether the flex panel clips its contents.
    /// </summary>
    public bool ClipsContents { get; set; }

    /// <summary>
    /// Position type of the flex panel.
    /// </summary>
    public PositionTypeKind PositionType { get; set; }

    /// <summary>
    /// How the flex panel aligns itself.
    /// </summary>
    public AlignmentKind AlignSelf { get; set; }

    /// <summary>
    /// Left margin of the flex panel.
    /// </summary>
    public GameMakerFlexValue MarginLeft { get; set; }

    /// <summary>
    /// Right margin of the flex panel.
    /// </summary>
    public GameMakerFlexValue MarginRight { get; set; }

    /// <summary>
    /// Top margin of the flex panel.
    /// </summary>
    public GameMakerFlexValue MarginTop { get; set; }

    /// <summary>
    /// Bottom margin of the flex panel.
    /// </summary>
    public GameMakerFlexValue MarginBottom { get; set; }

    /// <summary>
    /// Flex grow property of the flex panel.
    /// </summary>
    public float FlexGrow { get; set; }

    /// <summary>
    /// Flex shrink property of the flex panel.
    /// </summary>
    public float FlexShrink { get; set; }

    /// <inheritdoc/>
    public override void Serialize(GameMakerWriter writer)
    {
        writer.WriteGameMakerString(Name);
        Width.Serialize(writer);
        Height.Serialize(writer);
        MinimumWidth.Serialize(writer);
        MinimumHeight.Serialize(writer);
        MaximumWidth.Serialize(writer);
        MaximumHeight.Serialize(writer);
        OffsetLeft.Serialize(writer);
        OffsetRight.Serialize(writer);
        OffsetTop.Serialize(writer);
        OffsetBottom.Serialize(writer);
        writer.Write(ClipsContents);
        writer.Write((int)PositionType);
        writer.Write((int)AlignSelf);
        MarginLeft.Serialize(writer);
        MarginRight.Serialize(writer);
        MarginTop.Serialize(writer);
        MarginBottom.Serialize(writer);
        writer.Write(FlexGrow);
        writer.Write(FlexShrink);
        IGameMakerFlexProperties.Serialize(writer, this);
    }

    /// <inheritdoc/>
    public override void Unserialize(GameMakerReader reader)
    {
        Name = reader.ReadGameMakerString();
        Width = new(reader);
        Height = new(reader);
        MinimumWidth = new(reader);
        MinimumHeight = new(reader);
        MaximumWidth = new(reader);
        MaximumHeight = new(reader);
        OffsetLeft = new(reader);
        OffsetRight = new(reader);
        OffsetTop = new(reader);
        OffsetBottom = new(reader);
        ClipsContents = reader.ReadBoolean();
        PositionType = (PositionTypeKind)reader.ReadInt32();
        AlignSelf = (AlignmentKind)reader.ReadInt32();
        MarginLeft = new(reader);
        MarginRight = new(reader);
        MarginTop = new(reader);
        MarginBottom = new(reader);
        FlexGrow = reader.ReadSingle();
        FlexShrink = reader.ReadSingle();
        IGameMakerFlexProperties.Unserialize(reader, this);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 20 * 4;
        return IGameMakerFlexProperties.UnserializeChildObjectCount(reader);
    }
}

/// <summary>
/// Represents a UI game object node.
/// </summary>
public class GameMakerUIGameObject : GameMakerRoom.GameObject, IGameMakerUINodeData, IGameMakerFlexInstanceProperties
{
    /// <inheritdoc/>
    public bool Visible { get; set; }

    /// <inheritdoc/>
    public int Anchor { get; set; }

    /// <inheritdoc/>
    public bool StretchWidth { get; set; }

    /// <inheritdoc/>
    public bool StretchHeight { get; set; }

    /// <inheritdoc/>
    public bool TileH { get; set; }

    /// <inheritdoc/>
    public bool TileV { get; set; }

    /// <inheritdoc/>
    public bool KeepAspect { get; set; }

    /// <inheritdoc/>
    public override void Serialize(GameMakerWriter writer)
    {
        base.Serialize(writer);
        IGameMakerFlexInstanceProperties.Serialize(writer, this);
    }

    /// <inheritdoc/>
    public override void Unserialize(GameMakerReader reader)
    {
        base.Unserialize(reader);
        IGameMakerFlexInstanceProperties.Unserialize(reader, this);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 12 * 4;
        return IGameMakerFlexInstanceProperties.UnserializeChildObjectCount(reader);
    }
}

/// <summary>
/// Represents a UI sequence instance node.
/// </summary>
public class GameMakerUISequenceInstance : GameMakerRoom.SequenceInstance, IGameMakerUINodeData, IGameMakerFlexInstanceProperties
{
    /// <inheritdoc/>
    public bool Visible { get; set; }

    /// <inheritdoc/>
    public int Anchor { get; set; }

    /// <inheritdoc/>
    public bool StretchWidth { get; set; }

    /// <inheritdoc/>
    public bool StretchHeight { get; set; }

    /// <inheritdoc/>
    public bool TileH { get; set; }

    /// <inheritdoc/>
    public bool TileV { get; set; }

    /// <inheritdoc/>
    public bool KeepAspect { get; set; }

    /// <inheritdoc/>
    public override void Serialize(GameMakerWriter writer)
    {
        base.Serialize(writer);
        IGameMakerFlexInstanceProperties.Serialize(writer, this);
    }

    /// <inheritdoc/>
    public override void Unserialize(GameMakerReader reader)
    {
        base.Unserialize(reader);
        IGameMakerFlexInstanceProperties.Unserialize(reader, this);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 11 * 4;
        return IGameMakerFlexInstanceProperties.UnserializeChildObjectCount(reader);
    }
}

/// <summary>
/// Represents a UI sprite instance node.
/// </summary>
public class GameMakerUISpriteInstance : GameMakerRoom.SpriteInstance, IGameMakerUINodeData, IGameMakerFlexInstanceProperties
{
    /// <inheritdoc/>
    public bool Visible { get; set; }

    /// <inheritdoc/>
    public int Anchor { get; set; }

    /// <inheritdoc/>
    public bool StretchWidth { get; set; }

    /// <inheritdoc/>
    public bool StretchHeight { get; set; }

    /// <inheritdoc/>
    public bool TileH { get; set; }

    /// <inheritdoc/>
    public bool TileV { get; set; }

    /// <inheritdoc/>
    public bool KeepAspect { get; set; }

    /// <inheritdoc/>
    public override void Serialize(GameMakerWriter writer)
    {
        base.Serialize(writer);
        IGameMakerFlexInstanceProperties.Serialize(writer, this);
    }

    /// <inheritdoc/>
    public override void Unserialize(GameMakerReader reader)
    {
        base.Unserialize(reader);
        IGameMakerFlexInstanceProperties.Unserialize(reader, this);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 11 * 4;
        return IGameMakerFlexInstanceProperties.UnserializeChildObjectCount(reader);
    }
}

/// <summary>
/// Represents a UI text item instance node.
/// </summary>
public class GameMakerUITextItemInstance : GameMakerRoom.TextItemInstance, IGameMakerUINodeData, IGameMakerFlexInstanceProperties
{
    /// <inheritdoc/>
    public bool Visible { get; set; }

    /// <inheritdoc/>
    public int Anchor { get; set; }

    /// <inheritdoc/>
    public bool StretchWidth { get; set; }

    /// <inheritdoc/>
    public bool StretchHeight { get; set; }

    /// <inheritdoc/>
    public bool TileH { get; set; }

    /// <inheritdoc/>
    public bool TileV { get; set; }

    /// <inheritdoc/>
    public bool KeepAspect { get; set; }

    /// <inheritdoc/>
    public override void Serialize(GameMakerWriter writer)
    {
        base.Serialize(writer);
        IGameMakerFlexInstanceProperties.Serialize(writer, this);
    }

    /// <inheritdoc/>
    public override void Unserialize(GameMakerReader reader)
    {
        base.Unserialize(reader);
        IGameMakerFlexInstanceProperties.Unserialize(reader, this);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public new static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 17 * 4;
        return IGameMakerFlexInstanceProperties.UnserializeChildObjectCount(reader);
    }
}

/// <summary>
/// Represents a UI effect layer node.
/// </summary>
public class GameMakerUIEffectLayer : IGameMakerUINodeData
{
    /// <summary>
    /// Whether the effect layer is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Effect type for the effect layer.
    /// </summary>
    public GameMakerString EffectType { get; set; }

    /// <summary>
    /// List of effect properties for the effect layer.
    /// </summary>
    public GameMakerPointerList<GameMakerRoom.EffectProperty> Properties { get; set; } = new();

    /// <inheritdoc />
    public void Serialize(GameMakerWriter writer)
    {
        writer.Write(Enabled);
        writer.WriteGameMakerString(EffectType);
        Properties.Serialize(writer);
    }

    /// <inheritdoc />
    public void Unserialize(GameMakerReader reader)
    {
        Enabled = reader.ReadBoolean();
        EffectType = reader.ReadGameMakerString();
        Properties.Unserialize(reader);
    }

    /// <inheritdoc cref="GameMakerObject.UnserializeChildObjectCount(GameMakerReader)"/>
    public static uint UnserializeChildObjectCount(GameMakerReader reader)
    {
        reader.Position += 8;
        return GameMakerPointerList<GameMakerRoom.EffectProperty>.UnserializeChildObjectCount(reader);
    }
}
