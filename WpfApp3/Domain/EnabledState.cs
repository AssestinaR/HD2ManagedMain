using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiberTeaManager
{
    /// <summary>
    /// ÆôÓÃ×´Ì¬: Disabled=0, Enabled=1, Partial=2
    /// </summary>
    public enum EnabledState { Disabled = 0, Enabled = 1, Partial = 2 }

    /// <summary>
    /// ¼æÈÝ¾É JSON(0/1/2 »ò true/false »ò×Ö·û´®) µÄ×ª»»Æ÷
    /// </summary>
    public sealed class EnabledStateJsonConverter : JsonConverter<EnabledState>
    {
        public override EnabledState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return reader.TokenType switch
                {
                    JsonTokenType.Number => ToState(reader.GetInt32()),
                    JsonTokenType.True => EnabledState.Enabled,
                    JsonTokenType.False => EnabledState.Disabled,
                    JsonTokenType.String => ParseString(reader.GetString()),
                    _ => EnabledState.Disabled
                };
            }
            catch { return EnabledState.Disabled; }
        }

        public override void Write(Utf8JsonWriter writer, EnabledState value, JsonSerializerOptions options)
            => writer.WriteNumberValue((int)value);

        private static EnabledState ParseString(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return EnabledState.Disabled;
            if (int.TryParse(s, out var n)) return ToState(n);
            return s.Trim().ToLowerInvariant() switch
            {
                "enabled" => EnabledState.Enabled,
                "partial" => EnabledState.Partial,
                "true" => EnabledState.Enabled,
                _ => EnabledState.Disabled
            };
        }
        private static EnabledState ToState(int n) => n switch { 1 => EnabledState.Enabled, 2 => EnabledState.Partial, _ => EnabledState.Disabled };
    }
}
