using MultiplayerLiveOpsToolkit.Backends.Django;
using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Text;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Identity;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;
public class EquipmentBackendTests
{
    private const string STATE="{\"slots\":[{\"slot_id\":\"hat\",\"conflicts\":[]}],\"items\":[{\"item_id\":\"cap\",\"slot_id\":\"hat\"}],\"loadout\":{\"revision\":2,\"items\":{\"hat\":\"cap\"}}}";
    [UnityTest] public IEnumerator Fetch_UsesExplicitSessionAndStrictImmutableState()
    { var session=new Session();EquipmentState state=null;yield return new DjangoEquipmentBackend("https://equipment.example.test",session).Fetch(value=>state=value,error=>Assert.Fail(error.Message));Assert.That(state.Loadout.Revision,Is.EqualTo(2));Assert.That(session.Url,Does.EndWith("/api/equipment/loadout/")); }
    [UnityTest] public IEnumerator Replace_SendsOnlyIdsAndExpectedRevision()
    { var session=new Session();yield return new DjangoEquipmentBackend("https://equipment.example.test",session).Replace(new[]{"cap"},1,value=>{},error=>Assert.Fail(error.Message));Assert.That(session.Body,Is.EqualTo("{\"item_ids\":[\"cap\"],\"expected_revision\":1}")); }
    [UnityTest] public IEnumerator InvalidMutationResponse_IsUnknownWithoutRetry()
    { var session=new Session{BodyResponse=STATE.Replace("\"hat\":\"cap\"","\"missing\":\"cap\"")};EquipmentError error=null;yield return new DjangoEquipmentBackend("https://equipment.example.test",session).Replace(new[]{"cap"},1,value=>Assert.Fail(),value=>error=value);Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.UnknownOutcome));Assert.That(session.Count,Is.EqualTo(1)); }
    [UnityTest] public IEnumerator ChangedAccount_CancelsOldResponse()
    { var session=new Session{ChangeAccount=true};EquipmentError error=null;yield return new DjangoEquipmentBackend("https://equipment.example.test",session).Fetch(value=>Assert.Fail(),value=>error=value);Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.Cancelled)); }
    [UnityTest] public IEnumerator StaleRevision_IsTypedBusinessRejection()
    { var session=new Session{Status=409,BodyResponse="{\"code\":\"stale_equipment\"}"};EquipmentError error=null;yield return new DjangoEquipmentBackend("https://equipment.example.test",session).Replace(new[]{"cap"},1,value=>Assert.Fail(),value=>error=value);Assert.That(error.Code,Is.EqualTo(EquipmentErrorCode.StaleRevision)); }
    [UnityTest] public IEnumerator Proof_UsesEquipmentRouteAndRejectsDuplicateAssertionFields()
    {
        var session = new Session { BodyResponse = "{\"assertion\":\"opaque\"}" };
        string assertion = null;
        var proof = new DjangoEquipmentProofAPI("https://equipment.example.test", session);
        yield return proof.RequestProof("session", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", value => assertion = value, error => Assert.Fail(error.ToString()));
        Assert.That(session.Url, Does.EndWith("/api/equipment/proof/"));Assert.That(assertion,Is.EqualTo("opaque"));
        session.BodyResponse = "{\"assertion\":\"one\",\"assertion\":\"two\"}";
        IdentityProofRequestFailure? failure = null;
        yield return proof.RequestProof("session", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", value => Assert.Fail(), error => failure = error);
        Assert.That(failure,Is.EqualTo(IdentityProofRequestFailure.InvalidResponse));
    }
    private sealed class Session:IAuthenticatedSession
    {
        public bool IsActive=>true;public int SessionVersion{get;private set;}public event Action<string> SessionEnded{add{}remove{}}
        public string Url;public string Body;public string BodyResponse=STATE;public int Status=200;public int Count;public bool ChangeAccount;
        public IEnumerator Send(UnityWebRequest request,ApiResponse response,Action<bool> complete=null) {Count++;Url=request.url;Body=request.uploadHandler==null?null:Encoding.UTF8.GetString(request.uploadHandler.data);yield return null;if(ChangeAccount)SessionVersion++;response.Text=BodyResponse;response.ResponseCode=Status;response.Result=Status==200?UnityWebRequest.Result.Success:UnityWebRequest.Result.ProtocolError;}
        public void RejectSession(int version,string reason=null){}public void Dispose(){}
    }
}
