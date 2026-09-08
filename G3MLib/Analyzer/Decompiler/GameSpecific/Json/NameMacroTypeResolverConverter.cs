/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G3MLib.Analyzer.Decompiler.GameSpecific.Json;

internal static class NameMacroTypeResolverConverter
{
    public static void ReadContents(ref Utf8JsonReader reader, JsonSerializerOptions options, NameMacroTypeResolver existing)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }
            string propertyName = reader.GetString() ?? throw new JsonException();

            switch (propertyName)
            {
                case "Variables":
                    reader.Read();
                    ReadMacroNameList(ref reader, options, existing.DefineVariableType);
                    break;
                case "FunctionArguments":
                    reader.Read();
                    ReadMacroNameList(ref reader, options, existing.DefineFunctionArgumentsType);
                    break;
                case "FunctionReturn":
                    reader.Read();
                    ReadMacroNameList(ref reader, options, existing.DefineFunctionReturnType);
                    break;
                default:
                    throw new JsonException($"Unknown property name {propertyName}");
            }
        }

        throw new JsonException();
    }

    private static void ReadMacroNameList(ref Utf8JsonReader reader, JsonSerializerOptions options, Action<string, IMacroType?> define)
    {
        MacroTypeJson.ReadObject(ref reader, options, define);
    }
}
