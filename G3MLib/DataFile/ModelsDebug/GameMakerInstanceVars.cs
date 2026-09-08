using System;

namespace G3MLib.DataFile.ModelsDebug
{
    public class GameMakerInstanceVars : GameMakerResource
    {
        public void Serialize(GameMakerWriter writer)
        {
            throw new NotSupportedException("Non-empty INST debug records are not documented by GameMaker and no supported sample contains one.");
        }

        public void Unserialize(GameMakerReader reader)
        {
            throw new NotSupportedException("Non-empty INST debug records are not documented by GameMaker and no supported sample contains one.");
        }
    }
}
