using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project.Json;
using System;
using System.Reflection;

namespace G3MLib.DataFile.Project.SerializableAssets;

/// <summary>
/// A serializable version of <see cref="GameMakerRoom"/>.
/// </summary>
internal sealed class SerializableRoom : ISerializableProjectAsset
{
    /// <inheritdoc/>
    public string DataName { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public SerializableAssetType AssetType => SerializableAssetType.Room;

    /// <inheritdoc/>
    [JsonIgnore]
    public bool IndividualDirectory => false;

    /// <inheritdoc/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OverrideOrder { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Caption"/>
    public string Caption { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Width"/>
    public uint Width { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Height"/>
    public uint Height { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Speed"/>
    public uint Speed { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Persistent"/>
    public bool Persistent { get; set; }

    /// <inheritdoc cref="GameMakerRoom.BackgroundColor"/>
    public uint BackgroundColor { get; set; }

    /// <inheritdoc cref="GameMakerRoom.DrawBackgroundColor"/>
    public bool DrawBackgroundColor { get; set; }

    /// <inheritdoc cref="GameMakerRoom.CreationCodeId"/>
    public string CreationCodeEntry { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Flags"/>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameMakerRoom.RoomEntryFlags Flags { get; set; }

    /// <inheritdoc cref="GameMakerRoom.World"/>
    public bool World { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Top"/>
    public uint Top { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Left"/>
    public uint Left { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Right"/>
    public uint Right { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Bottom"/>
    public uint Bottom { get; set; }

    /// <inheritdoc cref="GameMakerRoom.GravityX"/>
    public float GravityX { get; set; }

    /// <inheritdoc cref="GameMakerRoom.GravityY"/>
    public float GravityY { get; set; }

    /// <inheritdoc cref="GameMakerRoom.MetersPerPixel"/>
    public float MetersPerPixel { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Backgrounds"/>
    public List<Background> Backgrounds { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Views"/>
    public List<View> Views { get; set; }

    /// <inheritdoc cref="GameMakerRoom.GameObjects"/>
    public List<GameObject> GameObjects { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Tiles"/>
    [JsonConverter(typeof(NoPrettyPrintJsonConverter<List<Tile>>))]
    public List<Tile> Tiles { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Layers"/>
    public List<Layer> Layers { get; set; }

    /// <inheritdoc cref="GameMakerRoom.InstanceCreationOrderIDs"/>
    public List<int> InstanceCreationOrderIDs { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Sequences"/>
    public List<string> Sequences { get; set; }

    /// <inheritdoc cref="GameMakerRoom.Background"/>
    public sealed class Background
    {
        /// <inheritdoc cref="GameMakerRoom.Background.Enabled"/>
        public bool Enabled { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.Foreground"/>
        public bool Foreground { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.BackgroundDefinition"/>
        public string BackgroundAsset { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.X"/>
        public int X { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.Y"/>
        public int Y { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.SpeedX"/>
        public int SpeedX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.SpeedY"/>
        public int SpeedY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.Stretch"/>
        public bool Stretch { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.TiledHorizontally"/>
        public bool TiledHorizontally { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Background.TiledVertically"/>
        public bool TiledVertically { get; set; }
    }

    /// <inheritdoc cref="GameMakerRoom.View"/>
    public sealed class View
    {
        /// <inheritdoc cref="GameMakerRoom.View.Enabled"/>
        public bool Enabled { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.ViewX"/>
        public int ViewX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.ViewY"/>
        public int ViewY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.ViewWidth"/>
        public int ViewWidth { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.ViewHeight"/>
        public int ViewHeight { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.PortX"/>
        public int PortX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.PortY"/>
        public int PortY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.PortWidth"/>
        public int PortWidth { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.PortHeight"/>
        public int PortHeight { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.BorderX"/>
        public uint BorderX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.BorderY"/>
        public uint BorderY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.SpeedX"/>
        public int SpeedX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.SpeedY"/>
        public int SpeedY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.View.ObjectId"/>
        public string FollowsObject { get; set; }
    }

    /// <inheritdoc cref="GameMakerRoom.GameObject"/>
    public sealed class GameObject
    {
        /// <inheritdoc cref="GameMakerRoom.GameObject.ObjectDefinition"/>
        public string ObjectName { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.X"/>
        public int X { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.Y"/>
        public int Y { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.CreationCode"/>
        public string CreationCodeEntry { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.ScaleX"/>
        public float ScaleX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.ScaleY"/>
        public float ScaleY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.Color"/>
        public uint Color { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.Rotation"/>
        public float Rotation { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.PreCreateCode"/>
        public string PreCreateCodeEntry { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.ImageSpeed"/>
        public float ImageSpeed { get; set; }

        /// <inheritdoc cref="GameMakerRoom.GameObject.ImageIndex"/>
        public int ImageIndex { get; set; }
    }

    /// <inheritdoc cref="GameMakerRoom.Tile"/>
    public sealed class Tile
    {
        /// <inheritdoc cref="GameMakerRoom.Tile.BackgroundDefinition"/>
        /// <seealso cref="GameMakerRoom.Tile.SpriteDefinition"/>
        public string Asset { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.X"/>
        public int X { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.Y"/>
        public int Y { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.SourceX"/>
        public int SourceX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.SourceY"/>
        public int SourceY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.Width"/>
        public uint Width { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.Height"/>
        public uint Height { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.TileDepth"/>
        public int Depth { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.ScaleX"/>
        public float ScaleX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.ScaleY"/>
        public float ScaleY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Tile.Color"/>
        public uint Color { get; set; }
    }

    /// <inheritdoc cref="GameMakerRoom.Layer"/>
    [JsonDerivedType(typeof(InstancesLayer), nameof(InstancesLayer))]
    [JsonDerivedType(typeof(TilesLayer), nameof(TilesLayer))]
    [JsonDerivedType(typeof(BackgroundLayer), nameof(BackgroundLayer))]
    [JsonDerivedType(typeof(AssetsLayer), nameof(AssetsLayer))]
    [JsonDerivedType(typeof(EffectLayer), nameof(EffectLayer))]
    [JsonDerivedType(typeof(PathLayer), nameof(PathLayer))]
    [JsonDerivedType(typeof(PathLayer2), nameof(PathLayer2))]
    public abstract class Layer
    {
        /// <inheritdoc cref="GameMakerRoom.Layer.LayerName"/>
        public string Name { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerId"/>
        public uint ID { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerDepth"/>
        public int Depth { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.XOffset"/>
        public float XOffset { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.YOffset"/>
        public float YOffset { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.HSpeed"/>
        public float HSpeed { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.VSpeed"/>
        public float VSpeed { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.IsVisible"/>
        public bool IsVisible { get; set; }
    }

    /// <inheritdoc cref="GameMakerRoom.Layer.LayerInstancesData"/>
    public sealed class InstancesLayer : Layer
    {
        /// <summary>
        /// List of indices corresponding to <see cref="GameObjects"/>, or -1 if a nonexistent object instance.
        /// </summary>
        public List<int> InstanceIndices { get; set; }
    }

    /// <inheritdoc cref="GameMakerRoom.Layer.LayerTilesData"/>
    public sealed class TilesLayer : Layer
    {
        /// <inheritdoc cref="GameMakerRoom.Layer.LayerTilesData.Background"/>
        public string Background { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerTilesData.TilesX"/>
        public uint TilesX { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerTilesData.TilesY"/>
        public uint TilesY { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerTilesData.TileData"/>
        [JsonConverter(typeof(NoPrettyPrintJsonConverter<List<uint>>))]
        public List<uint> TileData { get; set; }
    }

    /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData"/>
    public sealed class BackgroundLayer : Layer
    {
        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.Visible"/>
        public bool BackgroundVisible { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.Foreground"/>
        public bool Foreground { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.Sprite"/>
        public string Sprite { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.TiledHorizontally"/>
        public bool TiledHorizontally { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.TiledVertically"/>
        public bool TiledVertically { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.Stretch"/>
        public bool Stretch { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.Color"/>
        public uint Color { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.FirstFrame"/>
        public float FirstFrame { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.AnimationSpeed"/>
        public float AnimationSpeed { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerBackgroundData.AnimationSpeedType"/>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AnimationSpeedType AnimationSpeedType { get; set; }
    }

    /// <inheritdoc cref="GameMakerRoom.Layer.LayerAssetsData"/>
    public sealed class AssetsLayer : Layer
    {
        /// <inheritdoc cref="GameMakerRoom.Layer.LayerAssetsData.LegacyTiles"/>
        [JsonConverter(typeof(NoPrettyPrintJsonConverter<List<Tile>>))]
        public List<Tile> LegacyTiles { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerAssetsData.Sprites"/>
        public List<SpriteAssetInstance> Sprites { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerAssetsData.Sequences"/>
        public List<SequenceAssetInstance> Sequences { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerAssetsData.NineSlices"/>
        public List<SpriteAssetInstance> NineSlices { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerAssetsData.ParticleSystems"/>
        public List<ParticleSystemAssetInstance> ParticleSystems { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerAssetsData.TextItems"/>
        public List<TextItemAssetInstance> TextItems { get; set; }

        /// <summary>
        /// Common transform and animation data for an asset layer instance.
        /// </summary>
        public abstract class AssetInstance
        {
            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.Name"/>
            [JsonPropertyOrder(0)]
            public string Name { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.X"/>
            [JsonPropertyOrder(2)]
            public int X { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.Y"/>
            [JsonPropertyOrder(3)]
            public int Y { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.ScaleX"/>
            [JsonPropertyOrder(4)]
            public float ScaleX { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.ScaleY"/>
            [JsonPropertyOrder(5)]
            public float ScaleY { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.Color"/>
            [JsonPropertyOrder(6)]
            public uint Color { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.AnimationSpeed"/>
            [JsonPropertyOrder(7)]
            public float AnimationSpeed { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.AnimationSpeedType"/>
            [JsonPropertyOrder(8)]
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public AnimationSpeedType AnimationSpeedType { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.FrameIndex"/>
            [JsonPropertyOrder(9)]
            public float FrameIndex { get; set; }

            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.Rotation"/>
            [JsonPropertyOrder(10)]
            public float Rotation { get; set; }
        }

        /// <inheritdoc cref="GameMakerRoom.SpriteInstance"/>
        public sealed class SpriteAssetInstance : AssetInstance
        {
            /// <inheritdoc cref="GameMakerRoom.SpriteInstance.Sprite"/>
            [JsonPropertyOrder(1)]
            public string Sprite { get; set; }
        }

        /// <inheritdoc cref="GameMakerRoom.SequenceInstance"/>
        public sealed class SequenceAssetInstance : AssetInstance
        {
            /// <inheritdoc cref="GameMakerRoom.SequenceInstance.Sequence"/>
            [JsonPropertyOrder(1)]
            public string Sequence { get; set; }
        }

        /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance"/>
        public sealed class ParticleSystemAssetInstance
        {
            /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance.Name"/>
            public string Name { get; set; }

            /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance.ParticleSystem"/>
            public string ParticleSystem { get; set; }

            /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance.X"/>
            public int X { get; set; }

            /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance.Y"/>
            public int Y { get; set; }

            /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance.ScaleX"/>
            public float ScaleX { get; set; }

            /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance.ScaleY"/>
            public float ScaleY { get; set; }

            /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance.Color"/>
            public uint Color { get; set; }

            /// <inheritdoc cref="GameMakerRoom.ParticleSystemInstance.Rotation"/>
            public float Rotation { get; set; }
        }

        /// <inheritdoc cref="GameMakerRoom.TextItemInstance"/>
        public sealed class TextItemAssetInstance
        {
            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.Name"/>
            public string Name { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.Font"/>
            public string Font { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.Text"/>
            public string Text { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.X"/>
            public int X { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.Y"/>
            public int Y { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.ScaleX"/>
            public float ScaleX { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.ScaleY"/>
            public float ScaleY { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.Color"/>
            public uint Color { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.Rotation"/>
            public float Rotation { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.OriginX"/>
            public float OriginX { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.OriginY"/>
            public float OriginY { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.Alignment"/>
            public int Alignment { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.CharSpacing"/>
            public float CharSpacing { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.LineSpacing"/>
            public float LineSpacing { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.FrameWidth"/>
            public float FrameWidth { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.FrameHeight"/>
            public float FrameHeight { get; set; }

            /// <inheritdoc cref="GameMakerRoom.TextItemInstance.Wrap"/>
            public bool Wrap { get; set; }
        }
    }

    /// <inheritdoc cref="GameMakerRoom.Layer.LayerEffectData"/>
    public sealed class EffectLayer : Layer
    {
        /// <inheritdoc cref="GameMakerRoom.Layer.LayerEffectData.EffectType"/>
        public string EffectType { get; set; }

        /// <inheritdoc cref="GameMakerRoom.Layer.LayerEffectData.Properties"/>
        public List<EffectProperty> Properties { get; set; }

        /// <inheritdoc cref="GameMakerRoom.EffectProperty"/>
        public sealed class EffectProperty
        {
            /// <inheritdoc cref="GameMakerRoom.EffectProperty.Kind"/>
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public PropertyType Kind { get; set; }

            /// <inheritdoc cref="GameMakerRoom.EffectProperty.Name"/>
            public string Name { get; set; }

            /// <inheritdoc cref="GameMakerRoom.EffectProperty.Value"/>
            public string Value { get; set; }

            /// <inheritdoc cref="GameMakerRoom.EffectProperty.PropertyType"/>
            public enum PropertyType
            {
                Real = 0,
                Color = 1,
                Sampler = 2
            }
        }
    }

    /// <inheritdoc cref="GameMakerRoom.LayerType.Path"/>
    public sealed class PathLayer : Layer
    {
    }

    /// <inheritdoc cref="GameMakerRoom.LayerType.Path2"/>
    public sealed class PathLayer2 : Layer
    {
    }

    /// <inheritdoc cref="AnimationSpeedType"/>
    public enum AnimationSpeedType : uint
    {
        FramesPerSecond = 0,
        FramesPerGameFrame = 1
    }

    // Data asset that was located during pre-import.
    private GameMakerRoom _dataAsset = null;

    /// <summary>
    /// Populates this serializable room with data from an actual room.
    /// </summary>
    internal void PopulateFromData(ProjectContext projectContext, GameMakerRoom room)
    {
        // Update all main properties
        DataName = room.Name.Content;
        Caption = room.Caption?.Content;
        Width = room.Width;
        Height = room.Height;
        Speed = room.Speed;
        Persistent = room.Persistent;
        BackgroundColor = room.BackgroundColor;
        DrawBackgroundColor = room.DrawBackgroundColor;
        CreationCodeEntry = room.CreationCodeId?.Name?.Content;
        Flags = room.Flags;
        World = room.World;
        Top = room.Top;
        Left = room.Left;
        Right = room.Right;
        Bottom = room.Bottom;
        GravityX = room.GravityX;
        GravityY = room.GravityY;
        MetersPerPixel = room.MetersPerPixel;

        // Update backgrounds
        Backgrounds = new(room.Backgrounds.Count);
        foreach (GameMakerRoom.Background bg in room.Backgrounds)
        {
            Backgrounds.Add(new()
            {
                Enabled = bg.Enabled,
                Foreground = bg.Foreground,
                BackgroundAsset = bg.BackgroundDefinition?.Name?.Content,
                X = bg.X,
                Y = bg.Y,
                SpeedX = bg.SpeedX,
                SpeedY = bg.SpeedY,
                Stretch = bg.Stretch,
                TiledHorizontally = bg.TiledHorizontally,
                TiledVertically = bg.TiledVertically
            });
        }

        // Update views
        Views = new(room.Views.Count);
        foreach (GameMakerRoom.View view in room.Views)
        {
            Views.Add(new()
            {
                Enabled = view.Enabled,
                ViewX = view.ViewX,
                ViewY = view.ViewY,
                ViewWidth = view.ViewWidth,
                ViewHeight = view.ViewHeight,
                PortX = view.PortX,
                PortY = view.PortY,
                PortWidth = view.PortWidth,
                PortHeight = view.PortHeight,
                BorderX = view.BorderX,
                BorderY = view.BorderY,
                SpeedX = view.SpeedX,
                SpeedY = view.SpeedY,
                FollowsObject = view.ObjectId?.Name?.Content
            });
        }

        // Update game objects (and track indices if we have layers)
        Dictionary<GameMakerRoom.GameObject, int> gameObjectIndices =
            room.Layers is not null ? new(room.GameObjects.Count) : null;
        Dictionary<uint, int> gameObjectIndicesById =
            room.InstanceCreationOrderIDs is not null ? new(room.GameObjects.Count) : null;
        GameObjects = new(room.GameObjects.Count);
        foreach (GameMakerRoom.GameObject obj in room.GameObjects)
        {
            gameObjectIndices?.Add(obj, GameObjects.Count);
            gameObjectIndicesById?.Add(obj.InstanceID, GameObjects.Count);

            GameObjects.Add(new()
            {
                ObjectName = obj.ObjectDefinition?.Name?.Content,
                X = obj.X,
                Y = obj.Y,
                CreationCodeEntry = obj.CreationCode?.Name?.Content,
                ScaleX = obj.ScaleX,
                ScaleY = obj.ScaleY,
                Color = obj.Color,
                Rotation = obj.Rotation,
                PreCreateCodeEntry = obj.PreCreateCode?.Name?.Content,
                ImageSpeed = obj.ImageSpeed,
                ImageIndex = obj.ImageIndex
            });
        }

        // Update tiles
        Tiles = new(room.Tiles.Count);
        foreach (GameMakerRoom.Tile tile in room.Tiles)
        {
            Tiles.Add(new()
            {
                Asset = tile.ObjectDefinition?.Name?.Content,
                X = tile.X,
                Y = tile.Y,
                SourceX = tile.SourceX,
                SourceY = tile.SourceY,
                Width = tile.Width,
                Height = tile.Height,
                Depth = tile.TileDepth,
                ScaleX = tile.ScaleX,
                ScaleY = tile.ScaleY,
                Color = tile.Color
            });
        }

        // Update layers, if they exist
        if (room.Layers is GameMakerPointerList<GameMakerRoom.Layer> layers)
        {
            Layers = new(layers.Count);
            foreach (GameMakerRoom.Layer layer in layers)
            {
                Layer newLayer = layer.LayerType switch
                {
                    GameMakerRoom.LayerType.Instances => new InstancesLayer()
                    {
                        InstanceIndices =
                            layer.InstancesData.Instances.Select((GameMakerRoom.GameObject instance) =>
                            {
                                if (instance.Nonexistent)
                                {
                                    return -1;
                                }
                                if (gameObjectIndices.TryGetValue(instance, out int index))
                                {
                                    return index;
                                }
                                return -1;
                            })
                            .ToList()
                    },
                    GameMakerRoom.LayerType.Tiles => new TilesLayer()
                    {
                        Background = layer.TilesData.Background?.Name?.Content,
                        TilesX = layer.TilesData.TilesX,
                        TilesY = layer.TilesData.TilesY,
                        TileData = ConvertTileDataToList(layer.TilesData.TileData)
                    },
                    GameMakerRoom.LayerType.Background => new BackgroundLayer()
                    {
                        BackgroundVisible = layer.BackgroundData.Visible,
                        Foreground = layer.BackgroundData.Foreground,
                        Sprite = layer.BackgroundData.Sprite?.Name?.Content,
                        TiledHorizontally = layer.BackgroundData.TiledHorizontally,
                        TiledVertically = layer.BackgroundData.TiledVertically,
                        Stretch = layer.BackgroundData.Stretch,
                        Color = layer.BackgroundData.Color,
                        FirstFrame = layer.BackgroundData.FirstFrame,
                        AnimationSpeed = layer.BackgroundData.AnimationSpeed,
                        AnimationSpeedType = (AnimationSpeedType)layer.BackgroundData.AnimationSpeedType
                    },
                    GameMakerRoom.LayerType.Assets => new AssetsLayer()
                    {
                        LegacyTiles = layer.AssetsData.LegacyTiles.Select((GameMakerRoom.Tile tile) =>
                            new Tile()
                            {
                                Asset = tile.ObjectDefinition?.Name?.Content,
                                X = tile.X,
                                Y = tile.Y,
                                SourceX = tile.SourceX,
                                SourceY = tile.SourceY,
                                Width = tile.Width,
                                Height = tile.Height,
                                Depth = tile.TileDepth,
                                ScaleX = tile.ScaleX,
                                ScaleY = tile.ScaleY,
                                Color = tile.Color
                            })
                            .ToList(),
                        Sprites = layer.AssetsData.Sprites.Select((GameMakerRoom.SpriteInstance sprite) =>
                            new AssetsLayer.SpriteAssetInstance()
                            {
                                Name = sprite.Name?.Content,
                                Sprite = sprite.Sprite?.Name?.Content,
                                X = sprite.X,
                                Y = sprite.Y,
                                ScaleX = sprite.ScaleX,
                                ScaleY = sprite.ScaleY,
                                Color = sprite.Color,
                                AnimationSpeed = sprite.AnimationSpeed,
                                AnimationSpeedType = (AnimationSpeedType)sprite.AnimationSpeedType,
                                FrameIndex = sprite.FrameIndex,
                                Rotation = sprite.Rotation
                            })
                            .ToList(),
                        Sequences = layer.AssetsData.Sequences?.Select((GameMakerRoom.SequenceInstance seq) =>
                            new AssetsLayer.SequenceAssetInstance()
                            {
                                Name = seq.Name?.Content,
                                Sequence = seq.Sequence?.Name?.Content,
                                X = seq.X,
                                Y = seq.Y,
                                ScaleX = seq.ScaleX,
                                ScaleY = seq.ScaleY,
                                Color = seq.Color,
                                AnimationSpeed = seq.AnimationSpeed,
                                AnimationSpeedType = (AnimationSpeedType)seq.AnimationSpeedType,
                                FrameIndex = seq.FrameIndex,
                                Rotation = seq.Rotation
                            })
                            .ToList(),
                        NineSlices = layer.AssetsData.NineSlices?.Select((GameMakerRoom.SpriteInstance sprite) =>
                            new AssetsLayer.SpriteAssetInstance()
                            {
                                Name = sprite.Name?.Content,
                                Sprite = sprite.Sprite?.Name?.Content,
                                X = sprite.X,
                                Y = sprite.Y,
                                ScaleX = sprite.ScaleX,
                                ScaleY = sprite.ScaleY,
                                Color = sprite.Color,
                                AnimationSpeed = sprite.AnimationSpeed,
                                AnimationSpeedType = (AnimationSpeedType)sprite.AnimationSpeedType,
                                FrameIndex = sprite.FrameIndex,
                                Rotation = sprite.Rotation
                            })
                            .ToList(),
                        ParticleSystems = layer.AssetsData.ParticleSystems?.Select((GameMakerRoom.ParticleSystemInstance psys) =>
                            new AssetsLayer.ParticleSystemAssetInstance()
                            {
                                Name = psys.Name?.Content,
                                ParticleSystem = psys.ParticleSystem?.Name?.Content,
                                X = psys.X,
                                Y = psys.Y,
                                ScaleX = psys.ScaleX,
                                ScaleY = psys.ScaleY,
                                Color = psys.Color,
                                Rotation = psys.Rotation
                            })
                            .ToList(),
                        TextItems = layer.AssetsData.TextItems?.Select((GameMakerRoom.TextItemInstance text) =>
                            new AssetsLayer.TextItemAssetInstance()
                            {
                                Name = text.Name?.Content,
                                Font = text.Font?.Name?.Content,
                                Text = text.Text?.Content,
                                X = text.X,
                                Y = text.Y,
                                ScaleX = text.ScaleX,
                                ScaleY = text.ScaleY,
                                Color = text.Color,
                                Rotation = text.Rotation,
                                OriginX = text.OriginX,
                                OriginY = text.OriginY,
                                Alignment = text.Alignment,
                                CharSpacing = text.CharSpacing,
                                LineSpacing = text.LineSpacing,
                                FrameWidth = text.FrameWidth,
                                FrameHeight = text.FrameHeight,
                                Wrap = text.Wrap
                            })
                            .ToList()
                    },
                    GameMakerRoom.LayerType.Effect => new EffectLayer()
                    {
                        EffectType = layer.EffectType?.Content,
                        Properties = layer.EffectProperties.Select((GameMakerRoom.EffectProperty prop) =>
                            new EffectLayer.EffectProperty()
                            {
                                Kind = (EffectLayer.EffectProperty.PropertyType)prop.Kind,
                                Name = prop.Name?.Content,
                                Value = prop.Value?.Content
                            })
                            .ToList()
                    },
                    GameMakerRoom.LayerType.Path => new PathLayer(),
                    GameMakerRoom.LayerType.Path2 => new PathLayer2(),
                    _ => throw new ProjectException($"Failed to find valid layer type for {room.Name.Content}")
                };
                newLayer.Name = layer.LayerName?.Content;
                newLayer.ID = layer.LayerId;
                newLayer.Depth = layer.LayerDepth;
                newLayer.XOffset = layer.XOffset;
                newLayer.YOffset = layer.YOffset;
                newLayer.HSpeed = layer.HSpeed;
                newLayer.VSpeed = layer.VSpeed;
                newLayer.IsVisible = layer.IsVisible;
                Layers.Add(newLayer);
            }
        }

        // Update sequences, if they exist
        if (room.Sequences is not null)
        {
            Sequences = new(room.Sequences.Count);
            foreach (var seq in room.Sequences)
            {
                Sequences.Add(seq.Resource?.Name?.Content);
            }
        }

        // Update instance creation ID order, if it exists
        if (room.InstanceCreationOrderIDs is not null)
        {
            InstanceCreationOrderIDs = new(room.InstanceCreationOrderIDs.InstanceIDs.Count);
            foreach (uint id in room.InstanceCreationOrderIDs.InstanceIDs)
            {
                if (!gameObjectIndicesById.TryGetValue(id, out int gameObjectIndex))
                {
                    gameObjectIndex = -1;
                }
                InstanceCreationOrderIDs.Add(gameObjectIndex);
            }
        }
    }

    /// <inheritdoc/>
    public void Serialize(ProjectContext projectContext, string destinationFile)
    {
        using FileStream fs = new(destinationFile, FileMode.Create);
        JsonSerializer.Serialize<ISerializableProjectAsset>(fs, this, ProjectContext.JsonOptions);
    }

    /// <inheritdoc/>
    public void PreImport(ProjectContext projectContext)
    {
        if (projectContext.Data.Rooms.ByName(DataName) is GameMakerRoom existing)
        {
            // Room found
            _dataAsset = existing;
        }
        else
        {
            // No room found; create new one
            _dataAsset = new()
            {
                Name = projectContext.MakeString(DataName)
            };
            projectContext.Data.Rooms.Add(_dataAsset);
        }
    }

    /// <inheritdoc/>
    public IProjectAsset Import(ProjectContext projectContext)
    {
        GameMakerRoom room = _dataAsset;

        // Update all main properties
        room.Caption = projectContext.MakeString(Caption);
        room.Width = Width;
        room.Height = Height;
        room.Speed = Speed;
        room.Persistent = Persistent;
        room.BackgroundColor = BackgroundColor;
        room.DrawBackgroundColor = DrawBackgroundColor;
        room.CreationCodeId = projectContext.FindCode(CreationCodeEntry, this);
        room.Flags = Flags;
        room.World = World;
        room.Top = Top;
        room.Left = Left;
        room.Right = Right;
        room.Bottom = Bottom;
        room.GravityX = GravityX;
        room.GravityY = GravityY;
        room.MetersPerPixel = MetersPerPixel;

        // Update backgrounds
        room.Backgrounds ??= [];
        room.Backgrounds.Clear();
        room.Backgrounds.SetCapacity(Backgrounds?.Count ?? 0);
        foreach (Background bg in Backgrounds ?? [])
        {
            room.Backgrounds.Add(new()
            {
                Enabled = bg.Enabled,
                Foreground = bg.Foreground,
                BackgroundDefinition = projectContext.FindBackground(bg.BackgroundAsset, this),
                X = bg.X,
                Y = bg.Y,
                SpeedX = bg.SpeedX,
                SpeedY = bg.SpeedY,
                Stretch = bg.Stretch,
                TiledHorizontally = bg.TiledHorizontally,
                TiledVertically = bg.TiledVertically
            });
        }

        // Update views
        room.Views ??= [];
        room.Views.Clear();
        room.Views.SetCapacity(Views?.Count ?? 0);
        foreach (View view in Views ?? [])
        {
            room.Views.Add(new()
            {
                Enabled = view.Enabled,
                ViewX = view.ViewX,
                ViewY = view.ViewY,
                ViewWidth = view.ViewWidth,
                ViewHeight = view.ViewHeight,
                PortX = view.PortX,
                PortY = view.PortY,
                PortWidth = view.PortWidth,
                PortHeight = view.PortHeight,
                BorderX = view.BorderX,
                BorderY = view.BorderY,
                SpeedX = view.SpeedX,
                SpeedY = view.SpeedY,
                ObjectId = projectContext.FindGameObject(view.FollowsObject, this)
            });
        }

        // Update game objects
        room.GameObjects ??= [];
        room.GameObjects.Clear();
        room.GameObjects.SetCapacity(GameObjects?.Count ?? 0);
        foreach (GameObject obj in GameObjects ?? [])
        {
            room.GameObjects.Add(new()
            {
                ObjectDefinition = projectContext.FindGameObject(obj.ObjectName, this),
                X = obj.X,
                Y = obj.Y,
                CreationCode = projectContext.FindCode(obj.CreationCodeEntry, this),
                ScaleX = obj.ScaleX,
                ScaleY = obj.ScaleY,
                Color = obj.Color,
                Rotation = obj.Rotation,
                PreCreateCode = projectContext.FindCode(obj.PreCreateCodeEntry, this),
                ImageSpeed = obj.ImageSpeed,
                ImageIndex = obj.ImageIndex,
                InstanceID = ++projectContext.Data.GeneralInfo.LastObj
            });
        }

        // Update tiles
        room.Tiles ??= [];
        room.Tiles.Clear();
        room.Tiles.SetCapacity(Tiles?.Count ?? 0);
        foreach (Tile tile in Tiles ?? [])
        {
            GameMakerRoom.Tile newTile = new()
            {
                X = tile.X,
                Y = tile.Y,
                SourceX = tile.SourceX,
                SourceY = tile.SourceY,
                Width = tile.Width,
                Height = tile.Height,
                TileDepth = tile.Depth,
                ScaleX = tile.ScaleX,
                ScaleY = tile.ScaleY,
                Color = tile.Color
            };
            if (projectContext.Data.IsGameMaker2())
            {
                newTile.spriteMode = true;
                newTile.SpriteDefinition = projectContext.FindSprite(tile.Asset, this);
            }
            else
            {
                newTile.BackgroundDefinition = projectContext.FindBackground(tile.Asset, this);
            }
            room.Tiles.Add(newTile);
        }

        // Update layers, if they exist
        if (Layers is not null)
        {
            room.Layers ??= [];
            room.Layers.Clear();
            room.Layers.SetCapacity(Layers.Count);
            foreach (Layer layer in Layers)
            {
                room.Layers.Add(new()
                {
                    LayerName = projectContext.MakeString(layer.Name),
                    LayerId = layer.ID,
                    LayerDepth = layer.Depth,
                    XOffset = layer.XOffset,
                    YOffset = layer.YOffset,
                    HSpeed = layer.HSpeed,
                    VSpeed = layer.VSpeed,
                    IsVisible = layer.IsVisible,
                    LayerType = layer switch
                    {
                        InstancesLayer => GameMakerRoom.LayerType.Instances,
                        TilesLayer => GameMakerRoom.LayerType.Tiles,
                        BackgroundLayer => GameMakerRoom.LayerType.Background,
                        AssetsLayer => GameMakerRoom.LayerType.Assets,
                        EffectLayer => GameMakerRoom.LayerType.Effect,
                        PathLayer => GameMakerRoom.LayerType.Path,
                        PathLayer2 => GameMakerRoom.LayerType.Path2,
                        _ => throw new ProjectException($"Failed to find valid layer type for {room.Name.Content}")
                    },
                    Data = layer switch
                    {
                        InstancesLayer instances => new GameMakerRoom.Layer.LayerInstancesData()
                        {
                            Instances = new(instances.InstanceIndices.Select((int index) =>
                            {
                                if (index < 0 || index >= room.GameObjects.Count)
                                {
                                    return new GameMakerRoom.GameObject()
                                    {
                                        InstanceID = ++projectContext.Data.GeneralInfo.LastObj,
                                        Nonexistent = true
                                    };
                                }
                                return room.GameObjects[index];
                            }))
                        },
                        TilesLayer tiles => new GameMakerRoom.Layer.LayerTilesData()
                        {
                            Background = projectContext.FindBackground(tiles.Background, this),
                            TilesX = tiles.TilesX,
                            TilesY = tiles.TilesY,
                            TileData = ConvertListToTileData(tiles.TilesX, tiles.TilesY, tiles.TileData)
                        },
                        BackgroundLayer bg => new GameMakerRoom.Layer.LayerBackgroundData()
                        {
                            Visible = bg.BackgroundVisible,
                            Foreground = bg.Foreground,
                            Sprite = projectContext.FindSprite(bg.Sprite, this),
                            TiledHorizontally = bg.TiledHorizontally,
                            TiledVertically = bg.TiledVertically,
                            Stretch = bg.Stretch,
                            Color = bg.Color,
                            FirstFrame = bg.FirstFrame,
                            AnimationSpeed = bg.AnimationSpeed,
                            AnimationSpeedType = (Models.AnimationSpeedType)bg.AnimationSpeedType
                        },
                        AssetsLayer assets => new GameMakerRoom.Layer.LayerAssetsData()
                        {
                            LegacyTiles = new(assets.LegacyTiles.Select((Tile tile) =>
                            {
                                GameMakerRoom.Tile newTile = new()
                                {
                                    X = tile.X,
                                    Y = tile.Y,
                                    SourceX = tile.SourceX,
                                    SourceY = tile.SourceY,
                                    Width = tile.Width,
                                    Height = tile.Height,
                                    TileDepth = tile.Depth,
                                    ScaleX = tile.ScaleX,
                                    ScaleY = tile.ScaleY,
                                    Color = tile.Color,
                                    InstanceID = ++projectContext.Data.GeneralInfo.LastTile
                                };
                                if (projectContext.Data.IsGameMaker2())
                                {
                                    newTile.spriteMode = true;
                                    newTile.SpriteDefinition = projectContext.FindSprite(tile.Asset, this);
                                }
                                else
                                {
                                    newTile.BackgroundDefinition = projectContext.FindBackground(tile.Asset, this);
                                }
                                return newTile;
                            })),
                            Sprites = new(assets.Sprites.Select((AssetsLayer.SpriteAssetInstance sprite) =>
                                new GameMakerRoom.SpriteInstance()
                                {
                                    Name = projectContext.MakeString(sprite.Name),
                                    Sprite = projectContext.FindSprite(sprite.Sprite, this),
                                    X = sprite.X,
                                    Y = sprite.Y,
                                    ScaleX = sprite.ScaleX,
                                    ScaleY = sprite.ScaleY,
                                    Color = sprite.Color,
                                    AnimationSpeed = sprite.AnimationSpeed,
                                    AnimationSpeedType = (Models.AnimationSpeedType)sprite.AnimationSpeedType,
                                    FrameIndex = sprite.FrameIndex,
                                    Rotation = sprite.Rotation
                                })),
                            Sequences = assets.Sequences is null ? null :
                                new(assets.Sequences.Select((AssetsLayer.SequenceAssetInstance seq) =>
                                    new GameMakerRoom.SequenceInstance()
                                    {
                                        Name = projectContext.MakeString(seq.Name),
                                        Sequence = projectContext.FindSequence(seq.Sequence, this),
                                        X = seq.X,
                                        Y = seq.Y,
                                        ScaleX = seq.ScaleX,
                                        ScaleY = seq.ScaleY,
                                        Color = seq.Color,
                                        AnimationSpeed = seq.AnimationSpeed,
                                        AnimationSpeedType = (Models.AnimationSpeedType)seq.AnimationSpeedType,
                                        FrameIndex = seq.FrameIndex,
                                        Rotation = seq.Rotation
                                    })),
                            NineSlices = assets.NineSlices is null ? null :
                                new(assets.NineSlices.Select((AssetsLayer.SpriteAssetInstance sprite) =>
                                    new GameMakerRoom.SpriteInstance()
                                    {
                                        Name = projectContext.MakeString(sprite.Name),
                                        Sprite = projectContext.FindSprite(sprite.Sprite, this),
                                        X = sprite.X,
                                        Y = sprite.Y,
                                        ScaleX = sprite.ScaleX,
                                        ScaleY = sprite.ScaleY,
                                        Color = sprite.Color,
                                        AnimationSpeed = sprite.AnimationSpeed,
                                        AnimationSpeedType = (Models.AnimationSpeedType)sprite.AnimationSpeedType,
                                        FrameIndex = sprite.FrameIndex,
                                        Rotation = sprite.Rotation
                                    })),
                            ParticleSystems = assets.ParticleSystems is null ? null :
                                new(assets.ParticleSystems.Select((AssetsLayer.ParticleSystemAssetInstance psys) =>
                                    new GameMakerRoom.ParticleSystemInstance()
                                    {
                                        Name = projectContext.MakeString(psys.Name),
                                        ParticleSystem = projectContext.FindParticleSystem(psys.ParticleSystem, this),
                                        X = psys.X,
                                        Y = psys.Y,
                                        ScaleX = psys.ScaleX,
                                        ScaleY = psys.ScaleY,
                                        Color = psys.Color,
                                        Rotation = psys.Rotation
                                    })),
                            TextItems = assets.TextItems is null ? null :
                                new(assets.TextItems.Select((AssetsLayer.TextItemAssetInstance text) =>
                                    new GameMakerRoom.TextItemInstance()
                                    {
                                        Name = projectContext.MakeString(text.Name),
                                        Font = projectContext.FindFont(text.Font, this),
                                        Text = projectContext.MakeString(text.Text),
                                        X = text.X,
                                        Y = text.Y,
                                        ScaleX = text.ScaleX,
                                        ScaleY = text.ScaleY,
                                        Color = text.Color,
                                        Rotation = text.Rotation,
                                        OriginX = text.OriginX,
                                        OriginY = text.OriginY,
                                        Alignment = text.Alignment,
                                        CharSpacing = text.CharSpacing,
                                        LineSpacing = text.LineSpacing,
                                        FrameWidth = text.FrameWidth,
                                        FrameHeight = text.FrameHeight,
                                        Wrap = text.Wrap
                                    }))
                        },
                        EffectLayer effect => new GameMakerRoom.Layer.LayerEffectData()
                        {
                            EffectType = projectContext.MakeString(effect.EffectType),
                            Properties = new(effect.Properties.Select((EffectLayer.EffectProperty prop) =>
                                new GameMakerRoom.EffectProperty()
                                {
                                    Kind = (GameMakerRoom.EffectProperty.PropertyType)prop.Kind,
                                    Name = projectContext.MakeString(prop.Name),
                                    Value = projectContext.MakeString(prop.Value)
                                }))
                        },
                        PathLayer or PathLayer2 => null,
                        _ => throw new ProjectException($"Failed to find valid layer type for {room.Name.Content}")
                    }
                });
            }
        }

        // Update sequences, if they exist
        if (Sequences is not null)
        {
            room.Sequences = new(Sequences.Count);
            foreach (string seq in Sequences)
            {
                room.Sequences.Add(new(projectContext.FindSequence(seq, this)));
            }
        }

        // Update instance creation ID order, if it exists
        if (InstanceCreationOrderIDs is not null)
        {
            // Preserve the serialized creation order when the source project supplies one.
            room.InstanceCreationOrderIDs ??= new();
            room.InstanceCreationOrderIDs.InstanceIDs = new(InstanceCreationOrderIDs.Count);
            foreach (int index in InstanceCreationOrderIDs)
            {
                uint id;
                if (index < 0 || index >= room.GameObjects.Count)
                {
                    id = ++projectContext.Data.GeneralInfo.LastObj;
                }
                else
                {
                    id = room.GameObjects[index].InstanceID;
                }
                room.InstanceCreationOrderIDs.InstanceIDs.Add(id);
            }
        }

        return room;
    }

    /// <summary>
    /// Converts tile data from a 2d array to a 1d list, to be serialized as JSON.
    /// </summary>
    private static List<uint> ConvertTileDataToList(uint[][] tileData)
    {
        if (tileData.Length == 0)
        {
            return [];
        }

        List<uint> tiles = new(tileData.Length * tileData[0].Length);
        foreach (uint[] subarray in tileData)
        {
            tiles.AddRange(subarray);
        }
        return tiles;
    }

    /// <summary>
    /// Converts tile data from a 1d list to a 2d array.
    /// </summary>
    private static uint[][] ConvertListToTileData(uint tilesX, uint tilesY, List<uint> tileData)
    {
        uint[][] newData = new uint[tilesY][];
        int pos = 0;
        int y = 0;
        while (pos < tileData.Count)
        {
            uint[] row = newData[y] = new uint[tilesX];

            int numElemsInRow = Math.Min((int)tilesX, tileData.Count - pos);
            for (int i = 0; i < numElemsInRow; i++)
            {
                row[i] = tileData[pos++];
            }

            y++;
        }
        return newData;
    }
}
