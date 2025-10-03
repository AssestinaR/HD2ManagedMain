using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure.Json;

// 作用：为强类型 Guid 标识（如 ModNodeId/ProfileId）提供 JSON 序列化转换器（含字典键支持）。
// Purpose: JSON converters for strongly-typed Guid IDs (e.g., ModNodeId/ProfileId), including dictionary key support.
internal sealed class ModNodeIdJsonConverter : JsonConverter<ModNodeId>
{
	public override ModNodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var s = reader.GetString();
		return Guid.TryParse(s, out var g) ? new ModNodeId(g) : default;
	}

	public override void Write(Utf8JsonWriter writer, ModNodeId value, JsonSerializerOptions options)
		=> writer.WriteStringValue(value.Value);

	public override ModNodeId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> Read(ref reader, typeToConvert, options);

	public override void WriteAsPropertyName(Utf8JsonWriter writer, ModNodeId value, JsonSerializerOptions options)
		=> writer.WritePropertyName(value.Value.ToString());
}

internal sealed class ProfileIdJsonConverter : JsonConverter<ProfileId>
{
	public override ProfileId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var s = reader.GetString();
		return Guid.TryParse(s, out var g) ? new ProfileId(g) : default;
	}

	public override void Write(Utf8JsonWriter writer, ProfileId value, JsonSerializerOptions options)
		=> writer.WriteStringValue(value.Value);

	public override ProfileId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> Read(ref reader, typeToConvert, options);

	public override void WriteAsPropertyName(Utf8JsonWriter writer, ProfileId value, JsonSerializerOptions options)
		=> writer.WritePropertyName(value.Value.ToString());
}
