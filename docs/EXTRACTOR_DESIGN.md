# Extractor & Validation Engine Design

## 1. Dual-Tier Extraction Architecture

In commercial invoice ingestion pipelines, relying solely on third-party LLM APIs introduces latency, token costs, rate limits, and non-deterministic hallucination risks. Conversely, pure regex parsers struggle with unstructured scans.

`DocumentOps` adopts a **hybrid dual-tier pipeline**:

1. **Deterministic Rule-Based Extractor (`RegexDocumentExtractor`)**:
   - Executes 100% offline with sub-millisecond execution times.
   - Utilizes negative lookbehind assertions (`(?<!sub[- ])\btotal`) to prevent subtotal collisions.
   - Iterates line-by-line to prevent multi-line token overlap between generic `"INVOICE"` headers and specific `"Invoice Number: ..."` keys.
   - Supports ISO 8601, US (`MM/DD/YYYY`), UK/EU (`DD/MM/YYYY`), and long month name date notations.
   - Extracts structured multi-line item tables (Description, Quantity, Unit Price, Line Total).

2. **Pluggable AI / OCR Interface (`IDocumentExtractor`)**:
   - Defines a standardized asynchronous contract:
     ```csharp
     public interface IDocumentExtractor
     {
         string ExtractorName { get; }
         Task<ExtractionResult> ExtractAsync(string rawText, CancellationToken ct = default);
     }
     ```
   - Ready for drop-in adapters connecting to OpenAI (JSON Mode / Structured Outputs), Anthropic Claude, or Azure AI Document Intelligence.
   - Graceful fallback: If external AI services encounter rate limits or network failures, the engine automatically falls back to the deterministic engine without workflow disruption.

---

## 2. Invariant Validation Engine Rules

The validation engine executes 7 cross-field invariant checks across financial, temporal, and completeness dimensions:

| Rule Code | Field | Severity | Logic / Invariant |
| :--- | :--- | :---: | :--- |
| `INV_MISSING_NUMBER` | `InvoiceNumber` | **Error** | Fails if invoice number is null, empty, or unextracted. |
| `INV_MISSING_VENDOR` | `VendorName` | **Error** | Fails if vendor or supplier header is unidentified. |
| `INV_ZERO_TOTAL` | `TotalAmount` | **Error** | Fails if `TotalAmount <= 0.00`. |
| `INV_NEGATIVE_AMOUNTS` | `Amounts` | **Error** | Fails if `Subtotal < 0` or `TaxAmount < 0`. |
| `INV_ARITHMETIC_MISMATCH` | `TotalAmount` | **Error** | Fails if `\|(Subtotal + TaxAmount) - TotalAmount\| > 0.01`. Protects against calculation errors. |
| `INV_LINE_ITEMS_SUM_MISMATCH` | `LineItems` | **Warning** | Flags when `LineItems.Count > 0` and `\|Sum(LineTotal) - Subtotal\| > 0.05`. |
| `INV_DUE_BEFORE_ISSUE` | `DueDate` | **Warning** | Flags if `DueDate < IssueDate` (temporal inversion error). |
| `INV_SUSPICIOUS_DATE` | `IssueDate` | **Warning** | Flags if date is older than 5 years or more than 3 months in the future. |
| `INV_DUPLICATE_NUMBER` | `InvoiceNumber` | **Error** | Flags if an invoice with the same vendor name and invoice number is already stored. |

---

## 3. Weighted Confidence Scoring Formula

The confidence score $C \in [0.0, 1.0]$ is dynamically computed based on positive field completeness signals and negative discrepancy deductions:

$$C = \text{clamp}\left(\sum S_{\text{positive}} - \sum P_{\text{penalties}}, 0.0, 1.0\right)$$

### Positive Signals ($S_{\text{positive}}$)
- **Invoice Number extracted**: $+0.20$
- **Vendor Name extracted**: $+0.20$
- **Total Amount positive**: $+0.20$
- **Issue Date identified**: $+0.15$
- **Subtotal + Tax == Total reconciliation**: $+0.15$
- **Line items present and sum matches subtotal**: $+0.10$

### Discrepancy Penalties ($P_{\text{penalties}}$)
- **Each unresolved `Error` discrepancy**: $-0.35$
- **Each unresolved `Warning` discrepancy**: $-0.10$

### Decision Boundary
- If $C \ge 0.85$ and unresolved errors $= 0 \implies$ **Auto-Approved**
- If $C < 0.85$ or unresolved errors $> 0 \implies$ **Requires Human Review (HITL)**
