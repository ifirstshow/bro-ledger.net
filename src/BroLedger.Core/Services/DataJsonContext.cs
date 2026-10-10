using System.Text.Json.Serialization;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

[JsonSourceGenerationOptions(WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DictionaryEntry))]
[JsonSerializable(typeof(AppConfigData))]
[JsonSerializable(typeof(ShareRecord))]
[JsonSerializable(typeof(AnnotationService.AnnotationsFile))]
internal partial class DataJsonContext : JsonSerializerContext;
