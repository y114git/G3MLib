using System.Collections.Generic;
using System;
using System.IO;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.ModelsDebug;

namespace G3MLib.DataFile
{
    public class GameMakerDebugData
    {
        public GameMakerDebugFORM FORM { get; set; }

        public IList<GameMakerScriptSource> SourceCode => FORM.SCPT?.List;
        public IList<GameMakerDebugInfo> DebugInfo => FORM.DBGI?.List;
        public IList<GameMakerInstanceVars> InstanceVars => FORM.INST?.List;
        public IList<GameMakerCodeLocals> LocalVars => FORM.LOCL?.List;
        public IList<GameMakerString> Strings => FORM.STRG?.List;

        public static GameMakerDebugData CreateNew()
        {
            GameMakerDebugData data = new GameMakerDebugData();
            data.FORM = new GameMakerDebugFORM();
            data.FORM.Chunks["SCPT"] = new GameMakerDebugChunkSCPT();
            GameMakerDebugChunkDBGI debugInfo = new();
            debugInfo.CodeEntryIndices.Add(0);
            data.FORM.Chunks["DBGI"] = debugInfo;
            data.FORM.Chunks["INST"] = new GameMakerDebugChunkINST();
            data.FORM.Chunks["LOCL"] = new GameMakerDebugChunkLOCL();
            data.FORM.Chunks["STRG"] = new GameMakerDebugChunkSTRG();
            return data;
        }
    }

    public static class GameMakerDebugIO
    {
        public static GameMakerDebugData Read(Stream stream,
            GameMakerReader.WarningHandlerDelegate warningHandler = null,
            GameMakerReader.MessageHandlerDelegate messageHandler = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            GameMakerReader reader = new(stream, warningHandler, messageHandler);
            GameMakerDebugData data = reader.ReadGameMakerDebugData();
            reader.ThrowIfUnreadObjects();
            return data;
        }

        public static void Write(Stream stream, GameMakerDebugData data,
            GameMakerWriter.MessageHandlerDelegate messageHandler = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(data.FORM);

            GameMakerWriter writer = new(stream, messageHandler);
            writer.IsDebugData = true;
            foreach (var chunk in data.FORM.Chunks)
                writer.LastChunkName = chunk.Key;
            writer.Write(data.FORM);
            writer.ThrowIfUnwrittenObjects();
            writer.Flush();
        }
    }
}
