using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yafc.Model;
using Yafc.UI;

namespace Yafc.Blueprints;

[Serializable]
public class BlueprintString(string blueprintName) {
    public Blueprint blueprint { get; } = new Blueprint(blueprintName);
    private static readonly byte[] header = [0x78, 0xDA];
    private static readonly JsonSerializerOptions jsonSerializerOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToBpString() {
        if (InputSystem.Instance.control) {
            return ToJson();
        }

        byte[] sourceBytes = JsonSerializer.SerializeToUtf8Bytes(this, jsonSerializerOptions);
        using MemoryStream memory = new MemoryStream();
        memory.Write(header);
        using (DeflateStream compress = new DeflateStream(memory, CompressionLevel.Optimal, true)) {
            compress.Write(sourceBytes);
        }

        memory.Write(GetChecksum(sourceBytes, sourceBytes.Length));

        return "0" + Convert.ToBase64String(memory.ToArray());
    }

    private static byte[] GetChecksum(byte[] buffer, int length) {
        int a = 1, b = 0;

        for (int counter = 0; counter < length; ++counter) {
            a = (a + buffer[counter]) % 65521;
            b = (b + a) % 65521;
        }

        int checksum = (b * 65536) + a;
        byte[] intBytes = BitConverter.GetBytes(checksum);
        Array.Reverse(intBytes);

        return intBytes;
    }

    public string ToJson() {
        byte[] sourceBytes = JsonSerializer.SerializeToUtf8Bytes(this, jsonSerializerOptions);
        using MemoryStream memory = new MemoryStream(sourceBytes);
        using StreamReader reader = new StreamReader(memory);

        return reader.ReadToEnd();
    }
}

[Serializable]
public class Blueprint(string label) {
    public const long VERSION = 562949956632577;

    public string item { get; set; } = "blueprint";
    public string label { get; set; } = label;
    public List<BlueprintEntity> entities { get; } = [];
    public List<BlueprintIcon> icons { get; } = [];
    public long version { get; set; } = VERSION;
}

[Serializable]
public class BlueprintIcon {
    public int index { get; set; }
    public BlueprintSignal signal { get; set; } = new BlueprintSignal();
}

[Serializable]
public class BlueprintSignal {
    public string? name { get; set; }
    public string? type { get; set; }
    public string? quality { get; set; }

    public void Set(IObjectWithQuality<Goods> goods) {
        if (goods.target is Special sp) {
            type = "virtual";
            name = sp.virtualSignal;
        }
        else if (goods.target is Fluid fluid) {
            type = "fluid";
            name = fluid.originalName;
        }
        else {
            type = "item";
            name = goods.target.name;
            quality = goods.quality.name;
        }
    }
}

[Serializable]
public class BlueprintEntity {
    [JsonPropertyName("entity_number")]
    public int index { get; set; }

    public string? name { get; set; }
    public string? quality { get; set; }
    public BlueprintPosition position { get; set; } = new BlueprintPosition();
    public int direction { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? recipe { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? recipe_quality { get; set; }

    [JsonPropertyName("control_behavior")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BlueprintControlBehavior? controlBehavior { get; set; }
    public BlueprintConnection? connections { get; set; }

    [JsonPropertyName("request_filters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BlueprintRequestFilterSections? requestFilters { get; set; }
    public List<BlueprintItem> items { get; } = [];
    [JsonPropertyName("burner_fuel_inventory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BlueprintRequestFilterSection? burnerFuelInventory { get; set; }

    public void SetFuel(string name, string? quality = null) {
        BlueprintRequestFilter fuelFilter = new() {
            index = 1,
            comparator = "=",
            name = name,
            quality = quality,
            count = 1
        };

        burnerFuelInventory = new BlueprintRequestFilterSection();
        burnerFuelInventory.filters.Add(fuelFilter);
    }

    public void Connect(BlueprintEntity other, bool red = true, bool secondPort = false, bool targetSecond = false) {
        ConnectSingle(other, red, secondPort, targetSecond);
        other.ConnectSingle(this, red, targetSecond, secondPort);
    }

    private void ConnectSingle(BlueprintEntity other, bool red = true, bool secondPort = false, bool targetSecond = false) {
        connections ??= new BlueprintConnection();
        BlueprintConnectionPoint port;
        if (secondPort) {
            port = connections.p2 ?? (connections.p2 = new BlueprintConnectionPoint());
        }
        else {
            port = connections.p1 ?? (connections.p1 = new BlueprintConnectionPoint());
        }

        var list = red ? port.red : port.green;
        list.Add(new BlueprintConnectionData { entityId = other.index, circuitId = targetSecond ? 2 : 1 });
    }
}

[Serializable]
public class BlueprintRequestFilterSections {
    public List<BlueprintRequestFilterSection> sections { get; } = [];
}

[Serializable]
public class BlueprintRequestFilterSection {
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? index { get; set; }
    public List<BlueprintRequestFilter> filters { get; } = [];
}

[Serializable]
public class BlueprintRequestFilter {
    public string? name { get; set; }
    public string? quality { get; set; }
    public string? comparator { get; set; }
    public int index { get; set; }
    public int count { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? max_count { get; set; }
}

[Serializable]
public class BlueprintConnection {
    [JsonPropertyName("1")] public BlueprintConnectionPoint? p1 { get; set; }
    [JsonPropertyName("2")] public BlueprintConnectionPoint? p2 { get; set; }
}

[Serializable]
public class BlueprintConnectionPoint {
    public List<BlueprintConnectionData> red { get; } = [];
    public List<BlueprintConnectionData> green { get; } = [];
}

[Serializable]
public class BlueprintConnectionData {
    [JsonPropertyName("entity_id")] public int entityId { get; set; }
    [JsonPropertyName("circuit_id")] public int circuitId { get; set; } = 1;
}

[Serializable]
public class BlueprintPosition {
    public float x { get; set; }
    public float y { get; set; }
}

[Serializable]
public class BlueprintControlBehavior {
    public List<BlueprintControlFilter> filters { get; } = [];
}

[Serializable]
public class BlueprintControlFilter {
    public BlueprintSignal signal { get; set; } = new BlueprintSignal();
    public int index { get; set; }
    public int count { get; set; }
}

[Serializable]
public class BlueprintItem {
    // For 1.1 blueprints:
    public string? item { get; set; }
    public int count { get; set; }
    // For 2.0 blueprints:
    public BlueprintId id { get; } = new();
    public BlueprintItemInventory items { get; } = new();
}

[Serializable]
public class BlueprintId {
    public string? name { get; set; }
    public string? quality { get; set; }
}

[Serializable]
public class BlueprintItemInventory {
    [JsonPropertyName("in_inventory")] public List<BlueprintInventoryItem> inInventory { get; } = [];
}

[Serializable]
public class BlueprintInventoryItem {
    public int inventory { get; } = 4; // unknown magic number (probably 'modules', needs more investigating)
    public int stack { get; set; }
}
