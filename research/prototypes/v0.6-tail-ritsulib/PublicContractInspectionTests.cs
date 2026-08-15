using STS2RitsuLib;
using Xunit;

namespace Sts2TailPrototype;

public sealed class PublicContractInspectionTests
{
    [Fact]
    public void Public_api_exposes_required_typed_sidecar_contract_without_private_access()
    {
        PublicRitsuContract contract = PublicRitsuContract.Load(typeof(RitsuLibFramework).Assembly);
        Assert.True(contract.HasTypedRequiredDescriptor);
        Assert.True(contract.HasDirectNetServiceSend);
        Assert.True(contract.HasPublicReachabilityHint);
        Assert.True(contract.HasSessionReachability);
        Assert.DoesNotContain(contract.ReferencedMembers, name => name.Contains("SerializePatch", System.StringComparison.Ordinal));

        ulong opcode = SidecarCarrierProbe.Register();
        Assert.NotEqual(0UL, opcode);
        using System.IDisposable subscription = SidecarCarrierProbe.Subscribe(static _ => { });
    }
}
