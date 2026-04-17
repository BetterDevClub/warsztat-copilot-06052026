using BookSlot.Infrastructure;
using Npgsql;

namespace BookSlot.Worker.Leadership;

/// <summary>
/// Leader election backed by PostgreSQL session-scoped advisory locks
/// (<c>pg_try_advisory_lock(key)</c>). The lock is tied to the lifetime of
/// a dedicated <see cref="NpgsqlConnection"/>, so the moment the leader
/// crashes the connection drops and the lock is released automatically —
/// no heartbeat or TTL bookkeeping required.
/// </summary>
internal sealed class PostgresAdvisoryLockLeaderElection : ILeaderElection, IAsyncDisposable
{
    // Deterministic 64-bit key derived from the namespace literal "BookSlot.Worker.Leader".
    // Picked once, hard-coded so every replica targets the same lock slot.
    private const long LockKey = 0x42_00_4F_0B_55_57_4C_52L;

    private readonly string _connectionString;
    private readonly ILogger<PostgresAdvisoryLockLeaderElection> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NpgsqlConnection? _connection;

    public PostgresAdvisoryLockLeaderElection(
        IConfiguration configuration,
        ILogger<PostgresAdvisoryLockLeaderElection> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _connectionString = configuration.GetConnectionString(DependencyInjection.PostgresConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DependencyInjection.PostgresConnectionStringName}' is required for leader election.");
        _logger = logger;
    }

    public bool IsLeader => _connection is { State: System.Data.ConnectionState.Open };

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { State: System.Data.ConnectionState.Open })
                return true;

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }

            var conn = new NpgsqlConnection(_connectionString);
            try
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
                cmd.Parameters.AddWithValue("key", LockKey);
                var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                var acquired = result is bool b && b;
                if (acquired)
                {
                    _connection = conn;
                    _logger.LogInformation("Worker acquired leadership lock (key=0x{Key:X}).", LockKey);
                    return true;
                }

                await conn.DisposeAsync().ConfigureAwait(false);
                return false;
            }
            catch
            {
                await conn.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null) return;

            try
            {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock(@key)";
                cmd.Parameters.AddWithValue("key", LockKey);
                await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release leadership lock cleanly — relying on session teardown.");
            }

            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
            _logger.LogInformation("Worker released leadership lock.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
        _gate.Dispose();
    }
}
