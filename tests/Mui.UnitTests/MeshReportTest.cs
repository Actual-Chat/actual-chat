using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Mui.UnitTests;

public class MeshReportTest
{
    [Fact]
    public void ReportListsNodesRefsAndPeers()
    {
        // arrange
        var info = NewInfo("n1", [], [
            new RpcPeerDiagInfo("p1", "peer-1", "ws", RpcPeerConnectionStateKind.Disconnected, "no route", ""),
        ]);

        // act
        var report = MeshReport.Format(info);

        // assert
        report.Should().Contain("Mesh state (1 node(s)):");
        report.Should().Contain("[0, this] = n1 host:1 : Active");
        report.Should().Contain("Roles: Api");
        report.Should().Contain("ref-1 ==> route-1");
        report.Should().Contain("Id: p1");
        report.Should().Contain("IsConnected: Disconnected");
        report.Should().Contain("Connection state: no route");
    }

    [Fact]
    public void ReportSkipsConnectionStateOfLocalPeers()
    {
        // arrange
        var info = NewInfo("n1", [], [
            new RpcPeerDiagInfo("p1", "peer-1", "Local", RpcPeerConnectionStateKind.Connected, "info", ""),
        ]);

        // act
        var report = MeshReport.Format(info);

        // assert
        report.Should().Contain("ConnectionKind: Local");
        report.Should().NotContain("IsConnected");
    }

    [Fact]
    public void ReportSeparatesOtherNodes()
    {
        // arrange
        var other = NewInfo("n2", [], []);
        var info = NewInfo("n1", [other], []);

        // act
        var report = MeshReport.Format(info);

        // assert
        report.Should().Contain("***");
        report.Should().Contain("= n2 host:1");
    }

    [Fact]
    public void OnlyRemoteDisconnectedPeersAreProblems()
    {
        // arrange
        var disconnected = new RpcPeerDiagInfo("p", "p", "ws", RpcPeerConnectionStateKind.Disconnected, "", "");
        var connected = new RpcPeerDiagInfo("p", "p", "ws", RpcPeerConnectionStateKind.Connected, "", "");
        var local = new RpcPeerDiagInfo("p", "p", "local", RpcPeerConnectionStateKind.Disconnected, "", "");

        // act & assert
        MeshReport.IsProblem(disconnected).Should().BeTrue();
        MeshReport.IsProblem(connected).Should().BeFalse();
        MeshReport.IsProblem(local).Should().BeFalse();
    }

    private static MeshDiagInfo NewInfo(string nodeId, MeshDiagInfo[] others, RpcPeerDiagInfo[] peers)
        => new(nodeId, "tag", default,
            [new NodeDiagInfo(nodeId, "host:1", "Active", true, "Api", "")],
            peers,
            [new MeshRpcRefDiagInfo("ref-1", "route-1", "addr", nodeId, 1, "")],
            others,
            "");
}
