using System.Text.Json.Serialization;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DictionaryEntry))]
internal partial class DataJsonContext : JsonSerializerContext;
