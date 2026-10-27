# DocumentOps — Testing & Quality Assurance

## 1. Test Strategy

The testing suite validates core business rules, edge-case invoice formats, arithmetic reconciliations, and state machine transition guards across all layers:

1. **Extraction Tests (`RegexDocumentExtractorTests`)**:
   - Standard US invoice with currency symbol, tax, and line items table.
   - European invoice with Euro symbols, German invoice headers (`Rechnung`), and alphanumeric VAT IDs.
   - Invoices missing explicit subtotal labels, verifying fallback calculation by summing line items.

2. **Validation Engine Tests (`ValidationEngineTests`)**:
   - Verification of zero-discrepancy clean invoices.
   - Cross-field arithmetic mismatch detection (`INV_ARITHMETIC_MISMATCH`).
   - Temporal sequence verification (`DueDate < IssueDate`).
   - Completeness guards for missing vendor names and invoice numbers.
   - Multi-line item subtotal discrepancy detection.

3. **Scoring Tests (`ConfidenceScorerTests`)**:
   - High-score attribution for complete, accurate documents.
   - Significant score penalty upon encountering blocking errors.
   - Dynamic score restoration when an auditor resolves discrepancies.

4. **Workflow & End-to-End Tests (`DocumentProcessingAndReviewTests`)**:
   - Automatic approval workflow for high-confidence documents.
   - Review routing workflow for mathematically flawed invoices.
   - SHA-256 deduplication logic.
   - Approval guards blocking approval until errors are resolved.
   - Human review edit propagation and discrepancy resolution.
   - UBL 2.1 JSON export generation and ledger CSV serialization.
   - Dashboard KPI metric aggregation.

---

## 2. Running Automated Tests Locally

```powershell
# Run the complete test suite
dotnet test tests/DocumentOps.Tests/DocumentOps.Tests.csproj

# Run tests with detailed test execution output
dotnet test tests/DocumentOps.Tests/DocumentOps.Tests.csproj --verbosity normal
```

### Verified Test Outcome
```
Passed!  - Failed:     0, Passed:    19, Skipped:     0, Total:    19, Duration: 875 ms - DocumentOps.Tests.dll (net8.0)
```
100% automated test pass rate across all 19 test cases.
