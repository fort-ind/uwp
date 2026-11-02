using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public static class JsonText
    {
        public static JToken Parse(string text)
        {
            using (var reader = Reader(text))
            {
                return JToken.Load(reader);
            }
        }

        public static JObject ParseObject(string text)
        {
            using (var reader = Reader(text))
            {
                return JObject.Load(reader);
            }
        }

        public static bool TryParse(string text, out JToken token)
        {
            token = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                token = Parse(text);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static bool TryParseObject(string text, out JObject obj)
        {
            obj = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                obj = ParseObject(text);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static string Write(JToken token)
        {
            return token.ToString(Formatting.None);
        }

        public static bool IsNumber(JToken token)
        {
            return token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float);
        }

        private static JsonTextReader Reader(string text)
        {
            return new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
        }
    }
}
