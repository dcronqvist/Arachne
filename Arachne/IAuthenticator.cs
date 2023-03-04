namespace Arachne;

public interface IAuthenticator
{
    static IAuthenticator NoAuth = new NoAuth();
    static Client.GetChallengeResponse NoAuthResponse = (challenge) => Task.FromResult(challenge);

    Task<byte[]> GetChallengeForClientAsync(ulong clientID);
    Task<bool> AuthenticateAsync(ulong clientID, byte[] challenge, byte[] response);
}

public class NoAuth : IAuthenticator
{
    public Task<bool> AuthenticateAsync(ulong clientID, byte[] challenge, byte[] response)
    {
        return Task.FromResult(true);
    }

    public Task<byte[]> GetChallengeForClientAsync(ulong clientID)
    {
        return Task.FromResult(new byte[0]);
    }
}