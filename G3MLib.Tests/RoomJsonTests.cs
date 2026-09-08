using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using G3MLib.DataFile;
using Xunit;

namespace G3MLib.Tests;

public class RoomJsonTests
{
    [Theory("RoomJsonTests.cs", 10)]
    [InlineData(["SpriteAssetInstance", "Sprite"])]
    [InlineData(["SequenceAssetInstance", "Sequence"])]
    public void AssetInstancesKeepTheirJsonPropertyOrder(string typeName, string resourceProperty)
    {
        Type type = typeof(GameMakerData).Assembly.GetType("G3MLib.DataFile.Project.SerializableAssets.SerializableRoom+AssetsLayer").GetNestedType(typeName, BindingFlags.Public);
        object instance = Activator.CreateInstance(type);
        type.GetProperty("Name").SetValue(instance, "instance");
        type.GetProperty(resourceProperty).SetValue(instance, "resource");
        string[] propertyOrder = [.. (from property in JsonDocument.Parse(JsonSerializer.Serialize(instance, type)).RootElement.EnumerateObject()
            select property.Name)];
        InlineArray11<string> buffer = default;
        buffer[0] = "Name";
        buffer[1] = resourceProperty;
        buffer[2] = "X";
        buffer[3] = "Y";
        buffer[4] = "ScaleX";
        buffer[5] = "ScaleY";
        buffer[6] = "Color";
        buffer[7] = "AnimationSpeed";
        buffer[8] = "AnimationSpeedType";
        buffer[9] = "FrameIndex";
        buffer[10] = "Rotation";
        Assert.Equal(buffer, propertyOrder);
    }
}
