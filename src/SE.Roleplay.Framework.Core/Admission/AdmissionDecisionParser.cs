using Newtonsoft.Json.Linq;

namespace SE.Roleplay.Framework.Core.Admission
{
    /// <summary>Strictly parses the directory admission response. Missing or malformed decisions fail closed.</summary>
    public static class AdmissionDecisionParser
    {
        /// <summary>
        /// Attempts to read the admission decision from a directory response.
        /// </summary>
        /// <param name="json">The JSON response body.</param>
        /// <param name="shouldAdmit">Receives the parsed decision, or <see langword="false" /> when parsing fails.</param>
        /// <returns><see langword="true" /> when a Boolean admission decision is present; otherwise, <see langword="false" />.</returns>
        public static bool TryParse(string? json, out bool shouldAdmit)
        {
            shouldAdmit = false;
            if (json is null || string.IsNullOrWhiteSpace(json))
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
