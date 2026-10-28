using System.Text.RegularExpressions;

namespace DocumentOps.Application.Services;

public record TaxIdValidationResult(bool IsValid, string? CountryCode, string? NormalizedTaxId, string? ErrorMessage)
{
    public static TaxIdValidationResult Success(string countryCode, string normalized) =>
        new(true, countryCode, normalized, null);

    public static TaxIdValidationResult Failure(string errorMessage) =>
        new(false, null, null, errorMessage);
}

public interface ITaxIdValidator
{
    TaxIdValidationResult ValidateTaxId(string? taxId, string? fallbackCountryCode = null);
}

public class TaxIdValidator : ITaxIdValidator
{
    private static readonly Regex UsEinRegex = new(@"^\d{2}-?\d{7}$", RegexOptions.Compiled);
    private static readonly Regex UkVatRegex = new(@"^(GB)?\d{9}(\d{3})?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeVatRegex = new(@"^DE\d{9}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FrVatRegex = new(@"^FR[A-Z0-9]{2}\d{9}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ItVatRegex = new(@"^IT\d{11}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EsVatRegex = new(@"^ES[A-Z0-9]\d{7}[A-Z0-9]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TaxIdValidationResult ValidateTaxId(string? taxId, string? fallbackCountryCode = null)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            return TaxIdValidationResult.Failure("Tax ID is empty.");
        }

        var clean = taxId.Trim().Replace(" ", "").Replace(".", "").ToUpperInvariant();

        // 1. Australian ABN (11 digits with Modulo-89 checksum)
        if (clean.StartsWith("AU", StringComparison.OrdinalIgnoreCase) || fallbackCountryCode?.ToUpperInvariant() == "AU" || (clean.Length == 11 && char.IsDigit(clean[0]) && fallbackCountryCode == "AU"))
        {
            var abnDigits = clean.StartsWith("AU") ? clean.Substring(2) : clean;
            if (abnDigits.Length == 11 && abnDigits.All(char.IsDigit))
            {
                if (IsValidAbn(abnDigits))
                {
                    return TaxIdValidationResult.Success("AU", $"AU {abnDigits.Substring(0, 2)} {abnDigits.Substring(2, 3)} {abnDigits.Substring(5, 3)} {abnDigits.Substring(8, 3)}");
                }
                return TaxIdValidationResult.Failure("Australian ABN failed official Modulo-89 checksum verification.");
            }
        }

        // 2. UK VAT (GB + 9 digits or fallbackCountryCode == "GB")
        if (clean.StartsWith("GB") || (fallbackCountryCode?.ToUpperInvariant() == "GB" && clean.Length == 9))
        {
            if (UkVatRegex.IsMatch(clean))
            {
                var digits = clean.StartsWith("GB") ? clean.Substring(2) : clean;
                return TaxIdValidationResult.Success("GB", $"GB{digits}");
            }
            return TaxIdValidationResult.Failure("UK VAT format must consist of 'GB' followed by 9 digits.");
        }

        // 3. US EIN (9 digits: XX-XXXXXXX)
        if (clean.StartsWith("US") || fallbackCountryCode?.ToUpperInvariant() == "US" || UsEinRegex.IsMatch(clean))
        {
            var einDigits = clean.StartsWith("US") ? clean.Substring(2).Replace("-", "") : clean.Replace("-", "");
            if (einDigits.Length == 9 && einDigits.All(char.IsDigit))
            {
                return TaxIdValidationResult.Success("US", $"{einDigits.Substring(0, 2)}-{einDigits.Substring(2)}");
            }
        }

        // 4. EU VAT Member States
        if (clean.StartsWith("DE"))
        {
            return DeVatRegex.IsMatch(clean)
                ? TaxIdValidationResult.Success("DE", clean)
                : TaxIdValidationResult.Failure("German VAT must match DE + 9 digits.");
        }

        if (clean.StartsWith("FR"))
        {
            return FrVatRegex.IsMatch(clean)
                ? TaxIdValidationResult.Success("FR", clean)
                : TaxIdValidationResult.Failure("French VAT must match FR + 11 characters.");
        }

        if (clean.StartsWith("IT"))
        {
            return ItVatRegex.IsMatch(clean)
                ? TaxIdValidationResult.Success("IT", clean)
                : TaxIdValidationResult.Failure("Italian VAT must match IT + 11 digits.");
        }

        if (clean.StartsWith("ES"))
        {
            return EsVatRegex.IsMatch(clean)
                ? TaxIdValidationResult.Success("ES", clean)
                : TaxIdValidationResult.Failure("Spanish VAT must match ES + 9 alphanumeric characters.");
        }

        // Generic alphanumeric tax ID (e.g. other international jurisdictions)
        if (clean.Length >= 6 && clean.Length <= 20 && clean.All(c => char.IsLetterOrDigit(c) || c == '-'))
        {
            return TaxIdValidationResult.Success("GENERIC", clean);
        }

        return TaxIdValidationResult.Failure($"Tax ID '{taxId}' does not conform to recognized national formats (US EIN, UK VAT, EU VAT, AU ABN).");
    }

    public static bool IsValidAbn(string abn)
    {
        if (abn.Length != 11 || !abn.All(char.IsDigit)) return false;

        // Australian Business Register Modulo-89 algorithm:
        // Weights: 10, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19
        // Subtract 1 from the first digit, multiply by weights, sum must be divisible by 89
        int[] weights = { 10, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19 };
        int sum = 0;

        for (int i = 0; i < 11; i++)
        {
            int digit = abn[i] - '0';
            if (i == 0) digit -= 1;
            sum += digit * weights[i];
        }

        return sum % 89 == 0;
    }
}
