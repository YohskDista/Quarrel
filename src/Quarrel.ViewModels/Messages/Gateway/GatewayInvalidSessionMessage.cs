﻿
using DiscordAPI.Gateway.DownstreamEvents;

namespace Quarrel.Messages.Gateway
{
    public sealed class GatewayInvalidSessionMessage
    {
        public GatewayInvalidSessionMessage(InvalidSession packet)
        {
            EventData = packet;
        }

        public InvalidSession EventData {get;}
    }
}
