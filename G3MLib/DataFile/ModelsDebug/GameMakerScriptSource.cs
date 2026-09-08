using G3MLib.DataFile.Models;

namespace G3MLib.DataFile.ModelsDebug
{
    // The index of this object matches index in CODE in main data file
    public class GameMakerScriptSource : GameMakerResource
    {
        public GameMakerString SourceCode { get; set; }

        public void Serialize(GameMakerWriter writer)
        {
            writer.WriteGameMakerString(SourceCode);
        }

        public void Unserialize(GameMakerReader reader)
        {
            SourceCode = reader.ReadGameMakerString();
        }
    }
}
