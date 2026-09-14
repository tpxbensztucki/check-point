namespace CheckPoint.Api.Contracts;

// Returned when a magic link is Valid — the guest never enters a request id
// themselves, they only ever hold the token from the link URL.
public record MagicLinkViewResponse(Guid FeedbackRequestId);

// Mirrors FeedbackForm's three fields on the frontend (CBLT-232) — validated
// again here since the API is publicly reachable and must not trust client-side
// validation alone.
public record SubmitFeedbackRequest(string DoingWell, string NotDoingWell, string NeedsToImprove);
