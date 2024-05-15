using ActualChat.Mesh;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.MLSearch.Module;
using ActualChat.Performance;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.UnitTests.Engine.OpenSearch.Setup;

public class ClusterSetupTest(ITestOutputHelper @out) : TestBase(@out)
{
    private readonly IndexNames _indexNames = new();
    private readonly OpenSearchSettings _openSearchSettings = new OpenSearchSettings {
        ClusterUri = "some://uri",
        ModelGroup = "setup-test-group"
    };
    private readonly ClusterSetupResult _setupResult = new(new EmbeddingModelProps(
        "some-unuque-model-id", 1024, "{ some: 'json config'}"
    ));

    [Fact]
    public void ResultPropertyThrowsWhenNotInitialized()
    {
        var clusterSetup = new ClusterSetup(
            Mock.Of<IMeshLocks>(),
            Mock.Of<IClusterSetupActions>(),
            Mock.Of<IOptions<OpenSearchSettings>>(),
            [],
            Mock.Of<ILogger<ClusterSetup>>(),
            _indexNames,
            Tracer.None);
        Assert.Throws<InvalidOperationException>(() => clusterSetup.Result);
    }

    [Fact]
    public async Task InitializationChecksForAllOfRequiredOpenSearchEnitities()
    {
        var meshLocks = MockMeshLocks();
        var setupActions = MockSetupActions(true);
        var openSearchSettings = MockOpenSearchSettings();

        var clusterSetup = new ClusterSetup(
            meshLocks.Object,
            setupActions.Object,
            openSearchSettings.Object,
            [],
            Mock.Of<ILogger<ClusterSetup>>(),
            _indexNames,
            Tracer.None);

        var cancellationSource = new CancellationTokenSource();
        await clusterSetup.InitializeAsync(cancellationSource.Token);

        // Check model props are retrieved
        setupActions.Verify(actions => actions.RetrieveEmbeddingModelPropsAsync(
                It.Is<string>(modelGroup => modelGroup == _openSearchSettings.ModelGroup),
                It.Is<CancellationToken>(t => t == cancellationSource.Token)
            ), Times.Once());

        // Check if initializer verifies validness of all needed templates
        setupActions.Verify(actions => actions.IsTemplateValidAsync(
                It.Is<string>(name => name == IndexNames.MLTemplateName),
                It.Is<string>(pattern => pattern == IndexNames.MLIndexPattern),
                It.Is<int?>(numReplicas => numReplicas == _openSearchSettings.DefaultNumberOfReplicas),
                It.Is<CancellationToken>(t => t == cancellationSource.Token)
            ), Times.Once());

        // Check if initializer verifies existence of all needed ingestion pipelines
        var pipelineName = _indexNames.GetFullIngestPipelineName(IndexNames.ChatContent, _setupResult.EmbeddingModelProps);
        setupActions.Verify(actions => actions.IsPipelineExistsAsync(
                It.Is<string>(name => name == pipelineName),
                It.Is<CancellationToken>(t => t == cancellationSource.Token)
            ), Times.Once());

        // Check if initializer verifies existence of all needed indexes
        var indexShortNames = new[] { IndexNames.ChatContent, IndexNames.ChatContentCursor, IndexNames.ChatCursor };
        var indexNames = indexShortNames
            .Select(name => _indexNames.GetFullName(name, _setupResult.EmbeddingModelProps));
        foreach (var indexName in indexNames) {
            setupActions.Verify(actions => actions.IsIndexExistsAsync(
                    It.Is<string>(name => name == indexName),
                    It.Is<CancellationToken>(t => t == cancellationSource.Token)
                ), Times.Once());
        }

        // Verify no other checks were performed
        setupActions.VerifyNoOtherCalls();

        // Verify there was no attempt to accuire some distributed lock
        meshLocks.VerifyNoOtherCalls();
    }

    public enum EntityType { Template, Pipeline, Index }
    public static TheoryData<EntityType, string> FailedChecks => new() {
        { EntityType.Template, IndexNames.MLTemplateName},
        { EntityType.Pipeline, IndexNames.ChatContent},
        { EntityType.Index, IndexNames.ChatContent},
        { EntityType.Index, IndexNames.ChatContentCursor},
        { EntityType.Index, IndexNames.ChatCursor},
    };

    [Theory]
    [MemberData(nameof(FailedChecks))]
    public async Task AnyUnsuccessfulCheckLeadsToLockAcquisition(EntityType failedEntityType, string shortName)
    {
        var meshLocks = MockMeshLocks();
        var setupActions = MockSetupActionsWithFailedCheck(failedEntityType, shortName);
        var openSearchSettings = MockOpenSearchSettings();

        var clusterSetup = new ClusterSetup(
            meshLocks.Object,
            setupActions.Object,
            openSearchSettings.Object,
            [],
            Mock.Of<ILogger<ClusterSetup>>(),
            _indexNames,
            Tracer.None);

        var cancellationSource = new CancellationTokenSource();
        await clusterSetup.InitializeAsync(cancellationSource.Token);

        meshLocks.Verify(locks => locks.Lock(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MeshLockOptions>(),
                It.Is<CancellationToken>(t => t == cancellationSource.Token)
            ), Times.Once());
    }

    [Fact]
    public async Task InitializationEnsuresEntitiesIfClusterCheckIsUnsuccessful()
    {
        var meshLocks = MockMeshLocks();
        var setupActions = MockSetupActions(false);
        var openSearchSettings = MockOpenSearchSettings();

        var clusterSetup = new ClusterSetup(
            meshLocks.Object,
            setupActions.Object,
            openSearchSettings.Object,
            [],
            Mock.Of<ILogger<ClusterSetup>>(),
            _indexNames,
            Tracer.None);

        await clusterSetup.InitializeAsync(CancellationToken.None);

        setupActions.Verify(actions => actions.EnsureTemplateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()
        ), Times.Once());

        setupActions.Verify(actions => actions.EnsureEmbeddingIngestPipelineAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()
        ), Times.Once());

        setupActions.Verify(actions => actions.EnsureContentIndexAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()
        ), Times.Once());

        setupActions.Verify(actions => actions.EnsureContentCursorIndexAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()
        ), Times.Once());

        setupActions.Verify(actions => actions.EnsureChatsCursorIndexAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()
        ), Times.Once());

        // Verify all methods having setups are called
        setupActions.Verify();

        // Verify there are no other calls except ones we setup
        setupActions.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializationAlwaysNotifiesAboutResultPropertyChange(bool isClusterStateValid)
    {
        var meshLocks = MockMeshLocks();
        var setupActions = MockSetupActions(isClusterStateValid);
        var openSearchSettings = MockOpenSearchSettings();

        var settingChangeSource = new Mock<ISettingsChangeTokenSource>();
        settingChangeSource.Setup(s => s.RaiseChanged());

        var clusterSetup = new ClusterSetup(
            meshLocks.Object,
            setupActions.Object,
            openSearchSettings.Object,
            [settingChangeSource.Object],
            Mock.Of<ILogger<ClusterSetup>>(),
            _indexNames,
            Tracer.None);

        await clusterSetup.InitializeAsync(CancellationToken.None);

        settingChangeSource.Verify(s => s.RaiseChanged(), Times.Once());
    }

    private Mock<IClusterSetupActions> MockSetupActionsWithFailedCheck(EntityType failedEntityType, string shortName)
    {
        var isTemplate = failedEntityType == EntityType.Template;
        var isPipeline = failedEntityType == EntityType.Pipeline;
        var isIndex = failedEntityType == EntityType.Index;

        var modelProps = _setupResult.EmbeddingModelProps;

        var setupActions = new Mock<IClusterSetupActions>();
        setupActions
            .Setup(actions => actions.RetrieveEmbeddingModelPropsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(Task.FromResult(modelProps))
            .Verifiable();

        setupActions
            .Setup(actions => actions.IsTemplateValidAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns<string, string, int?, CancellationToken>((name, _, _, _)
                => Task.FromResult(!isTemplate || name!=shortName))
            .Verifiable();

        setupActions
            .Setup(actions => actions.IsPipelineExistsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns<string, CancellationToken>((name, _)
                => Task.FromResult(!isPipeline || name!=_indexNames.GetFullIngestPipelineName(shortName, modelProps)))
            .Verifiable();

        setupActions
            .Setup(action => action.IsIndexExistsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns<string, CancellationToken>((name, _)
                => Task.FromResult(!isIndex || name!=_indexNames.GetFullName(shortName, modelProps)))
            .Verifiable();
        return setupActions;
    }

    private Mock<IClusterSetupActions> MockSetupActions(bool isClusterStateValid)
    {
        var modelProps = _setupResult.EmbeddingModelProps;

        var setupActions = new Mock<IClusterSetupActions>();
        setupActions.Setup(actions => actions.RetrieveEmbeddingModelPropsAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()
        ))
        .Returns(Task.FromResult(modelProps))
        .Verifiable();

        setupActions.Setup(actions => actions.IsTemplateValidAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()
        ))
        .Returns(Task.FromResult(isClusterStateValid))
        .Verifiable();

        setupActions.Setup(actions => actions.IsPipelineExistsAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()
        ))
        .Returns(Task.FromResult(isClusterStateValid))
        .Verifiable();

        setupActions.Setup(action => action.IsIndexExistsAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()
        ))
        .Returns(Task.FromResult(isClusterStateValid))
        .Verifiable();

        if (isClusterStateValid) {
            setupActions.Setup(actions => actions.EnsureTemplateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()
            ))
            .Verifiable();

            setupActions.Setup(actions => actions.EnsureEmbeddingIngestPipelineAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()
            ))
            .Verifiable();

            setupActions.Setup(actions => actions.EnsureContentIndexAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()
            ))
            .Verifiable();

            setupActions.Setup(actions => actions.EnsureContentCursorIndexAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()
            ))
            .Verifiable();

            setupActions.Setup(actions => actions.EnsureChatsCursorIndexAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()
            ))
            .Verifiable();
        }

        return setupActions;
    }

    private Mock<IMeshLocks> MockMeshLocks() {
        var meshLocks = new Mock<IMeshLocks>();
        meshLocks
            .SetupGet(x => x.LockOptions)
            .Returns(MeshLocksBase.DefaultLockOptions);
        meshLocks
            .Setup(locks => locks.Lock(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MeshLockOptions>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns<string, string, MeshLockOptions, CancellationToken>((key, value, options, ct)
                => Task.FromResult(new MeshLockHolder(Mock.Of<IMeshLocksBackend>(), "id", key, value, options)))
            .Verifiable();

        return meshLocks;
    }

    private Mock<IOptions<OpenSearchSettings>> MockOpenSearchSettings()
    {
        var openSearchSettings = new Mock<IOptions<OpenSearchSettings>>();
        openSearchSettings
            .SetupGet(x => x.Value)
            .Returns(_openSearchSettings);
        return openSearchSettings;
    }
}
