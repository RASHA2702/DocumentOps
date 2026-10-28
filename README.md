# DocumentOps — Automated Invoice Ingestion & Verification Engine

> **Enterprise-grade document processing pipeline built with .NET 8 Clean Architecture, featuring deterministic regex extraction, an extensible document intelligence pipeline, cross-field arithmetic validation, international tax ID compliance verification, and a Human-in-the-Loop (HITL) review queue for automated ERP ingestion.**

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/Tests-40%20Passed%20(100%25)-success)](https://github.com/RASHA2702/DocumentOps)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Clean Architecture](https://img.shields.io/badge/Architecture-Clean%20%2F%20DDD-green)](docs/ARCHITECTURE.md)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker&logoColor=white)](Dockerfile)

---

## 1. The Commercial Problem

Small and mid-sized enterprises (agencies, logistics firms, retail distributors, accounting practices) spend hundreds of hours manually keying vendor invoices and receipts into accounting software like QuickBooks, Xero, and SAP.

While modern OCR and vision models can extract raw text, commercial operations face three critical risks:
1. **Extraction & Arithmetic Errors**: Unvalidated models regularly misread line items or miscalculate sales taxes, corrupting accounting ledgers.
2. **Vendor Rate Limits & Cost**: Sending thousands of repetitive invoices to external cloud APIs introduces latency, recurring fees, and API downtime.
3. **Lack of Human Oversight & Tax Fraud Risks**: Without a robust Human-in-the-Loop (HITL) review mechanism with blocking guards and VAT/EIN tax syntax verification, error-ridden or fraudulent invoices get posted directly into financial ledgers.

**DocumentOps** provides a self-hosted solution that combines **instant offline deterministic extraction**, **cross-field arithmetic validation**, **international tax ID syntax checks**, **rule-weighted confidence scoring**, and an **auditable human review workflow** before producing standard ERP export payloads.

---

## 2. System Architecture

```mermaid
flowchart TD
    RawDoc["Uploaded Document / Batch Payload"] --> Hash["SHA-256 Deduplication Guard"]
    Hash --> Extract["IDocumentExtractor (Deterministic / Pluggable AI)"]
    Extract --> Val["Validation Engine (10 Invariant Rules)"]
    Val --> Score["Confidence Scorer (0.00 - 1.00 Heuristic Rule-Weights)"]
    Score --> Decision{"Score >= 0.85 & Errors == 0?"}
    Decision -- Yes --> AutoApprove["Status: Approved (System Auto-Approval)"]
    Decision -- No --> ReviewQueue["Status: RequiresReview (HITL Queue)"]
    ReviewQueue --> Auditor["Auditor Field Editing & Discrepancy Resolution"]
    Auditor --> AutoApprove
    AutoApprove --> ERPExport["ERP Export Service (UBL-modeled JSON / CWE-1236 Sanitized CSV)"]
    ERPExport --> FinalStatus["Status: Exported"]
```

### Clean Architecture Solution Layout
- **`DocumentOps.Core`**: Pure domain entities (`DocumentRecord`, `InvoiceData`, `InvoiceLineItem`, `ValidationDiscrepancy`, `DocumentAuditLog`) and status enums. Zero external dependencies.
- **`DocumentOps.Application`**: Use cases, validation engine (`IValidationEngine`), tax ID compliance (`ITaxIdValidator`), confidence scoring (`IConfidenceScorer`), and orchestrators (`DocumentProcessingService`, `DocumentReviewService`).
- **`DocumentOps.Infrastructure`**: Deterministic rule extractor (`RegexDocumentExtractor`), pluggable AI adapter (`MockAiDocumentExtractor`), thread-safe repository (`InMemoryDocumentRepository`), and ERP exporter (`ErpExportService`).
- **`DocumentOps.Api`**: ASP.NET Core minimal Web API with batch processing, RFC 9457 `ProblemDetails` error handling, and health probes.
- **`DocumentOps.Tests`**: 40 automated tests covering extraction accuracy, arithmetic auditing, international tax ID checksums, batch processing resilience, currency subunit precision, line item duplicate detection, scoring penalties, approval guards, and CSV formula injection defense.

---

## 3. Key Capabilities

### 🔍 Deterministic & Extensible Extraction Pipeline
- **Sub-Millisecond Regex Parser**: Extracts vendor name, invoice number, issue/due dates, tax ID, currency, subtotal, tax amount, and multi-line item tables 100% offline.
- **Extensible Pipeline Interface**: Standardized `IDocumentExtractor` contract allows drop-in integration with document intelligence APIs and vision models, with automatic fallback to deterministic parsing upon rate limiting or network downtime.

### 🛡️ Cross-Field Arithmetic & Currency Invariant Engine
The validation engine continuously audits financial documents for arithmetic, currency precision, temporal consistency, and tax ID compliance across **10 invariant rules**:
- **Cross-field reconciliation**: Enforces `|(Subtotal + Tax) - Total| <= 0.01`. Any discrepancy triggers a blocking `INV_ARITHMETIC_MISMATCH` error.
- **Line item summation**: Verifies that the sum of line items equals the reported subtotal.
- **Currency Subunit Precision (ISO 4217)**: Flags `INV_CURRENCY_PRECISION_MISMATCH` if amounts contain fractional cents in zero-decimal currencies (JPY, KRW, VND) or exceed standard currency decimal limits.
- **Line Item Detail & Duplicate Detection**: Enforces positive quantities, non-negative unit prices, line item calculation accuracy, and flags potential duplicate line items (`INV_DUPLICATE_LINE_ITEM`).
- **International Tax ID Validation (Rule 10)**: Validates vendor tax identifiers against country-specific syntax and checksums:
  - **US EIN**: 9-digit format (`XX-XXXXXXX` or unhyphenated 9 digits) with valid campus prefixes.
  - **UK VAT**: Standard 9-digit format (`GB` prefix + 9 digits or unhyphenated 9 digits).
  - **EU VAT**: Standard alphanumeric country prefixes and length invariants across member states (e.g. `DE`, `FR`, `IT`, `ES`, `NL`).
  - **Australian ABN**: 11-digit format verified via the official **Australian Business Register Modulo-89 algorithm** (`weights: [10, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19]`). Flagged with `INV_TAX_ID_SYNTAX_INVALID`.
- **Temporal verification**: Detects date anomalies such as `DueDate < IssueDate` or suspicious future/historical dates.
- **Duplicate prevention**: Flags duplicate invoice numbers from identical vendors (`INV_DUPLICATE_NUMBER`) and avoids duplicate processing via SHA-256 document hashing.

### ⚡ Resilient Concurrent Batch Processing
- **Parallel Document Pipeline**: Ingests batches of up to 50 documents simultaneously via `POST /api/documents/batch-upload` using bounded worker concurrency (`Parallel.ForEachAsync` with `Environment.ProcessorCount * 2` max parallelism).
- **Failure Boundary Isolation**: Individual extraction or validation failures in bad documents do not abort the entire batch. Successful invoices are persisted and scored, while failures return descriptive error objects in the aggregate `BatchProcessingSummaryDto`.

### ⚖️ Weighted Confidence Scoring
- Calculates a transparent composite confidence score $C \in [0.0, 1.0]$ based on deterministic rule weights ($+0.20$ for invoice #, $+0.20$ for vendor, $+0.15$ for arithmetic reconciliation, $+0.10$ for line item match) minus penalties for discrepancies ($-0.35$ for errors, $-0.10$ for warnings).
- **Auto-Approval**: Documents with $C \ge 0.85$ and 0 errors bypass manual queues. Flawed documents are safely routed to the human review queue.

### 👥 Human-in-the-Loop (HITL) Review Queue
- Reviewers inspect flagged discrepancies with clear expected vs. actual values.
- Reviewers can update extracted fields and resolve discrepancy flags.
- **Strict Approval Guards**: The system prohibits approval if any unresolved blocking `Error` exists.

### 📤 Multi-Format ERP Export & Security
- **UBL-Modeled JSON**: Structured accounting schema modeled on UBL 2.1 invoice conventions for automated ingestion by enterprise accounting APIs.
- **Sanitized General Ledger CSV (CWE-1236 Defense)**: Formatted tabular CSV including header metadata and itemized transaction rows, protected against spreadsheet formula execution (`=`, `+`, `-`, `@`, tab, CR prefix neutralization).

---

## 4. API Endpoints Reference

| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/health` | Service health check and operational status probe. |
| `GET` | `/api/dashboard/metrics` | High-level KPIs: Auto-approval rate, review backlog, total approved spend. |
| `GET` | `/api/documents` | Paginated document listing with filtering by `status` and `search`. |
| `GET` | `/api/documents/{id}` | Full document detail with extracted fields, line items, and discrepancies. |
| `POST` | `/api/documents/upload` | Upload single raw document text or file for automated processing. |
| `POST` | `/api/documents/batch-upload` | Batch upload up to 50 documents with parallel worker execution and failure isolation. |
| `PUT` | `/api/documents/{id}/review` | Human review update endpoint to modify fields and resolve flags. |
| `POST` | `/api/documents/{id}/approve` | Mark document as approved for ERP export (blocked if errors exist). |
| `POST` | `/api/documents/{id}/reject` | Reject invalid document with recorded reason. |
| `GET` | `/api/documents/{id}/export` | Export approved invoice in `format=json` (UBL-modeled) or `format=csv`. |

---

## 5. Quickstart & Local Execution

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher.
- [Docker](https://www.docker.com/) (optional, for containerized execution).

### Option A: Run with .NET CLI
```powershell
# Clone the repository
git clone https://github.com/RASHA2702/DocumentOps.git
cd DocumentOps

# Run the automated test suite
dotnet test tests/DocumentOps.Tests/DocumentOps.Tests.csproj

# Run the API server
dotnet run --project src/DocumentOps.Api/DocumentOps.Api.csproj
```
The server will start at `http://localhost:5080`.

### Option B: Run with Docker Compose
```bash
docker compose up -d --build
```

---

## 6. Live API Verification (Sample Curl Commands)

### 1. Ingest an Invoice via API
```bash
curl -X POST http://localhost:5080/api/documents/upload \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "acme-cloud.txt",
    "rawText": "Acme Cloud Services\nINVOICE\nInvoice #: INV-9901\nDate: 2026-10-01\nDue Date: 2026-10-31\nTax ID: 12-3456789\n\nKubernetes Cluster 1 $1,000.00 $1,000.00\n\nSubtotal: $1,000.00\nTax: $100.00\nTotal: $1,100.00"
  }'
```

### 2. Ingest a Batch of Documents Concurrently
```bash
curl -X POST http://localhost:5080/api/documents/batch-upload \
  -H "Content-Type: application/json" \
  -d '[
    {
      "fileName": "invoice-us.txt",
      "rawText": "Vendor US Inc\nInvoice #: US-101\nDate: 2026-10-01\nTax ID: 12-3456789\nSubtotal: $500.00\nTax: $50.00\nTotal: $550.00"
    },
    {
      "fileName": "invoice-au.txt",
      "rawText": "Sydney Logistics Pty Ltd\nInvoice #: AU-202\nDate: 2026-10-01\nTax ID: 51 824 753 556\nSubtotal: $800.00\nTax: $80.00\nTotal: $880.00"
    }
  ]'
```

### 3. Check Dashboard Metrics
```bash
curl http://localhost:5080/api/dashboard/metrics
```
**Response:**
```json
{
  "totalDocuments": 3,
  "requiresReviewCount": 2,
  "approvedCount": 1,
  "exportedCount": 0,
  "rejectedCount": 0,
  "autoApprovalRatePercent": 33.3,
  "averageConfidencePercent": 79.3,
  "totalApprovedSpend": 1440.00
}
```

### 4. Export an Approved Document to Accounting JSON
```bash
curl "http://localhost:5080/api/documents/11111111-1111-1111-1111-111111111111/export?format=json"
```

---

## 7. Automated Test Suite

```powershell
dotnet test tests/DocumentOps.Tests/DocumentOps.Tests.csproj
```

**Results:**
```text
Test run for DocumentOps.Tests.dll (.NETCoreApp,Version=v8.0)
Passed!  - Failed: 0, Passed: 40, Skipped: 0, Total: 40, Duration: 1 s
```

### Test Coverage Highlights
- Standard US & European currency and date format parsing.
- Fallback subtotal calculation from parsed line items.
- Arithmetic discrepancy detection (`INV_ARITHMETIC_MISMATCH`).
- Currency subunit precision validation (`INV_CURRENCY_PRECISION_MISMATCH`).
- International Tax ID validation for US EIN, UK VAT, EU VAT, and Australian ABN Modulo-89 algorithm (`INV_TAX_ID_SYNTAX_INVALID`).
- Concurrent batch document ingestion with failure boundary isolation (`BatchProcessingTests`).
- Line item quantity, negative price, and duplicate description detection (`INV_DUPLICATE_LINE_ITEM`).
- Temporal inversion detection (`INV_DUE_BEFORE_ISSUE`).
- SHA-256 duplicate document detection.
- Approval barrier blocking unverified error states.
- UBL-modeled JSON and sanitized CSV export formatting with CWE-1236 formula defense.

---

## 8. Author & Contact

Built by **Rashad** — Full-Stack .NET & Angular Software Engineer
- **GitHub**: [@RASHA2702](https://github.com/RASHA2702)
- **Email**: [rashadhaq7@gmail.com](mailto:rashadhaq7@gmail.com)
- **Available for**: Remote contracts, system architecture consulting, and custom ERP integration development.
