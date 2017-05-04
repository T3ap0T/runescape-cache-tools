﻿using RuneScapeCacheToolsTests.Fixtures;
using Villermen.RuneScapeCacheTools.Cache;
using Villermen.RuneScapeCacheTools.Cache.FileTypes;
using Xunit;
using Xunit.Sdk;

namespace RuneScapeCacheToolsTests
{
    [Collection(TestCacheCollection.Name)]
    public class FilesTests
    {
        private TestCacheFixture Fixture { get; }

        public FilesTests(TestCacheFixture fixture)
        {
            this.Fixture = fixture;
        }

        [Theory]
        [InlineData(38900, 53)]
        public void TestJagaFile(int fileId, int expectedNumberOfChunks)
        {
            var jagaFile = this.Fixture.RuneTek5Cache.GetFile<JagaFile>(Index.Music, fileId);

            Assert.Equal(expectedNumberOfChunks, jagaFile.ChunkCount);
        }

        [Theory]
        [InlineData(Index.ItemDefinitions, 155, 134, "Hazelmere's signet ring", 4)]
        [InlineData(Index.ItemDefinitions, 5, 241, "Oak logs", 12)]
        [InlineData(Index.ItemDefinitions, 155, 104, "Attuned crystal teleport seed", 14)]
        public void TestItemDefinition(Index index, int fileId, int entryId, string expectedName, int expectedPropertyCount)
        {
            var itemDefinition = this.Fixture.RuneTek5Cache.GetFile<ItemDefinitionFile>(index, fileId, entryId);

            Assert.Equal(expectedName, itemDefinition.Name);
            Assert.Equal(expectedPropertyCount, itemDefinition.Properties.Count);
        }

        [Fact(Skip = "Takes too long and is unfinished")]
        public void TestAllItemDefinitions()
        {
            foreach (var fileId in this.Fixture.Downloader.GetFileIds(Index.ItemDefinitions))
            {
                // TODO: Needs a method of getting all entries as BinaryFiles (store them as such?)
                this.Fixture.Downloader.GetFile<ItemDefinitionFile>(Index.ItemDefinitions, fileId);
            }
        }
    }
}