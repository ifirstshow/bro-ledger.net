using System.Text.Json.Serialization;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

[JsonSourceGenerationOptions(WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DictionaryEntry))]
[JsonSerializable(typeof(AppConfigData))]
[JsonSerializable(typeof(ShareRecord))]
[JsonSerializable(typeof(AnnotationService.AnnotationsFile))]
[JsonSerializable(typeof(StatModifier))]
[JsonSerializable(typeof(StatModifierService.Wrapper))]
[JsonSerializable(typeof(Role))]
[JsonSerializable(typeof(RolePack))]
[JsonSerializable(typeof(RoleService.Wrapper), TypeInfoPropertyName = "RoleServiceWrapper")]
[JsonSerializable(typeof(WeaponStats))]
internal partial class DataJsonContext : JsonSerializerContext;
