namespace CheckPoint.Api.Services;

public enum RequestDispatchMode
{
    Automatic,
    Manual,
}

// Interim, until CBLT-254 (Admin Settings: Configure automatic vs. manual request
// send toggle) exists — that story will move this to a DB-backed, runtime-editable
// setting. Until then, binding from configuration (appsettings/environment
// variables) still satisfies "takes effect without needing a deployment" (CBLT-234's
// own AC), it just isn't editable through the app itself yet.
public class RequestDispatchOptions
{
    public const string SectionName = "RequestDispatch";

    public RequestDispatchMode Mode { get; set; } = RequestDispatchMode.Automatic;
}
