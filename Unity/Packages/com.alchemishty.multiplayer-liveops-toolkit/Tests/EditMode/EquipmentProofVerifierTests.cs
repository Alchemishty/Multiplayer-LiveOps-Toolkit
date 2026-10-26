using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class EquipmentProofVerifierTests
{
    private RSA signer;
    private EquipmentProofVerifier verifier;
    [OneTimeSetUp] public void SetUp()
    {
        signer = new RSACryptoServiceProvider(2048);
        verifier = new EquipmentProofVerifier(new IdentityProofVerifier(new Dictionary<string,RSAParameters>{{"test",signer.ExportParameters(false)}},new IdentityProofPolicy("issuer","game","test","v1",60,0)));
    }
    [OneTimeTearDown] public void TearDown()=>signer.Dispose();
    private static IdentityChallenge Challenge()=>new IdentityChallenge("session",IdentityProofTestVector.NONCE,1900000100);
    private static JObject Claims()=>new JObject { ["iss"]="issuer",["aud"]="game",["env"]="test",["content"]="v1",["sub"]="player",["name"]="Player",["sid"]="session",["nonce"]=IdentityProofTestVector.NONCE,["iat"]=1900000000,["nbf"]=1900000000,["exp"]=1900000060,["jti"]="0123456789abcdef0123456789abcdef",["ver"]=1,["revision"]=2,["items"]=new JObject{["hat"]="cap"} };
    private string Sign(JObject claims=null,string type="liveops-equipment+jwt")
    {
        string content=Encode(Encoding.UTF8.GetBytes(new JObject{["alg"]="RS256",["kid"]="test",["typ"]=type}.ToString(Formatting.None)))+"."+Encode(Encoding.UTF8.GetBytes((claims??Claims()).ToString(Formatting.None)));
        return content+"."+Encode(signer.SignData(Encoding.ASCII.GetBytes(content),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
    }
    private static string Encode(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
    [Test] public void SignedLoadout_BindsPeerAndConsumesChallenge()
    {
        var challenge=Challenge();Assert.That(verifier.TryVerify(Sign(),challenge,1900000010,"player",null,out var result),Is.True);Assert.That(result.Revision,Is.EqualTo(2));Assert.That(result.ItemsBySlot["hat"],Is.EqualTo("cap"));Assert.That(verifier.TryVerify(Sign(),challenge,1900000010,"player",null,out _),Is.False);
    }
    [TestCase("sub","\"other\"")][TestCase("sid","\"other\"")][TestCase("content","\"v2\"")][TestCase("exp","1900000005")][TestCase("revision","-1")][TestCase("revision","2.5")][TestCase("items","{\"hat\":\"cap\",\"hand\":\"cap\"}")]
    public void InvalidEquipmentClaims_AreRejected(string field,string value)
    { var claims=Claims();claims[field]=JToken.Parse(value);Assert.That(verifier.TryVerify(Sign(claims),Challenge(),1900000010,"player",null,out _),Is.False); }
    [Test] public void OlderRevisionAndSameRevisionDifferentLoadout_AreRejected()
    {
        var previous=new EquipmentLoadout(3,new Dictionary<string,string>{{"hat","cap"}});Assert.That(verifier.TryVerify(Sign(),Challenge(),1900000010,"player",previous,out _),Is.False);
        previous=new EquipmentLoadout(2,new Dictionary<string,string>());Assert.That(verifier.TryVerify(Sign(),Challenge(),1900000010,"player",previous,out _),Is.False);
    }
    [Test] public void MissingRevisionReplacedByUnknownClaim_IsRejectedWithoutThrowing()
    { var claims=Claims();claims.Remove("revision");claims["unknown"]=2;Assert.That(verifier.TryVerify(Sign(claims),Challenge(),1900000010,"player",null,out _),Is.False); }
    [Test] public void FullBoundedLoadout_WithLongStableIds_IsSupported()
    {
        var claims=Claims();var items=new JObject();for(int index=0;index<32;index++) items[new string('s',120)+index]=new string('i',120)+index;claims["items"]=items;
        Assert.That(verifier.TryVerify(Sign(claims),Challenge(),1900000010,"player",null,out var loadout),Is.True);
        Assert.That(loadout.ItemsBySlot.Count,Is.EqualTo(32));
    }
    [Test] public void IdentityTypeAndTamperedSignature_CannotPublishEquipment()
    {
        Assert.That(verifier.TryVerify(Sign(type:"liveops-identity+jwt"),Challenge(),1900000010,"player",null,out _),Is.False);
        string signed=Sign();var parts=signed.Split('.');parts[1]=Encode(Encoding.UTF8.GetBytes(Claims().ToString(Formatting.None).Replace("cap","forged")));Assert.That(verifier.TryVerify(string.Join(".",parts),Challenge(),1900000010,"player",null,out _),Is.False);
    }
}
