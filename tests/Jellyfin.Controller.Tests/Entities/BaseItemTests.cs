using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using Moq;
using Xunit;

namespace Jellyfin.Controller.Tests.Entities;

public class BaseItemTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("1", "0000000001")]
    [InlineData("t", "t")]
    [InlineData("test", "test")]
    [InlineData("test1", "test0000000001")]
    [InlineData("1test 2", "0000000001test 0000000002")]
    public void BaseItem_ModifySortChunks_Valid(string input, string expected)
        => Assert.Equal(expected, BaseItem.ModifySortChunks(input));

    [Theory]
    [InlineData("/Movies/Ted/Ted.mp4", "/Movies/Ted/Ted - Unrated Edition.mp4", "Ted", "Unrated Edition")]
    [InlineData("/Movies/Deadpool 2 (2018)/Deadpool 2 (2018).mkv", "/Movies/Deadpool 2 (2018)/Deadpool 2 (2018) - Super Duper Cut.mkv", "Deadpool 2 (2018)", "Super Duper Cut")]
    public void GetMediaSourceName_Valid(string primaryPath, string altPath, string name, string altName)
    {
        var mediaSourceManager = new Mock<IMediaSourceManager>();
        mediaSourceManager.Setup(x => x.GetPathProtocol(It.IsAny<string>()))
                .Returns((string x) => MediaProtocol.File);
        BaseItem.MediaSourceManager = mediaSourceManager.Object;

        var video = new Video()
        {
            Path = primaryPath
        };

        var videoAlt = new Video()
        {
            Path = altPath,
        };

        video.LocalAlternateVersions = [videoAlt.Path];

        Assert.Equal(name, video.GetMediaSourceName(video));
        Assert.Equal(altName, video.GetMediaSourceName(videoAlt));
    }

    [Fact]
    public void IsParentalAllowed_AllowsSubScoredRating_WhenUserLimitHasNoSubScore()
    {
        var localizationManager = new Mock<ILocalizationManager>();
        localizationManager.Setup(x => x.GetRatingScore("TV-14", null))
            .Returns(new ParentalRatingScore(14, 1));
        BaseItem.LocalizationManager = localizationManager.Object;
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetCollectionFolders(It.IsAny<BaseItem>()))
            .Returns(new List<Folder>());
        BaseItem.LibraryManager = libraryManager.Object;

        var user = new Jellyfin.Database.Implementations.Entities.User("test", "auth", "reset")
        {
            MaxParentalRatingScore = 14
        };

        var item = new Video
        {
            Name = "Test",
            OfficialRating = "TV-14"
        };

        Assert.True(item.IsParentalAllowed(user, false));
    }

    [Fact]
    public void IsParentalAllowed_BlocksHigherSubScoredRating_WhenUserLimitHasSubScore()
    {
        var localizationManager = new Mock<ILocalizationManager>();
        localizationManager.Setup(x => x.GetRatingScore("TV-14-L", null))
            .Returns(new ParentalRatingScore(14, 1));
        BaseItem.LocalizationManager = localizationManager.Object;
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetCollectionFolders(It.IsAny<BaseItem>()))
            .Returns(new List<Folder>());
        BaseItem.LibraryManager = libraryManager.Object;

        var user = new Jellyfin.Database.Implementations.Entities.User("test", "auth", "reset")
        {
            MaxParentalRatingScore = 14,
            MaxParentalRatingSubScore = 0
        };

        var item = new Video
        {
            Name = "Test",
            OfficialRating = "TV-14-L"
        };

        Assert.False(item.IsParentalAllowed(user, false));
    }
}
