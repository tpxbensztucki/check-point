namespace CheckPoint.Api.Contracts;

// Per-POC outcome for one FeedbackRequest (spec Section 5.4, 9) — deliberately
// never a single status on the request as a whole, since different POCs on the
// same request can be in different states (CBLT-237).
public enum PocResponseStatus
{
    NotYetSent,
    Sent,
    Submitted,
    NoResponse,
    Cancelled,
}

public record PocResponseStatusEntry(Guid PocId, string PocName, string PocEmail, PocResponseStatus Status);
