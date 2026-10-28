using DocumentOps.Application.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DocumentOps.Tests;

[TestClass]
public class TaxIdValidatorTests
{
    private readonly TaxIdValidator _validator = new();

    [TestMethod]
    [DataRow("12-3456789", "US")]
    [DataRow("123456789", "US")]
    public void ValidateTaxId_UsEin_ValidFormats_Passes(string taxId, string expectedCountry)
    {
        var result = _validator.ValidateTaxId(taxId, "US");
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(expectedCountry, result.CountryCode);
    }

    [TestMethod]
    [DataRow("GB123456789", "GB")]
    [DataRow("123456789", "GB")]
    public void ValidateTaxId_UkVat_ValidFormats_Passes(string taxId, string fallbackCountry)
    {
        var result = _validator.ValidateTaxId(taxId, fallbackCountry);
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("GB", result.CountryCode);
    }

    [TestMethod]
    public void ValidateTaxId_AustralianAbn_ValidModulo89_Passes()
    {
        // 51 824 753 556 is a verified valid Australian Government ABN (Australian Taxation Office)
        var result = _validator.ValidateTaxId("51 824 753 556", "AU");
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("AU", result.CountryCode);
    }

    [TestMethod]
    public void ValidateTaxId_AustralianAbn_InvalidChecksum_Fails()
    {
        // 51 824 753 557 (last digit changed from 6 to 7)
        var result = _validator.ValidateTaxId("51 824 753 557", "AU");
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.ErrorMessage!.Contains("Modulo-89"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("!@#$%^")]
    public void ValidateTaxId_EmptyOrMalformed_Fails(string taxId)
    {
        var result = _validator.ValidateTaxId(taxId);
        Assert.IsFalse(result.IsValid);
    }
}
