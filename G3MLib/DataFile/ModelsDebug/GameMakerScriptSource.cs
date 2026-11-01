namespace G3MLib.DataFile.ModelsDebug;

// The index of this object matches index in CODE in main data file
public class GameMakerScriptSource : GameMakerResource
{
    public GameMakerString SourceCode { get; set; }

    public void Serialize(GameMakerWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteGameMakerString(SourceCode);
    }

    public void Unserialize(GameMakerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        SourceCode = reader.ReadGameMakerString();
    }
}
