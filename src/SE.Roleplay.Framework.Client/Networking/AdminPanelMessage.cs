using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SE.Roleplay.Framework.Client.Networking
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
        [DataMember(Order = 12)] public int MemberCount { get; set; }

        public static byte[] Serialize(AdminPanelMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var serializer = new DataContractJsonSerializer(typeof(AdminPanelMessage));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, message);
                return stream.ToArray();
            }
        }

        public static AdminPanelMessage Deserialize(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length == 0 || payload.Length > 16 * 1024)
                throw new SerializationException("Admin panel packet size is invalid.");

            var serializer = new DataContractJsonSerializer(typeof(AdminPanelMessage));
            using (var stream = new MemoryStream(payload, false))
                return (AdminPanelMessage)serializer.ReadObject(stream)!;
        }
    }
}
