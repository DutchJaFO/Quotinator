using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Database.Checks;

/// <summary>
/// #490, decision D as superseded (developer, 2026-11-16): the check reads a database whose <c>-wal</c> holds data
/// through an image built in memory. Every <c>-wal</c> here is written by the test from fileformat.html §4.1 and
/// §4.2, so each test states exactly which frames exist, which are valid and which commit.
/// </summary>
[TestClass]
public sealed class WalReplayTests
{
    private const int PageSize = 512;
    private const uint Salt1 = 0x11111111;
    private const uint Salt2 = 0x22222222;

    [TestMethod]
    public void Apply_CommittedFrame_ReplacesThePage()
    {
        byte[] image = WalReplay.Apply(MainFile(3), new Wal().Frame(2, Page(9), commitSize: 3).Bytes, PageSize);

        AssertPages(image, 1, 9, 3);
    }

    [TestMethod]
    public void Apply_LaterCommittedVersionOfAPage_Wins()
    {
        byte[] wal = new Wal().Frame(2, Page(8), commitSize: 3).Frame(2, Page(9), commitSize: 3).Bytes;

        AssertPages(WalReplay.Apply(MainFile(3), wal, PageSize), 1, 9, 3);
    }

    /// <summary>§4.5: a page counts only if it is a commit frame or a commit frame follows it.</summary>
    [TestMethod]
    public void Apply_FramesAfterTheLastCommit_AreIgnored()
    {
        byte[] wal = new Wal().Frame(1, Page(7), commitSize: 3).Frame(2, Page(9), commitSize: 0).Bytes;

        AssertPages(WalReplay.Apply(MainFile(3), wal, PageSize), 7, 2, 3);
    }

    /// <summary>A frame not yet covered by a commit becomes part of the image once a later commit follows it.</summary>
    [TestMethod]
    public void Apply_FrameFollowedByACommit_IsApplied()
    {
        byte[] wal = new Wal().Frame(2, Page(9), commitSize: 0).Frame(3, Page(8), commitSize: 3).Bytes;

        AssertPages(WalReplay.Apply(MainFile(3), wal, PageSize), 1, 9, 8);
    }

    /// <summary>§4.1: a frame whose salts differ from the header's is not valid, so it is not applied.</summary>
    [TestMethod]
    public void Apply_FrameWhoseSaltsDiffer_IsIgnored()
    {
        byte[] wal = new Wal().Frame(2, Page(9), commitSize: 3, salt1: Salt1 + 1).Bytes;

        AssertPages(WalReplay.Apply(MainFile(3), wal, PageSize), 1, 2, 3);
    }

    /// <summary>
    /// §4.1: a frame is valid only if its checksum matches the one computed consecutively up to and including it,
    /// so a frame whose content changed is not applied, and neither is any later frame whose checksum covered it.
    /// </summary>
    [TestMethod]
    public void Apply_FrameWhoseContentNoLongerMatchesItsChecksum_IsIgnoredWithEveryLaterFrame()
    {
        Wal wal = new Wal().Frame(2, Page(9), commitSize: 3).Frame(3, Page(8), commitSize: 3);
        byte[] bytes = wal.Bytes;
        bytes[Wal.HeaderLength + Wal.FrameHeaderLength] ^= 0xFF;

        AssertPages(WalReplay.Apply(MainFile(3), bytes, PageSize), 1, 2, 3);
    }

    /// <summary>§4.1: a commit record carries the database's size in pages after the commit, which sizes the image.</summary>
    [TestMethod]
    public void Apply_CommitThatGrowsTheDatabase_ExtendsTheImage()
    {
        byte[] image = WalReplay.Apply(MainFile(2), new Wal().Frame(3, Page(9), commitSize: 3).Bytes, PageSize);

        AssertPages(image, 1, 2, 9);
    }

    [TestMethod]
    public void Apply_CommitThatShrinksTheDatabase_TruncatesTheImage()
    {
        byte[] image = WalReplay.Apply(MainFile(3), new Wal().Frame(1, Page(7), commitSize: 2).Bytes, PageSize);

        AssertPages(image, 7, 2);
    }

    /// <summary>§4.2: the words are little-endian when the magic number is 0x377f0682.</summary>
    [TestMethod]
    public void Apply_LittleEndianChecksums_AreReadInThatOrder()
    {
        byte[] wal = new Wal(bigEndian: false).Frame(2, Page(9), commitSize: 3).Bytes;

        AssertPages(WalReplay.Apply(MainFile(3), wal, PageSize), 1, 9, 3);
    }

    /// <summary>Control: a <c>-wal</c> with no frames leaves the main file as it is.</summary>
    [TestMethod]
    public void Apply_NoFrames_LeavesTheMainFile()
        => AssertPages(WalReplay.Apply(MainFile(3), new Wal().Bytes, PageSize), 1, 2, 3);

    /// <summary>
    /// A <c>-wal</c> SQLite itself wrote: the image holds exactly what SQLite writes into the main file when it
    /// checkpoints a copy of the same files.
    /// </summary>
    [TestMethod]
    public void Apply_WalSQLiteWrote_MatchesWhatACheckpointWrites()
    {
        using TempDirectory directory = new("quotinator_replay_");
        string path = Path.Combine(directory.Path, "source.db");
        DocumentedFormatFixtures.CreateWithWalHoldingData(path,
            ["CREATE TABLE Item (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);", "INSERT INTO Item (Id, Name) VALUES (1, 'one');"],
            ["INSERT INTO Item (Id, Name) VALUES (2, 'two');", "CREATE TABLE Later (Id INTEGER PRIMARY KEY);"]);
        byte[] main = File.ReadAllBytes(path);
        byte[] wal = File.ReadAllBytes(path + "-wal");
        int pageSize = BinaryPrimitives.ReadUInt16BigEndian(main.AsSpan(16, 2));

        byte[] image = WalReplay.Apply(main, wal, pageSize);

        Assert.AreSequenceEqual(Checkpointed(directory.Path, main, wal), image);
    }

    private static byte[] Checkpointed(string directory, byte[] main, byte[] wal)
    {
        string copy = Path.Combine(directory, "checkpointed.db");
        File.WriteAllBytes(copy, main);
        File.WriteAllBytes(copy + "-wal", wal);
        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }

        return File.ReadAllBytes(copy);
    }

    private static byte[] MainFile(int pages) => [.. Enumerable.Range(1, pages).SelectMany(n => Page((byte)n))];

    private static byte[] Page(byte fill) => [.. Enumerable.Repeat(fill, PageSize)];

    private static void AssertPages(byte[] image, params byte[] fills)
    {
        Assert.HasCount(fills.Length * PageSize, image, "The image is not sized to the committed page count.");
        for (int page = 0; page < fills.Length; page++)
            Assert.IsLessThan(0, image.AsSpan(page * PageSize, PageSize).IndexOfAnyExcept(fills[page]), $"Page {page + 1} is not the expected version.");
    }

    /// <summary>A <c>-wal</c> written by fileformat.html §4.1 and §4.2.</summary>
    private sealed class Wal
    {
        public const int HeaderLength = 32;
        public const int FrameHeaderLength = 24;

        private readonly List<byte> _bytes = [];
        private readonly bool _bigEndian;
        private uint _s0;
        private uint _s1;

        public Wal(bool bigEndian = true)
        {
            _bigEndian = bigEndian;
            byte[] header = new byte[HeaderLength];
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), bigEndian ? 0x377f0683u : 0x377f0682u);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 3007000);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), PageSize);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), Salt1);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), Salt2);
            Accumulate(header.AsSpan(0, 24));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(24), _s0);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(28), _s1);
            _bytes.AddRange(header);
        }

        public byte[] Bytes => [.. _bytes];

        public Wal Frame(uint pageNumber, byte[] data, uint commitSize, uint salt1 = Salt1)
        {
            byte[] header = new byte[FrameHeaderLength];
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), pageNumber);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), commitSize);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), salt1);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), Salt2);
            Accumulate(header.AsSpan(0, 8));
            Accumulate(data);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), _s0);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), _s1);
            _bytes.AddRange(header);
            _bytes.AddRange(data);
            return this;
        }

        private void Accumulate(ReadOnlySpan<byte> content)
        {
            for (int i = 0; i < content.Length; i += 8)
            {
                uint x0 = _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(content[i..]) : BinaryPrimitives.ReadUInt32LittleEndian(content[i..]);
                uint x1 = _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(content[(i + 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(content[(i + 4)..]);
                unchecked
                {
                    _s0 += x0 + _s1;
                    _s1 += x1 + _s0;
                }
            }
        }
    }
}
