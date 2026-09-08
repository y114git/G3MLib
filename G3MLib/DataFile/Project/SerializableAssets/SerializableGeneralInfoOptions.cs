using System.Text.Json.Serialization;
using G3MLib.DataFile.Models;
using static G3MLib.DataFile.Models.GameMakerGeneralInfo;
using static G3MLib.DataFile.Models.GameMakerOptions;

namespace G3MLib.DataFile.Project.SerializableAssets;

internal sealed class SerializableGeneralInfoOptions
{
    /// <inheritdoc cref="GameMakerGeneralInfo.FileName"/>
    public string FileName { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.Config"/>
    public string Config { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.GameID"/>
    public uint? GameID { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.Name"/>
    public string Name { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.DefaultWindowWidth"/>
    public uint? DefaultWindowWidth { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.DefaultWindowHeight"/>
    public uint? DefaultWindowHeight { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.Info"/>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InfoFlags? GeneralInfo { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.DisplayName"/>
    public string DisplayName { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.SteamAppID"/>
    public int? SteamAppID { get; set; }

    /// <inheritdoc cref="GameMakerGeneralInfo.GMS2FPS"/>
    public float? GMS2FPS { get; set; }

    /// <inheritdoc cref="GameMakerOptions.Info"/>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OptionsFlags? OptionsInfo { get; set; }

    /// <inheritdoc cref="GameMakerOptions.VertexSync"/>
    public uint? VertexSync { get; set; }

    /// <summary>
    /// Imports the data from this object into the given data file.
    /// </summary>
    public void ImportTo(GameMakerData data)
    {
        GameMakerGeneralInfo generalInfo = data.GeneralInfo;
        if (FileName is not null)
        {
            generalInfo.FileName = data.Strings.MakeString(FileName);
        }
        if (Config is not null)
        {
            generalInfo.Config = data.Strings.MakeString(Config);
        }
        if (GameID is not null)
        {
            generalInfo.GameID = GameID.Value;
        }
        if (Name is not null)
        {
            generalInfo.Name = data.Strings.MakeString(Name);
        }
        if (DefaultWindowWidth is not null)
        {
            generalInfo.DefaultWindowWidth = DefaultWindowWidth.Value;
        }
        if (DefaultWindowHeight is not null)
        {
            generalInfo.DefaultWindowHeight = DefaultWindowHeight.Value;
        }
        if (GeneralInfo is not null)
        {
            generalInfo.Info = GeneralInfo.Value;
        }
        if (DisplayName is not null)
        {
            generalInfo.DisplayName = data.Strings.MakeString(DisplayName);
        }
        if (SteamAppID is not null)
        {
            generalInfo.SteamAppID = SteamAppID.Value;
        }
        if (GMS2FPS is not null)
        {
            generalInfo.GMS2FPS = GMS2FPS.Value;
        }

        GameMakerOptions options = data.Options;
        if (OptionsInfo is not null)
        {
            options.Info = OptionsInfo.Value;
        }
        if (VertexSync is not null)
        {
            options.VertexSync = VertexSync.Value;
        }
    }
}
