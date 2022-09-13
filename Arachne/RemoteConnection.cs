using System.Net;
using Arachne.Packets;

namespace Arachne;

public enum ConnectionState
{
    Disconnected,
    Requested,
    WaitingForChallengeResponse,
    AuthenticatedConnected,
}

public enum ConnectionTransition
{
    CRReceived,
    CHSent,
    CHRReceived,
    CRSSent,
    CTReceived,
    CTSent,
    TimedOut
}

public class RemoteConnection : FSM<ConnectionState, ConnectionTransition>
{
    private Server _server;
    private ConnectionChallenge? _sentChallenge;
    private ulong _nextSequenceNumber = 1;
    private ulong _lastReceivedSequenceNumber = 0;

    private Dictionary<ulong, (DateTime, ProtocolPacket)> _unackedReliableSequenceNumbers = new();

    public IPEndPoint RemoteEndPoint { get; private set; }
    public bool IsConnected => base.CurrentState == ConnectionState.AuthenticatedConnected;
    public ulong ClientID { get; private set; }

    public RemoteConnection(Server owner, IPEndPoint endpoint) : base(ConnectionState.Disconnected)
    {
        this._server = owner;
        this.RemoteEndPoint = endpoint;

        // Connection initialization
        base.AddTransition(ConnectionState.Disconnected, ConnectionTransition.CRReceived, ConnectionState.Requested);
        base.AddTransition(ConnectionState.Requested, ConnectionTransition.CHSent, ConnectionState.WaitingForChallengeResponse);
        base.AddTransition(ConnectionState.WaitingForChallengeResponse, ConnectionTransition.CHRReceived, ConnectionState.AuthenticatedConnected);
        base.AddTransition(ConnectionState.AuthenticatedConnected, ConnectionTransition.CRSSent, ConnectionState.AuthenticatedConnected);
        base.AddTransition(ConnectionState.AuthenticatedConnected, ConnectionTransition.CTSent, ConnectionState.Disconnected);

        // Connection termination
        base.AddTransition(ConnectionState.AuthenticatedConnected, ConnectionTransition.CTReceived, ConnectionState.Disconnected);

        // From all states, we can go to Disconnected if we time out
        base.AddTransition(ConnectionState.Disconnected, ConnectionTransition.TimedOut, ConnectionState.Disconnected);
        base.AddTransition(ConnectionState.Requested, ConnectionTransition.TimedOut, ConnectionState.Disconnected);
        base.AddTransition(ConnectionState.WaitingForChallengeResponse, ConnectionTransition.TimedOut, ConnectionState.Disconnected);
        base.AddTransition(ConnectionState.AuthenticatedConnected, ConnectionTransition.TimedOut, ConnectionState.Disconnected);
    }

    internal ulong GetNextSequenceNumber()
    {
        return this._nextSequenceNumber++;
    }

    internal void SetLastReceivedSequenceNumber(ulong sequenceNumber)
    {
        this._lastReceivedSequenceNumber = sequenceNumber;
    }

    internal bool FollowsOrder(ulong sequenceNumber)
    {
        return sequenceNumber > this._lastReceivedSequenceNumber;
    }

    internal void RegisterReliablePacketSend(ProtocolPacket packet)
    {
        this._unackedReliableSequenceNumbers.Add(packet.SequenceNumber, (DateTime.Now, packet));
    }

    internal void RegisterResentReliablePacket(ulong sequenceNumber)
    {
        this._unackedReliableSequenceNumbers[sequenceNumber] = (DateTime.Now, this._unackedReliableSequenceNumbers[sequenceNumber].Item2);
    }

    internal void RegisterReliableSequenceNumberAcked(params ulong[] sequenceNumbers)
    {
        foreach (var sequenceNumber in sequenceNumbers)
        {
            if (this._unackedReliableSequenceNumbers.ContainsKey(sequenceNumber))
            {
                this._unackedReliableSequenceNumbers.Remove(sequenceNumber);
            }
        }
    }

    internal List<(ProtocolPacket, IPEndPoint)> GetUnackedPacketsOlderThan(TimeSpan timeout)
    {
        var now = DateTime.Now;
        var packets = new List<(ProtocolPacket, IPEndPoint)>();
        var dict = this._unackedReliableSequenceNumbers.ToDictionary(x => x.Key, x => x.Value);
        foreach (var kvp in dict)
        {
            if (now - kvp.Value.Item1 > timeout)
            {
                packets.Add((kvp.Value.Item2, this.RemoteEndPoint));
            }
        }

        return packets;
    }

    internal async Task ReceiveProtocolPacket(ProtocolPacket packet)
    {
        if (packet.PacketType == ProtocolPacketType.ConnectionRequest)
        {
            // TODO: Check stuff
            var cr = (ConnectionRequest)packet;
            this.ClientID = cr.ClientID;

            this._server.TriggerConnRequestedEvent(this);

            if (cr.ProtocolID != this._server._protocolID)
            {
                this._server.TriggerConnFailedAuthEvent(this);
                return;
                // TODO: Remove connection immediately.
            }

            if (this.TryMoveNext(ConnectionTransition.CRReceived, out var newState))
            {
                // Send challenge
                var challenge = await this._server._authenticator!.GetChallengeForClientAsync(this.ClientID);
                var challengePacket = (ConnectionChallenge)new ConnectionChallenge(challenge).SetChannel(ChannelType.ReliableOrdered);

                this._sentChallenge = challengePacket;

                this._server.SendPacketTo(challengePacket, this.RemoteEndPoint);
                this.MoveNext(ConnectionTransition.CHSent);
            }
            else
            {
                // Invalid packet to receive in this state, ignore
            }

            return;
        }

        if (packet.PacketType == ProtocolPacketType.ConnectionChallengeResponse)
        {
            // TODO: Check challenge response
            var challengeResponse = (ConnectionChallengeResponse)packet;
            var challenge = this._sentChallenge!;

            var success = await this._server._authenticator!.AuthenticateAsync(this.ClientID, challenge.Challenge, challengeResponse.Response);
            string? reason = null;

            if (!success)
            {
                reason = "Failed to authenticate.";
                this._server.TriggerConnFailedAuthEvent(this);
            }
            else
            {
                this._server.TriggerConnEstablishedEvent(this);
            }

            if (this.TryMoveNext(ConnectionTransition.CHRReceived, out var newState))
            {
                // Send connection request response, here we are authenticated and connected
                var response = new ConnectionResponse(success ? (byte)1 : (byte)0, reason).SetChannel(ChannelType.ReliableOrdered);
                this._server.SendPacketTo(response, this.RemoteEndPoint);

                this.MoveNext(ConnectionTransition.CRSSent);
            }
            else
            {
                // Invalid packet to receive in this state, ignore
            }

            return;
        }

        if (packet.PacketType == ProtocolPacketType.ConnectionTermination)
        {
            // The client has requested to disconnect.
            if (this.TryMoveNext(ConnectionTransition.CTReceived, out var newState))
            {
                // Send connection termination response (assume disconnected already)
                var response = new ConnectionTerminationAck().SetChannel(ChannelType.ReliableOrdered);
                this._server.SendPacketTo(response, this.RemoteEndPoint);

                this._server.TriggerClientDisconnectEvent(this);
                this._server.RemoveConnection(this);
            }
            else
            {
                // Invalid packet to receive in this state, ignore
            }
        }

        if (packet.PacketType == ProtocolPacketType.ConnectionTerminationAck)
        {
            // The client has acknowledged the fact that we have disconnected them.
            // We will already have put them in the disconnected state, so we can just ignore this.
            this._server.RemoveConnection(this);
            return;
        }
    }

    internal void DisconnectFromServer(string reason)
    {
        if (this.TryMoveNext(ConnectionTransition.CTSent, out var newState))
        {
            var disconnect = new ConnectionTermination(reason).SetChannel(ChannelType.ReliableOrdered);
            this._server.SendPacketTo(disconnect, this.RemoteEndPoint);
        }
        else
        {
            // Currently not in a connected state, so do nothing.
        }
    }
}