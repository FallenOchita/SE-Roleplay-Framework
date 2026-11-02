using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SE.Roleplay.Framework.Torch
{
    [DataContract]
    internal sealed class LuaChatNetworkMessage
    {
        public const int CurrentProtocolVersion = 1;
        [DataMember(Order = 1)] public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
        [DataMember(Order = 2)] public string Channel { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string Sender { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string Text { get; set; } = string.Empty;

        public byte[] Serialize()
        {
            var serializer = new DataContractJsonSerializer(typeof(LuaChatNetworkMessage));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return stream.ToArray();
            }
        }
    }
}