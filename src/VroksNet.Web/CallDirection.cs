namespace VroksNet.Web;

public enum CallDirection
{
    InboundHttpRequest,
    OutboundBrokerPublish,
    OutboundHttpRequest,
    InboundBrokerMessage,
    InboundGrpcCall,
    OutboundGrpcCall
}
