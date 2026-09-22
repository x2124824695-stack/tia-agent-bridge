using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TiaAgent.Host.Safety;

public sealed class PreviewTokenService
{
    private readonly ConcurrentDictionary<string, TokenState> _tokens = new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(10);

    public PreviewToken Create(string blockPath, string currentHash, string proposedHash)
    {
        SweepExpired();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var expires = DateTimeOffset.UtcNow.Add(_ttl);
        _tokens[token] = new TokenState(blockPath, currentHash, proposedHash, expires);
        return new PreviewToken(token, expires);
    }

    public void Consume(string token, string blockPath, string currentHash, string proposedHash)
    {
        if (!_tokens.TryRemove(token, out var state))
            throw new InvalidOperationException("Preview token is invalid, expired, or already used. Run preview_block_update again.");

        if (state.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Preview token expired. Run preview_block_update again.");

        if (!string.Equals(state.BlockPath, blockPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(state.CurrentHash, currentHash, StringComparison.Ordinal) ||
            !string.Equals(state.ProposedHash, proposedHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Preview token does not match the requested block or source hashes.");
    }

    private void SweepExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _tokens)
            if (pair.Value.ExpiresAt <= now)
                _tokens.TryRemove(pair.Key, out _);
    }

    private sealed record TokenState(string BlockPath, string CurrentHash, string ProposedHash, DateTimeOffset ExpiresAt);
}

public sealed record PreviewToken(string Value, DateTimeOffset ExpiresAt);
