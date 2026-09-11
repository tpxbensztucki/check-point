namespace CheckPoint.Api.Contracts;

// Returned when a magic link is Valid — the guest never enters a request id
// themselves, they only ever hold the token from the link URL.
public record MagicLinkViewResponse(Guid FeedbackRequestId);
