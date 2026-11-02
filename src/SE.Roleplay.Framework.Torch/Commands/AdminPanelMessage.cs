using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SE.Roleplay.Framework.Torch.Commands
{
    [DataContract]
    internal sealed class AdminPanelMessage
    {
        public const int CurrentProtocolVersion = 1;

        [DataMember(Order = 1)] public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
        [DataMember(Order = 2)] public Guid RequestId { get; set; } = Guid.NewGuid();
        [DataMember(Order = 3)] public string Operation { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string SteamId64 { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string Note { get; set; } = string.Empty;
        [DataMember(Order = 6)] public bool Enabled { get; set; }
        [DataMember(Order = 7)] public bool Success { get; set; }
        [DataMember(Order = 8)] public string ErrorCode { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string Message { get; set; } = string.Empty;
        [DataMember(Order = 10)] public bool AllowlistEnabled { get; set; }
        [DataMember(Order = 11)] public string[] Members { get; set; } = Array.Empty<string>();

        public static AdminPanelMessage Deserialize(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 3500)
                throw new SerializationException("Admin panel request size is invalid.");

            var serializer = new DataContractJsonSerializer(typeof(AdminPanelMessage));
            using (var stream = new MemoryStream(payload, false))
                return (AdminPanelMessage)serializer.ReadObject(stream)!;
        }

        public byte[] Serialize()
        {
            var serializer = new DataContractJsonSerializer(typeof(AdminPanelMessage));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return stream.ToArray();
            }
        }
    }
}
