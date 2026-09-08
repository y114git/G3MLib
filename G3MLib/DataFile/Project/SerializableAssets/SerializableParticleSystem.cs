using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.Project.SerializableAssets;

internal sealed class SerializableParticleSystem : ISerializableProjectAsset
{
    public string DataName { get; set; }
    [JsonIgnore] public SerializableAssetType AssetType => SerializableAssetType.ParticleSystem;
    [JsonIgnore] public bool IndividualDirectory => false;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int OverrideOrder { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int DrawOrder { get; set; }
    public bool GlobalSpaceParticles { get; set; }
    public List<Emitter> Emitters { get; set; }

    public sealed class Emitter
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public int Mode { get; set; }
        public int EmitCount { get; set; }
        public bool EmitRelative { get; set; }
        public float DelayMin { get; set; }
        public float DelayMax { get; set; }
        public int DelayUnit { get; set; }
        public float IntervalMin { get; set; }
        public float IntervalMax { get; set; }
        public int IntervalUnit { get; set; }
        public int Distribution { get; set; }
        public int Shape { get; set; }
        public float RegionX { get; set; }
        public float RegionY { get; set; }
        public float RegionWidth { get; set; }
        public float RegionHeight { get; set; }
        public float Rotation { get; set; }
        public string Sprite { get; set; }
        public int Texture { get; set; }
        public float FrameIndex { get; set; }
        public bool Animate { get; set; }
        public bool Stretch { get; set; }
        public bool IsRandom { get; set; }
        public uint StartColor { get; set; }
        public uint MidColor { get; set; }
        public uint EndColor { get; set; }
        public bool AdditiveBlend { get; set; }
        public float LifetimeMin { get; set; }
        public float LifetimeMax { get; set; }
        public float ScaleX { get; set; }
        public float ScaleY { get; set; }
        public float SizeMinX { get; set; }
        public float SizeMaxX { get; set; }
        public float SizeIncreaseX { get; set; }
        public float SizeWiggleX { get; set; }
        public float SizeMinY { get; set; }
        public float SizeMaxY { get; set; }
        public float SizeIncreaseY { get; set; }
        public float SizeWiggleY { get; set; }
        public float SpeedMin { get; set; }
        public float SpeedMax { get; set; }
        public float SpeedIncrease { get; set; }
        public float SpeedWiggle { get; set; }
        public float GravityForce { get; set; }
        public float GravityDirection { get; set; }
        public float DirectionMin { get; set; }
        public float DirectionMax { get; set; }
        public float DirectionIncrease { get; set; }
        public float DirectionWiggle { get; set; }
        public float OrientationMin { get; set; }
        public float OrientationMax { get; set; }
        public float OrientationIncrease { get; set; }
        public float OrientationWiggle { get; set; }
        public bool OrientationRelative { get; set; }
        public string SpawnOnDeath { get; set; }
        public int SpawnOnDeathCount { get; set; }
        public string SpawnOnUpdate { get; set; }
        public int SpawnOnUpdateCount { get; set; }
    }

    private GameMakerParticleSystem _dataAsset;

    internal void PopulateFromData(GameMakerParticleSystem system)
    {
        DataName = system.Name.Content;
        OriginX = system.OriginX;
        OriginY = system.OriginY;
        DrawOrder = (int)system.DrawOrder;
        GlobalSpaceParticles = system.GlobalSpaceParticles;
        Emitters = [.. system.Emitters.Where(reference => reference?.Resource is not null).Select(reference => FromData(reference.Resource))];
    }

    public void Serialize(ProjectContext projectContext, string destinationFile)
    {
        using FileStream stream = new(destinationFile, FileMode.Create);
        JsonSerializer.Serialize<ISerializableProjectAsset>(stream, this, ProjectContext.JsonOptions);
    }

    public void PreImport(ProjectContext projectContext)
    {
        _dataAsset = projectContext.Data.ParticleSystems.ByName(DataName) ?? new GameMakerParticleSystem { Name = projectContext.MakeString(DataName) };
        if (!projectContext.Data.ParticleSystems.Contains(_dataAsset))
            projectContext.Data.ParticleSystems.Add(_dataAsset);
        _dataAsset.Emitters.Clear();
        foreach (Emitter emitter in Emitters ?? [])
        {
            GameMakerParticleSystemEmitter dataEmitter = projectContext.Data.ParticleSystemEmitters.ByName(emitter.Name) ?? new() { Name = projectContext.MakeString(emitter.Name) };
            if (!projectContext.Data.ParticleSystemEmitters.Contains(dataEmitter))
                projectContext.Data.ParticleSystemEmitters.Add(dataEmitter);
            _dataAsset.Emitters.Add(new(dataEmitter, projectContext.Data.ParticleSystemEmitters.IndexOf(dataEmitter)));
        }
    }

    public IProjectAsset Import(ProjectContext projectContext)
    {
        _dataAsset.OriginX = OriginX;
        _dataAsset.OriginY = OriginY;
        _dataAsset.DrawOrder = (GameMakerParticleSystem.DrawOrderEnum)DrawOrder;
        _dataAsset.GlobalSpaceParticles = GlobalSpaceParticles;
        foreach (Emitter emitter in Emitters ?? [])
        {
            GameMakerParticleSystemEmitter dataEmitter = projectContext.Data.ParticleSystemEmitters.ByName(emitter.Name) ??
                throw new ProjectException($"Failed to find particle emitter \"{emitter.Name}\" for \"{DataName}\"");
            Apply(projectContext, dataEmitter, emitter);
        }
        return _dataAsset;
    }

    private static Emitter FromData(GameMakerParticleSystemEmitter emitter) => new()
    {
        Name = emitter.Name.Content, Enabled = emitter.Enabled, Mode = (int)emitter.Mode, EmitCount = emitter.EmitCount, EmitRelative = emitter.EmitRelative,
        DelayMin = emitter.DelayMin, DelayMax = emitter.DelayMax, DelayUnit = (int)emitter.DelayUnit, IntervalMin = emitter.IntervalMin, IntervalMax = emitter.IntervalMax, IntervalUnit = (int)emitter.IntervalUnit,
        Distribution = (int)emitter.Distribution, Shape = (int)emitter.Shape, RegionX = emitter.RegionX, RegionY = emitter.RegionY, RegionWidth = emitter.RegionWidth, RegionHeight = emitter.RegionHeight, Rotation = emitter.Rotation,
        Sprite = emitter.Sprite?.Name?.Content, Texture = (int)emitter.Texture, FrameIndex = emitter.FrameIndex, Animate = emitter.Animate, Stretch = emitter.Stretch, IsRandom = emitter.IsRandom,
        StartColor = emitter.StartColor, MidColor = emitter.MidColor, EndColor = emitter.EndColor, AdditiveBlend = emitter.AdditiveBlend, LifetimeMin = emitter.LifetimeMin, LifetimeMax = emitter.LifetimeMax, ScaleX = emitter.ScaleX, ScaleY = emitter.ScaleY,
        SizeMinX = emitter.SizeMinX, SizeMaxX = emitter.SizeMaxX, SizeIncreaseX = emitter.SizeIncreaseX, SizeWiggleX = emitter.SizeWiggleX, SizeMinY = emitter.SizeMinY, SizeMaxY = emitter.SizeMaxY, SizeIncreaseY = emitter.SizeIncreaseY, SizeWiggleY = emitter.SizeWiggleY,
        SpeedMin = emitter.SpeedMin, SpeedMax = emitter.SpeedMax, SpeedIncrease = emitter.SpeedIncrease, SpeedWiggle = emitter.SpeedWiggle, GravityForce = emitter.GravityForce, GravityDirection = emitter.GravityDirection,
        DirectionMin = emitter.DirectionMin, DirectionMax = emitter.DirectionMax, DirectionIncrease = emitter.DirectionIncrease, DirectionWiggle = emitter.DirectionWiggle, OrientationMin = emitter.OrientationMin, OrientationMax = emitter.OrientationMax,
        OrientationIncrease = emitter.OrientationIncrease, OrientationWiggle = emitter.OrientationWiggle, OrientationRelative = emitter.OrientationRelative,
        SpawnOnDeath = emitter.SpawnOnDeath?.Name?.Content, SpawnOnDeathCount = emitter.SpawnOnDeathCount, SpawnOnUpdate = emitter.SpawnOnUpdate?.Name?.Content, SpawnOnUpdateCount = emitter.SpawnOnUpdateCount
    };

    private static void Apply(ProjectContext context, GameMakerParticleSystemEmitter target, Emitter source)
    {
        target.Enabled = source.Enabled; target.Mode = (GameMakerParticleSystemEmitter.EmitMode)source.Mode; target.EmitCount = source.EmitCount; target.EmitRelative = source.EmitRelative;
        target.DelayMin = source.DelayMin; target.DelayMax = source.DelayMax; target.DelayUnit = (GameMakerParticleSystemEmitter.TimeUnitEnum)source.DelayUnit; target.IntervalMin = source.IntervalMin; target.IntervalMax = source.IntervalMax; target.IntervalUnit = (GameMakerParticleSystemEmitter.TimeUnitEnum)source.IntervalUnit;
        target.Distribution = (GameMakerParticleSystemEmitter.DistributionEnum)source.Distribution; target.Shape = (GameMakerParticleSystemEmitter.EmitterShape)source.Shape; target.RegionX = source.RegionX; target.RegionY = source.RegionY; target.RegionWidth = source.RegionWidth; target.RegionHeight = source.RegionHeight; target.Rotation = source.Rotation;
        target.Sprite = string.IsNullOrWhiteSpace(source.Sprite) ? null : context.FindSprite(source.Sprite, null); target.Texture = (GameMakerParticleSystemEmitter.TextureEnum)source.Texture; target.FrameIndex = source.FrameIndex; target.Animate = source.Animate; target.Stretch = source.Stretch; target.IsRandom = source.IsRandom;
        target.StartColor = source.StartColor; target.MidColor = source.MidColor; target.EndColor = source.EndColor; target.AdditiveBlend = source.AdditiveBlend; target.LifetimeMin = source.LifetimeMin; target.LifetimeMax = source.LifetimeMax; target.ScaleX = source.ScaleX; target.ScaleY = source.ScaleY;
        target.SizeMinX = source.SizeMinX; target.SizeMaxX = source.SizeMaxX; target.SizeIncreaseX = source.SizeIncreaseX; target.SizeWiggleX = source.SizeWiggleX; target.SizeMinY = source.SizeMinY; target.SizeMaxY = source.SizeMaxY; target.SizeIncreaseY = source.SizeIncreaseY; target.SizeWiggleY = source.SizeWiggleY;
        target.SpeedMin = source.SpeedMin; target.SpeedMax = source.SpeedMax; target.SpeedIncrease = source.SpeedIncrease; target.SpeedWiggle = source.SpeedWiggle; target.GravityForce = source.GravityForce; target.GravityDirection = source.GravityDirection;
        target.DirectionMin = source.DirectionMin; target.DirectionMax = source.DirectionMax; target.DirectionIncrease = source.DirectionIncrease; target.DirectionWiggle = source.DirectionWiggle; target.OrientationMin = source.OrientationMin; target.OrientationMax = source.OrientationMax; target.OrientationIncrease = source.OrientationIncrease; target.OrientationWiggle = source.OrientationWiggle; target.OrientationRelative = source.OrientationRelative;
        target.SpawnOnDeath = string.IsNullOrWhiteSpace(source.SpawnOnDeath) ? null : context.Data.ParticleSystemEmitters.ByName(source.SpawnOnDeath); target.SpawnOnDeathCount = source.SpawnOnDeathCount; target.SpawnOnUpdate = string.IsNullOrWhiteSpace(source.SpawnOnUpdate) ? null : context.Data.ParticleSystemEmitters.ByName(source.SpawnOnUpdate); target.SpawnOnUpdateCount = source.SpawnOnUpdateCount;
    }
}
