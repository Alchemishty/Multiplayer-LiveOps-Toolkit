using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace MultiplayerLiveOpsToolkit.Equipment
{
    /// <summary>Sanitized observer state only: revision and equipped slot/ID pairs.</summary>
    public static class EquipmentPublicState
    {
        public static string Serialize(EquipmentLoadout loadout)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            return JsonConvert.SerializeObject(new { revision = loadout.Revision, items = loadout.ItemsBySlot });
        }
        public static EquipmentLoadout Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 16384) throw new FormatException("Public equipment state is missing or oversized.");
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 3, DateParseHandling = DateParseHandling.None })
            {
                var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read() || root.Count != 2 || root["revision"]?.Type != JTokenType.Integer || !(root["items"] is JObject items)) throw new FormatException("Public equipment state requires revision and items only.");
                var active = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in items.Properties()) { if (item.Value.Type != JTokenType.String) throw new FormatException("Public cosmetic IDs must be strings."); active.Add(item.Name, (string)item.Value); }
                return new EquipmentLoadout((long)root["revision"], active);
            }
        }
    }
}
