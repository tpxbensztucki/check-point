namespace CheckPoint.Api.Domain;

public enum CatchUpStatus
{
    Pending,

    // Set once its outcome is recorded — see the Record LM-Practice Lead
    // catch-up outcome story (Milestone 8, not built yet), which will add the
    // outcome fields alongside this transition.
    Recorded,
}
