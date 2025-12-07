using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RimLife.Networking
{
    [DataContract]
    public class Player2Request
    {
        [DataMember(Name = "messages")]
        public List<Message> Messages { get; set; } = new List<Message>();

        [DataMember(Name = "stream", EmitDefaultValue = false)]
        public bool? Stream { get; set; }
    }

    [DataContract]
    public class Message
    {
        [DataMember(Name = "role")]
        public string Role { get; set; }

        [DataMember(Name = "content")]
        public string Content { get; set; }
    }

    [DataContract]
    public class Player2Response
    {
        [DataMember(Name = "choices")]
        public List<Choice> Choices { get; set; }
    }

    [DataContract]
    public class Choice
    {
        [DataMember(Name = "message")]
        public Message Message { get; set; }

        [DataMember(Name = "delta")]
        public Delta Delta { get; set; }
    }

    [DataContract]
    public class Delta
    {
        [DataMember(Name = "content")]
        public string Content { get; set; }
    }
    
    [DataContract]
    public class Player2StreamChunk
    {
        [DataMember(Name = "choices")]
        public List<Choice> Choices { get; set; }
    }
}
