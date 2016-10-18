﻿using System;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    /// <summary>
    /// Interface for entities that the clients can send information of to the server
    /// </summary>
    interface IClientSerializable
    {
        UInt32 NetStateID { get; }

        void ClientWrite(NetOutgoingMessage msg);
        void ServerRead(NetIncomingMessage msg, Client c);        
    }

    /// <summary>
    /// Interface for entities that the server can send information of to the clients
    /// </summary>
    interface IServerSerializable
    {
        UInt32 NetStateID { get; }

        void ServerWrite(NetOutgoingMessage msg, Client c);
        void ClientRead(NetIncomingMessage msg, float sendingTime);
    }
}
