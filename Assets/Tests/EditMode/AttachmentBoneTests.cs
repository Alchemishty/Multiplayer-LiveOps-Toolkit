using NUnit.Framework;

[TestFixture]
public class AttachmentBoneTests
{
    [Test]
    public void GetAttachmentBoneName_Headwear_ReturnsHead()
    {
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Headwear));
    }

    [Test]
    public void GetAttachmentBoneName_Hair_ReturnsHead()
    {
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Hair));
    }

    [Test]
    public void GetAttachmentBoneName_Facewear_ReturnsHead()
    {
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Facewear));
    }

    [Test]
    public void GetAttachmentBoneName_MainHand_ReturnsRightHand()
    {
        Assert.AreEqual("RightHand", AccessoryPool.GetAttachmentBoneName(AccessoryType.MainHand));
    }

    [Test]
    public void GetAttachmentBoneName_OffHand_ReturnsLeftHand()
    {
        Assert.AreEqual("LeftHand", AccessoryPool.GetAttachmentBoneName(AccessoryType.OffHand));
    }

    [Test]
    public void GetAttachmentBoneName_Back_ReturnsSpine2()
    {
        Assert.AreEqual("Spine2", AccessoryPool.GetAttachmentBoneName(AccessoryType.Back));
    }

    [Test]
    public void GetAttachmentBoneName_DefaultSlot_ReturnsHead()
    {
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Torso));
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Arms));
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Legs));
        Assert.AreEqual("Head", AccessoryPool.GetAttachmentBoneName(AccessoryType.Feet));
    }
}
