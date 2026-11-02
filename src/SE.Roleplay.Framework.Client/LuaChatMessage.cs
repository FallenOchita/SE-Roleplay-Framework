using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SE.Roleplay.Framework.Client
{
    [DataContract]
    internal sealed class LuaChatMessage
    {
        public const ushort NetworkChannel = 51348;
        public const int CurrentProtocolVersion = 1;
        [DataMember(Order = 1)] public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
        [DataMember(Order = 2)] public string Channel { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string Sender { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string Text { get; set; } = string.Empty;

        public static LuaChatMessage Deserialize(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 1024)
                throw new SerializationException("RP chat packet size is invalid.");
            var serializer = new DataContractJsonSerializer(typeof(LuaChatMessage));
            using (var stream = new MemoryStream(payload, false))
                return (LuaChatMessage)serializer.ReadObject(stream);
        }
    }
}