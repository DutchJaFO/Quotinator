using System.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// Shared setup for the backup endpoints (#349): a real, disposable backups folder and a test host
/// whose reader and writer point at it.
/// <para>
/// Deliberately real rather than faked. The behaviour these endpoints are judged on — a traversal
/// attempt that resolves outside the folder, a delete that removes one file and not its neighbour, a
/// download that returns the same bytes that were written — is filesystem behaviour, and a fake reader
/// would assert only that the handler called it. Nothing here opens a database: that is the property
/// the degraded-reachability tests depend on.
/// </para>
/// </summary>
internal sealed class BackupTestHarness : IDisposable
{
    /// <summary>The admin key this harness configures, and the one <see cref="AuthenticatedClient"/> sends.</summary>
    internal const string TestKey = "test-admin-key";

    /// <summary>The disposable folder standing in for <c>{dataDir}/backups/</c>.</summary>
    internal string BackupsPath { get; }

    /// <summary>The options the reader and writer were built with.</summary>
    internal DatabaseOptions Options { get; }

    /// <summary>Audit entries written during the test, in order.</summary>
    internal RecordingAuditWriter Audit { get; }

    /// <summary>
    /// The initializer the host resolves. Its backup behaviour is settable so a test can arrange a
    /// refusal or a pre-flight answer after construction — the folder it writes into is this
    /// harness's own, which is only known once the harness exists.
    /// </summary>
    internal BackupStubInitializer Db { get; }

    private readonly WebApplicationFactory<Program> _factory;

    internal BackupTestHarness(
        string? adminApiKey            = TestKey,
        int maxBackupStorageGb         = 1,
        int backupQuotaPercent         = DatabaseOptions.DefaultBackupQuotaPercent,
        IDiskSpaceProvider? diskSpace  = null)
    {
        BackupsPath = Path.Combine(Path.GetTempPath(), $"quotinator-349-{Guid.NewGuid():N}");
        Directory.CreateDirectory(BackupsPath);

        Options = new DatabaseOptions
        {
            DbPath             = Path.Combine(BackupsPath, "quotinatordata.db"),
            BackupsPath        = BackupsPath,
            MaxBackupStorageGb = maxBackupStorageGb,
            BackupQuotaPercent = backupQuotaPercent,
        };

        IDiskSpaceProvider disk = diskSpace ?? NoOpDiskSpaceProvider.Instance;
        Audit = new RecordingAuditWriter();
        Db    = new BackupStubInitializer { WriteInto = BackupsPath };

        _factory = new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(Db);
                services.AddSingleton<IAuditEntryWriter>(Audit);
                services.AddSingleton<IAuditEntryReader>(new NoOpAuditEntryReader());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton<INotificationWriter>(NoOpNotificationWriter.Instance);
                services.AddSingleton(disk);
                services.AddSingleton<IDatabaseBackupReader>(new DatabaseBackupReader(Options, disk));
                services.AddSingleton<IDatabaseBackupWriter>(new DatabaseBackupWriter(Options));
            });

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Quotinator:AdminApiKey"] = adminApiKey,
                });
            });
        });
    }

    /// <summary>A client with no API key, for the 401 cases.</summary>
    internal HttpClient AnonymousClient() => _factory.CreateClient();

    /// <summary>
    /// Puts the host into the degraded state these endpoints exist to be usable in — the same state
    /// <c>Program.cs</c> records when startup initialisation fails, set directly rather than by
    /// arranging a real failure, so the test is about reachability and not about how the failure
    /// arose.
    /// </summary>
    internal void MarkDatabaseUnhealthy() =>
        _factory.Services.GetRequiredService<Quotinator.Api.Startup.DatabaseHealthState>()
                .MarkFailed("Simulated degraded startup (#349 reachability test).");

    /// <summary>A client carrying <see cref="TestKey"/>.</summary>
    internal HttpClient AuthenticatedClient()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", TestKey);
        return client;
    }

    /// <summary>
    /// Writes a backup file with deterministic content derived from its name, so a download can be
    /// compared byte for byte against what was written rather than only by length.
    /// </summary>
    /// <param name="name">File name to create inside the backups folder.</param>
    /// <param name="sizeBytes">How many bytes to write.</param>
    /// <returns>The bytes written.</returns>
    internal byte[] WriteBackup(string name, int sizeBytes = 64)
    {
        byte[] content = ContentFor(name, sizeBytes);
        File.WriteAllBytes(Path.Combine(BackupsPath, name), content);
        return content;
    }

    /// <summary>The deterministic content <see cref="WriteBackup"/> produces for a given name.</summary>
    /// <param name="name">The file name the content is derived from.</param>
    /// <param name="sizeBytes">How many bytes.</param>
    internal static byte[] ContentFor(string name, int sizeBytes)
    {
        byte[] content = new byte[sizeBytes];
        for (int i = 0; i < sizeBytes; i++)
            content[i] = (byte)((name[i % name.Length] + i) % 256);
        return content;
    }

    /// <summary>File names currently in the backups folder.</summary>
    internal IReadOnlyList<string> FilesOnDisk() =>
        [.. Directory.EnumerateFiles(BackupsPath).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).Order()];

    public void Dispose()
    {
        _factory.Dispose();

        // Best-effort: a leaked temp folder is untidy, a test failing in teardown is worse.
        try { Directory.Delete(BackupsPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// An initializer whose backup behaviour a test controls, forwarding everything else to the no-op.
    /// <para>
    /// Composed rather than derived because <see cref="NoOpDatabaseInitializer"/> is sealed, and
    /// deliberately writes a <em>real</em> file when it succeeds: the point of several of these tests
    /// is that what create produces is the same thing list and download then see, which a stub
    /// returning a path to nothing could not show.
    /// </para>
    /// </summary>
    internal sealed class BackupStubInitializer : IDatabaseInitializer
    {
        private readonly NoOpDatabaseInitializer _inner = NoOpDatabaseInitializer.Instance;

        /// <summary>Folder to write a backup into when the attempt succeeds.</summary>
        public string? WriteInto { get; init; }

        /// <summary>Obstacle to report instead of taking a backup.</summary>
        public BackupOutcome? RefuseWith { get; set; }

        /// <summary>What the pre-flight reports, independently of what an attempt would do.</summary>
        public BackupOutcome Readiness { get; set; } = BackupOutcome.Succeeded;

        /// <summary>How many times a backup was actually attempted.</summary>
        public int CreateCalls { get; private set; }

        /// <inheritdoc/>
        public Task<DatabaseBackupResult> CreateBackupAsync()
        {
            CreateCalls++;

            if (RefuseWith is not null)
                return Task.FromResult(DatabaseBackupResult.Failed(RefuseWith.Value));

            string name = $"quotinatordata_v5_{DateTime.UtcNow:yyyyMMddTHHmmssfff}Z.db";
            string path = Path.Combine(WriteInto ?? Path.GetTempPath(), name);
            File.WriteAllBytes(path, ContentFor(name, 96));
            return Task.FromResult(DatabaseBackupResult.Success(path));
        }

        /// <inheritdoc/>
        public BackupOutcome CheckBackupReadiness(bool allowReserve = false) => Readiness;

        /// <inheritdoc/>
        public int SchemaVersion => _inner.SchemaVersion;
        /// <inheritdoc/>
        public int DataSchemaVersion => _inner.DataSchemaVersion;
        /// <inheritdoc/>
        public int QuoteCount => _inner.QuoteCount;
        /// <inheritdoc/>
        public int SourceCount => _inner.SourceCount;
        /// <inheritdoc/>
        public int CharacterCount => _inner.CharacterCount;
        /// <inheritdoc/>
        public int PeopleCount => _inner.PeopleCount;
        /// <inheritdoc/>
        public int SeriesCount => _inner.SeriesCount;
        /// <inheritdoc/>
        public int UniverseCount => _inner.UniverseCount;
        /// <inheritdoc/>
        public int StageDirectionCount => _inner.StageDirectionCount;
        /// <inheritdoc/>
        public int SoundCueCount => _inner.SoundCueCount;
        /// <inheritdoc/>
        public int ConversationCount => _inner.ConversationCount;
        /// <inheritdoc/>
        public string? MigrationApplied => _inner.MigrationApplied;
        /// <inheritdoc/>
        public bool SchemaVersionOvershootDetected => _inner.SchemaVersionOvershootDetected;
        /// <inheritdoc/>
        public IReadOnlyList<FileImportReport> LastSeedReport => _inner.LastSeedReport;
        /// <inheritdoc/>
        public Task<DatabaseOperationResult> InitialiseAsync() => _inner.InitialiseAsync();
        /// <inheritdoc/>
        public Task ReseedAsync(bool forceSourceRefresh = false) => _inner.ReseedAsync(forceSourceRefresh);
        /// <inheritdoc/>
        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false)
            => _inner.ResetAsync(preserveSchemaVersion, forceSourceRefresh, allowNoBackup);
        /// <inheritdoc/>
        public Task<SeedPreviewResult> PreviewSeedAsync() => _inner.PreviewSeedAsync();
        /// <inheritdoc/>
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => _inner.RefreshSourcesAsync(force);
    }

    /// <summary>Captures whole audit entries, not just their operation names — #349 asserts what was recorded about the file.</summary>
    internal sealed class RecordingAuditWriter : IAuditEntryWriter
    {
        /// <summary>Every entry written, in order.</summary>
        public List<AuditEntryEntity> Entries { get; } = [];

        /// <inheritdoc/>
        public Task WriteAsync(AuditEntryEntity entry)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task WriteAsync(AuditEntryEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
            => WriteAsync(entry);

        /// <inheritdoc/>
        public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, IDbConnection connection, IDbTransaction? transaction = null)
        {
            Entries.AddRange(entries);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        /// <remarks>No backup endpoint clears the audit trail; present to satisfy the interface.</remarks>
        public Task ClearAsync(string? table = null) => Task.CompletedTask;
    }
}
