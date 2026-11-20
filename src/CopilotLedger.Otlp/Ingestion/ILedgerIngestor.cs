using CopilotLedger.Otlp.Decoding;

namespace CopilotLedger.Otlp.Ingestion;

public interface ILedgerIngestor
{
    Task<IngestResult> IngestAsync(OtlpTraceRequest request, CancellationToken cancellationToken = default);
}
