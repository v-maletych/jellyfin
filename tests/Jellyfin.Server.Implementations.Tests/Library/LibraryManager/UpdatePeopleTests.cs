using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using Emby.Naming.Common;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Library.LibraryManager;

public sealed class UpdatePeopleTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IItemRepository> _itemRepositoryMock;
    private readonly Mock<IPeopleRepository> _peopleRepositoryMock;
    private readonly Mock<IProviderManager> _providerManagerMock;
    private readonly Emby.Server.Implementations.Library.LibraryManager _libraryManager;

    public UpdatePeopleTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        var peoplePath = Path.Combine(_testDirectory, "people");

        var fixture = new Fixture().Customize(new AutoMoqCustomization());
        fixture.Register(() => new NamingOptions());

        var applicationPathsMock = fixture.Freeze<Mock<IServerApplicationPaths>>();
        applicationPathsMock.SetupGet(paths => paths.PeoplePath).Returns(peoplePath);
        applicationPathsMock.SetupGet(paths => paths.ProgramDataPath).Returns(_testDirectory);

        var configMock = fixture.Freeze<Mock<IServerConfigurationManager>>();
        configMock.SetupGet(config => config.ApplicationPaths).Returns(applicationPathsMock.Object);
        configMock.SetupGet(config => config.Configuration).Returns(new ServerConfiguration());

        _itemRepositoryMock = fixture.Freeze<Mock<IItemRepository>>();
        _itemRepositoryMock.Setup(repository => repository.RetrieveItem(It.IsAny<Guid>())).Returns(() => null!);
        _itemRepositoryMock.Setup(repository => repository.SaveItems(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<BaseItem>, CancellationToken>((items, _) =>
            {
                foreach (var item in items)
                {
                    BaseItem.LibraryManager = _libraryManager;
                }
            });

        _peopleRepositoryMock = fixture.Freeze<Mock<IPeopleRepository>>();

        _providerManagerMock = fixture.Freeze<Mock<IProviderManager>>();
        _providerManagerMock.Setup(manager => manager.RefreshSingleItem(It.IsAny<BaseItem>(), It.IsAny<MetadataRefreshOptions>(), It.IsAny<CancellationToken>()))
            .Returns<BaseItem, MetadataRefreshOptions, CancellationToken>(async (item, _, cancellationToken) =>
            {
                item.ProductionLocations = ["New York, New York, USA"];
                item.DateLastRefreshed = DateTime.UtcNow;
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataDownload, cancellationToken).ConfigureAwait(false);
                return ItemUpdateType.MetadataDownload;
            });
        _providerManagerMock.Setup(manager => manager.SaveMetadataAsync(It.IsAny<BaseItem>(), It.IsAny<ItemUpdateType>()))
            .Returns(Task.CompletedTask);

        var fileSystemMock = fixture.Freeze<Mock<IFileSystem>>();
        fileSystemMock.Setup(fileSystem => fileSystem.GetFileInfo(It.IsAny<string>())).Returns<string>(path => new FileSystemMetadata { FullName = path });
        fileSystemMock.Setup(fileSystem => fileSystem.GetValidFilename(It.IsAny<string>())).Returns<string>(name => name);

        _libraryManager = fixture.Build<Emby.Server.Implementations.Library.LibraryManager>().Do(manager => manager.AddParts(
                fixture.Create<IEnumerable<IResolverIgnoreRule>>(),
                Enumerable.Empty<IItemResolver>(),
                fixture.Create<IEnumerable<IIntroProvider>>(),
                fixture.Create<IEnumerable<IBaseItemComparer>>(),
                fixture.Create<IEnumerable<ILibraryPostScanTask>>()))
            .Create();

        BaseItem.ConfigurationManager = configMock.Object;
        BaseItem.FileSystem = fileSystemMock.Object;
        BaseItem.LibraryManager = _libraryManager;
        BaseItem.MediaSourceManager = fixture.Create<IMediaSourceManager>();
        BaseItem.ProviderManager = _providerManagerMock.Object;
    }

    [Fact]
    public async Task UpdatePeopleAsync_NewPerson_RefreshesPersonMetadata()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid()
        };

        var personInfo = new PersonInfo
        {
            Name = "Actor",
            Type = PersonKind.Actor
        };
        personInfo.SetProviderId(MetadataProvider.Tmdb, "1");

        await _libraryManager.UpdatePeopleAsync(movie, [personInfo], CancellationToken.None);

        _peopleRepositoryMock.Verify(repository => repository.UpdatePeople(movie.Id, It.IsAny<IReadOnlyList<PersonInfo>>()), Times.Once);
        _providerManagerMock.Verify(
            manager => manager.RefreshSingleItem(
                It.Is<Person>(person => person.Name == personInfo.Name && person.ProductionLocations.Contains("New York, New York, USA")),
                It.Is<MetadataRefreshOptions>(options => options.MetadataRefreshMode == MetadataRefreshMode.Default && options.ImageRefreshMode == MetadataRefreshMode.None),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _itemRepositoryMock.Verify(
            repository => repository.SaveItems(
                It.Is<IReadOnlyList<BaseItem>>(items => items.OfType<Person>().Any(person => person.Name == personInfo.Name && person.ProductionLocations.Contains("New York, New York, USA"))),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }

        GC.SuppressFinalize(this);
    }
}
