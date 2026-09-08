namespace VroksNet.Domain.CallRecords;

public enum CallDirection
{
    /// <summary>An inbound REST call handled by a mock endpoint.</summary>
    InboundHttpRequest,

    /// <summary>An outbound message published to a broker for an AsyncAPI channel.</summary>
    OutboundBrokerPublish
}
