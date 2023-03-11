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
    internal ulong _lastReceivedSequenceNumber = 0;
    internal DateTime _lastReceivedPacketTime;
    internal ThreadSafe<ReliabilityManager> _reliabilityManager = new(new());

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

    internal bool FollowsOrder(ProtocolPacket packet)
    {
        if (packet.Channel.IsOrdered() && packet.Channel.IsReliable())
        {
            return packet.SequenceNumber == this._lastReceivedSequenceNumber + 1;
        }
        else if (packet.Channel.IsOrdered())
        {
            return packet.SequenceNumber > this._lastReceivedSequenceNumber;
        }

        return true;
    }

    internal bool HasTimedOut(TimeSpan timeout)
    {
        return DateTime.Now - this._lastReceivedPacketTime > timeout;
    }

    internal void AddSentPacket(ProtocolPacket packet)
    {
        if (packet.Channel.IsReliable())
        {
            this._reliabilityManager.LockedAction(rm =>
            {
                rm.AddSentPacket(packet);
            });
        }
    }

    internal async Task ReceiveProtocolPacket(ProtocolPacket packet)
    {
        if (packet.PacketType == ProtocolPacketType.ConnectionRequest)
        {
            if (this.TryMoveNext(ConnectionTransition.CRReceived, out var newState))
            {
                // TODO: Check stuff
                var cr = (ConnectionRequest)packet;

                this._server.TriggerConnRequestedEvent(this);

                if (cr.ProtocolID != this._server._protocolID && !this._server._supportedClientProtocolIDs.Contains(cr.ProtocolID))
                {
                    var response = new ConnectionResponse(Constant.FAILURE_UNSUPPORTED_PROTOCOL_VERSION, 0).SetChannelType(ChannelType.Reliable | ChannelType.Ordered);
                    this._server.SendPacketTo(response, this.RemoteEndPoint);

                    this._server.TriggerConnFailedAuthEvent(this);
                    this._server.RemoveConnection(this);
                    return;
                    // TODO: Remove connection immediately.
                }

                // Send challenge
                var challenge = await this._server._authenticator!.GetChallengeForClientAsync(this.ClientID);
                var challengePacket = (ConnectionChallenge)new ConnectionChallenge(challenge).SetChannelType(ChannelType.Reliable | ChannelType.Ordered);

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
            if (this.TryMoveNext(ConnectionTransition.CHRReceived, out var newState))
            {
                // TODO: Check challenge response
                var challengeResponse = (ConnectionChallengeResponse)packet;
                var challenge = this._sentChallenge!;

                var success = await this._server._authenticator!.AuthenticateAsync(this.ClientID, challenge.Challenge, challengeResponse.Response);
                Constant code = Constant.SUCCESS;

                if (!success)
                {
                    code = Constant.FAILURE_INVALID_AUTHENTICATION;
                    this._server.TriggerConnFailedAuthEvent(this);
                }
                else
                {
                    // Must select a client ID for this connection
                    this.ClientID = this._server.GetNextClientID();
                    this._server.TriggerConnEstablishedEvent(this);
                }

                // Send connection request response, here we are authenticated and connected
                var response = new ConnectionResponse(code, this.ClientID).SetChannelType(ChannelType.Reliable | ChannelType.Ordered);
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
                var response = new ConnectionTerminationAck().SetChannelType(ChannelType.Reliable | ChannelType.Ordered);
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
            var disconnect = new ConnectionTermination(reason).SetChannelType(ChannelType.Reliable | ChannelType.Ordered);
            this._server.SendPacketTo(disconnect, this.RemoteEndPoint);
        }
        else
        {
            // Currently not in a connected state, so do nothing.
        }
    }
}