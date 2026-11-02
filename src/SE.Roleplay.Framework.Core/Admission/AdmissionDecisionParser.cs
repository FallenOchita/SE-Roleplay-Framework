using Newtonsoft.Json.Linq;

namespace SE.Roleplay.Framework.Core.Admission
{
    /// <summary>Strictly parses the directory admission response. Missing or malformed decisions fail closed.</summary>
    public static class AdmissionDecisionParser
    {
        public static bool TryParse(string? json, out bool shouldAdmit)
        {
            shouldAdmit = false;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var document = JObject.Parse(json);
                var value = document["shouldAdmit"];
                if (value == null || value.Type != JTokenType.Boolean)
                    return false;

                shouldAdmit = value.Value<bool>();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
