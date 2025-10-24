﻿using System;
using System.IO;
using System.Text;

namespace Yafc.Parser;

internal static class FactorioPropertyTree {
    private static int ReadSpaceOptimizedUint(BinaryReader reader) {
        byte b = reader.ReadByte();

        if (b < 255) {
            return b;
        }

        return reader.ReadInt32();
    }

    private static string ReadString(BinaryReader reader) {
        if (reader.ReadBoolean()) {
            return "";
        }

        int len = ReadSpaceOptimizedUint(reader);
        byte[] bytes = reader.ReadBytes(len);

        return Encoding.UTF8.GetString(bytes);
    }

    public static object? ReadModSettings(BinaryReader reader, LuaContext context) {
        short major = reader.ReadInt16();
        _ = reader.ReadInt16(); // Minor version
        _ = reader.ReadInt32(); // Patch level
        _ = reader.ReadBoolean();

        if (major is not 1 and not 2) {
            return null;
        }

        return ReadAny(reader, context);
    }

    private static object? ReadAny(BinaryReader reader, LuaContext context) {
        byte type = reader.ReadByte();
        _ = reader.ReadByte();

        switch (type) {
            case 0:
                return null;

            case 1:
                return reader.ReadBoolean();

            case 2:
                return reader.ReadDouble();

            case 3:
                return ReadString(reader);

            case 4:
                int count = reader.ReadInt32();
                var arr = context.NewTable();

                for (int i = 0; i < count; i++) {
                    _ = ReadString(reader);
                    arr[i + 1] = ReadAny(reader, context);
                }

                return arr;

            case 5:
                count = reader.ReadInt32();
                var table = context.NewTable();

                for (int i = 0; i < count; i++) {
                    table[ReadString(reader)] = ReadAny(reader, context);
                }

                return table;

            case 6:
                return reader.ReadInt64();

            case 7:
                return reader.ReadUInt64();

            default:
                throw new NotSupportedException("Unknown type");
        }
    }
}
